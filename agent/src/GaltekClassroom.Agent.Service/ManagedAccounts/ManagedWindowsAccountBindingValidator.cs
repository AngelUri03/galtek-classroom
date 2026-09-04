using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.ManagedAccounts;

public enum ManagedWindowsAccountBindingValidationStatus
{
    Valid,
    Invalid
}

public sealed record ManagedWindowsAccountBindingValidationResult(
    ManagedWindowsAccountBindingValidationStatus Status,
    string? ErrorMessage)
{
    public bool IsValid => Status == ManagedWindowsAccountBindingValidationStatus.Valid;

    public static ManagedWindowsAccountBindingValidationResult Valid()
    {
        return new ManagedWindowsAccountBindingValidationResult(
            ManagedWindowsAccountBindingValidationStatus.Valid,
            null);
    }

    public static ManagedWindowsAccountBindingValidationResult Invalid(string errorMessage)
    {
        return new ManagedWindowsAccountBindingValidationResult(
            ManagedWindowsAccountBindingValidationStatus.Invalid,
            errorMessage);
    }
}

public static class ManagedWindowsAccountBindingValidator
{
    private static readonly string[] OrderedAccountIds =
    [
        ClassroomManagedWindowsAccountTypes.Primary,
        ClassroomManagedWindowsAccountTypes.Secondary
    ];

    public static ManagedWindowsAccountBindingValidationResult ValidateAccountId(string? accountId)
    {
        return ManagedWindowsAccountBinding.IsValidAccountId(accountId)
            ? ManagedWindowsAccountBindingValidationResult.Valid()
            : ManagedWindowsAccountBindingValidationResult.Invalid(
                "Managed Windows accountId must be PRIMARY or SECONDARY.");
    }

    public static ManagedWindowsAccountBindingValidationResult ValidateBinding(
        ManagedWindowsAccountBinding? binding)
    {
        if (binding is null)
        {
            return ManagedWindowsAccountBindingValidationResult.Invalid(
                "managed-windows-accounts.json contains an empty binding.");
        }

        var accountId = ValidateAccountId(binding.AccountId);
        if (!accountId.IsValid)
        {
            return accountId;
        }

        if (!string.Equals(
            binding.AccountId,
            ManagedWindowsAccountBinding.NormalizeAccountId(binding.AccountId),
            StringComparison.Ordinal))
        {
            return ManagedWindowsAccountBindingValidationResult.Invalid(
                "managed-windows-accounts.json accountId must be canonical PRIMARY or SECONDARY.");
        }

        if (!MasterBindingValidator.IsValidSid(binding.WindowsSid))
        {
            return ManagedWindowsAccountBindingValidationResult.Invalid(
                "managed-windows-accounts.json windowsSid is invalid.");
        }

        if (string.IsNullOrWhiteSpace(binding.AccountReference))
        {
            return ManagedWindowsAccountBindingValidationResult.Invalid(
                "managed-windows-accounts.json accountReference is missing.");
        }

        if (binding.AccountReference.Any(char.IsControl))
        {
            return ManagedWindowsAccountBindingValidationResult.Invalid(
                "managed-windows-accounts.json accountReference contains control characters.");
        }

        if (binding.CreatedAtUtc == default)
        {
            return ManagedWindowsAccountBindingValidationResult.Invalid(
                "managed-windows-accounts.json createdAtUtc is missing.");
        }

        if (binding.UpdatedAtUtc == default)
        {
            return ManagedWindowsAccountBindingValidationResult.Invalid(
                "managed-windows-accounts.json updatedAtUtc is missing.");
        }

        return ManagedWindowsAccountBindingValidationResult.Valid();
    }

    public static ManagedWindowsAccountBindingValidationResult ValidateCatalogDocument(
        ManagedWindowsAccountBindingCatalogDocument? document,
        Guid currentInstallationId)
    {
        if (document is null)
        {
            return ManagedWindowsAccountBindingValidationResult.Invalid(
                "managed-windows-accounts.json is empty.");
        }

        if (document.SchemaVersion != ManagedWindowsAccountBindingConstants.SchemaVersion)
        {
            return ManagedWindowsAccountBindingValidationResult.Invalid(
                "managed-windows-accounts.json uses an unsupported schemaVersion.");
        }

        if (document.InstallationId == Guid.Empty)
        {
            return ManagedWindowsAccountBindingValidationResult.Invalid(
                "managed-windows-accounts.json installationId is missing.");
        }

        if (document.InstallationId != currentInstallationId)
        {
            return ManagedWindowsAccountBindingValidationResult.Invalid(
                "managed-windows-accounts.json belongs to another installationId.");
        }

        if (document.Bindings is null)
        {
            return ManagedWindowsAccountBindingValidationResult.Invalid(
                "managed-windows-accounts.json bindings are missing.");
        }

        if (document.Bindings.Count > OrderedAccountIds.Length)
        {
            return ManagedWindowsAccountBindingValidationResult.Invalid(
                "managed-windows-accounts.json contains too many bindings.");
        }

        var accountIds = new HashSet<string>(StringComparer.Ordinal);
        var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var binding in document.Bindings)
        {
            var bindingValidation = ValidateBinding(binding);
            if (!bindingValidation.IsValid)
            {
                return bindingValidation;
            }

            if (!accountIds.Add(binding.AccountId))
            {
                return ManagedWindowsAccountBindingValidationResult.Invalid(
                    $"managed-windows-accounts.json contains duplicate {binding.AccountId} binding.");
            }

            if (!sids.Add(binding.WindowsSid))
            {
                return ManagedWindowsAccountBindingValidationResult.Invalid(
                    "managed-windows-accounts.json binds PRIMARY and SECONDARY to the same SID.");
            }
        }

        return ManagedWindowsAccountBindingValidationResult.Valid();
    }

    public static IReadOnlyList<string> OrderedSlots => OrderedAccountIds;
}
