using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.ManagedAccounts;

public enum ManagedWindowsAccountBindingConfigurationStatus
{
    Success,
    AdministratorRequired,
    InstallationIdentityInvalid,
    WindowsAccountNotFound,
    ManagedAccountBindingsInvalid,
    ManagedAccountBindingAlreadyExists,
    ManagedAccountBindingNotFound,
    ManagedAccountBindingConflict,
    ManagedAccountBindingInvalid
}

public static class ManagedWindowsAccountBindingConfigurationStatusExtensions
{
    public static string ToCode(this ManagedWindowsAccountBindingConfigurationStatus status)
    {
        return status switch
        {
            ManagedWindowsAccountBindingConfigurationStatus.Success => "SUCCESS",
            ManagedWindowsAccountBindingConfigurationStatus.AdministratorRequired => ManagedWindowsAccountBindingErrorCodes.AdministratorRequired,
            ManagedWindowsAccountBindingConfigurationStatus.InstallationIdentityInvalid => ManagedWindowsAccountBindingErrorCodes.InstallationIdentityInvalid,
            ManagedWindowsAccountBindingConfigurationStatus.WindowsAccountNotFound => ManagedWindowsAccountBindingErrorCodes.WindowsAccountNotFound,
            ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingsInvalid => ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingsInvalid,
            ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingAlreadyExists => ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingAlreadyExists,
            ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingNotFound => ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingNotFound,
            ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingConflict => ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingConflict,
            ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingInvalid => ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingInvalid,
            _ => ManagedWindowsAccountBindingErrorCodes.ManagedAccountBindingInvalid
        };
    }
}

public sealed record ManagedWindowsAccountBindingConfigurationResult(
    ManagedWindowsAccountBindingConfigurationStatus Status,
    bool Succeeded,
    IReadOnlyList<ManagedWindowsAccountView> Accounts,
    string? BlockingReason);

public sealed class ManagedWindowsAccountBindingConfigurationService
{
    private readonly InstallationIdentityResolver _installationIdentityResolver;
    private readonly IManagedWindowsAccountBindingStore _store;
    private readonly IWindowsAccountResolver _accountResolver;
    private readonly IAdministratorPrivilegeChecker _administratorPrivilegeChecker;
    private readonly ISystemClock _clock;

    public ManagedWindowsAccountBindingConfigurationService(
        InstallationIdentityResolver installationIdentityResolver,
        IManagedWindowsAccountBindingStore store,
        IWindowsAccountResolver accountResolver,
        IAdministratorPrivilegeChecker administratorPrivilegeChecker,
        ISystemClock clock)
    {
        _installationIdentityResolver = installationIdentityResolver;
        _store = store;
        _accountResolver = accountResolver;
        _administratorPrivilegeChecker = administratorPrivilegeChecker;
        _clock = clock;
    }

    public async Task<ManagedWindowsAccountBindingConfigurationResult> ListAsync(
        CancellationToken cancellationToken)
    {
        var identity = await ResolveInstallationIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity.Status != InstallationIdentityResolutionStatus.Ready)
        {
            return Failure(
                ManagedWindowsAccountBindingConfigurationStatus.InstallationIdentityInvalid,
                identity.ErrorMessage ?? "Installation identity is invalid.");
        }

        var load = await _store.ListAsync(identity.Identity!.InstallationId, cancellationToken).ConfigureAwait(false);
        return load.Loaded
            ? Success(BuildStatus(load.Bindings))
            : Failure(
                ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingsInvalid,
                load.ErrorMessage ?? "Managed Windows account bindings are invalid.");
    }

    public Task<ManagedWindowsAccountBindingConfigurationResult> BindAsync(
        string accountId,
        string windowsAccountReference,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        return MutateAsync(
            accountId,
            token => BindResolvedAccountAsync(accountId, windowsAccountReference, replaceExisting, token),
            cancellationToken);
    }

    public Task<ManagedWindowsAccountBindingConfigurationResult> RemoveAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        return MutateAsync(
            accountId,
            async token =>
            {
                var identity = await ResolveInstallationIdentityAsync(token).ConfigureAwait(false);
                if (identity.Status != InstallationIdentityResolutionStatus.Ready)
                {
                    return Failure(
                        ManagedWindowsAccountBindingConfigurationStatus.InstallationIdentityInvalid,
                        identity.ErrorMessage ?? "Installation identity is invalid.");
                }

                var write = await _store.RemoveAsync(
                    identity.Identity!.InstallationId,
                    accountId,
                    token).ConfigureAwait(false);

                return await ToMutationResultAsync(
                    identity.Identity.InstallationId,
                    write,
                    token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    private async Task<ManagedWindowsAccountBindingConfigurationResult> BindResolvedAccountAsync(
        string accountId,
        string windowsAccountReference,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(windowsAccountReference))
        {
            return Failure(
                ManagedWindowsAccountBindingConfigurationStatus.WindowsAccountNotFound,
                "Windows account reference is required.");
        }

        var normalizedAccountId = ManagedWindowsAccountBinding.NormalizeAccountId(accountId);
        var normalizedReference = WindowsAccountNameNormalizer.NormalizeAccountName(
            windowsAccountReference,
            Environment.MachineName);
        var account = _accountResolver.ResolveAccount(normalizedReference);
        if (!account.Found || account.Identity is null || account.Identity.SidNameUse != WindowsAccountSidNameUse.User)
        {
            return Failure(
                ManagedWindowsAccountBindingConfigurationStatus.WindowsAccountNotFound,
                account.ErrorMessage ?? "Windows account could not be resolved as a User account.");
        }

        var identity = await ResolveInstallationIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity.Status != InstallationIdentityResolutionStatus.Ready)
        {
            return Failure(
                ManagedWindowsAccountBindingConfigurationStatus.InstallationIdentityInvalid,
                identity.ErrorMessage ?? "Installation identity is invalid.");
        }

        var candidate = ManagedWindowsAccountBinding.Create(
            normalizedAccountId,
            account.Identity.WindowsSid,
            account.Identity.AccountDisplayName,
            _clock.UtcNow);

        var write = replaceExisting
            ? await _store.ReplaceAsync(identity.Identity!.InstallationId, candidate, cancellationToken).ConfigureAwait(false)
            : await _store.AddAsync(identity.Identity!.InstallationId, candidate, cancellationToken).ConfigureAwait(false);

        return await ToMutationResultAsync(
            identity.Identity.InstallationId,
            write,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedWindowsAccountBindingConfigurationResult> MutateAsync(
        string accountId,
        Func<CancellationToken, Task<ManagedWindowsAccountBindingConfigurationResult>> mutation,
        CancellationToken cancellationToken)
    {
        var idValidation = ManagedWindowsAccountBindingValidator.ValidateAccountId(accountId);
        if (!idValidation.IsValid)
        {
            return Failure(
                ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingInvalid,
                idValidation.ErrorMessage ?? "Managed Windows accountId is invalid.");
        }

        if (!_administratorPrivilegeChecker.IsElevatedAdministrator())
        {
            return Failure(
                ManagedWindowsAccountBindingConfigurationStatus.AdministratorRequired,
                "Changing managed Windows account bindings requires an elevated administrator shell.");
        }

        return await mutation(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedWindowsAccountBindingConfigurationResult> ToMutationResultAsync(
        Guid installationId,
        ManagedWindowsAccountBindingStoreWriteResult write,
        CancellationToken cancellationToken)
    {
        if (write.Succeeded)
        {
            var list = await _store.ListAsync(installationId, cancellationToken).ConfigureAwait(false);
            return list.Loaded
                ? Success(BuildStatus(list.Bindings))
                : Failure(
                    ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingsInvalid,
                    list.ErrorMessage ?? "Managed Windows account bindings are invalid.");
        }

        return Failure(
            ToConfigurationStatus(write.Status),
            write.ErrorMessage ?? "Managed Windows account binding change failed.");
    }

    private IReadOnlyList<ManagedWindowsAccountView> BuildStatus(
        IReadOnlyList<ManagedWindowsAccountBinding> bindings)
    {
        var byAccountId = bindings.ToDictionary(binding => binding.AccountId, StringComparer.Ordinal);
        var accounts = new List<ManagedWindowsAccountView>(capacity: 2);

        foreach (var accountId in ManagedWindowsAccountBindingValidator.OrderedSlots)
        {
            if (!byAccountId.TryGetValue(accountId, out var binding))
            {
                accounts.Add(new ManagedWindowsAccountView(
                    accountId,
                    Configured: false,
                    AccountReference: null,
                    CredentialConfigured: false,
                    ClassroomManagedWindowsAccountStatuses.NotConfigured));
                continue;
            }

            var resolved = _accountResolver.ResolveSid(binding.WindowsSid);
            var foundUser = resolved.Found
                && resolved.Identity is not null
                && resolved.Identity.SidNameUse == WindowsAccountSidNameUse.User
                && string.Equals(resolved.Identity.WindowsSid, binding.WindowsSid, StringComparison.OrdinalIgnoreCase);

            accounts.Add(new ManagedWindowsAccountView(
                accountId,
                Configured: true,
                foundUser ? resolved.Identity!.AccountDisplayName : binding.AccountReference,
                CredentialConfigured: false,
                foundUser
                    ? ClassroomManagedWindowsAccountStatuses.CredentialNotConfigured
                    : ClassroomManagedWindowsAccountStatuses.AccountNotFound));
        }

        return accounts;
    }

    private Task<InstallationIdentityResolution> ResolveInstallationIdentityAsync(
        CancellationToken cancellationToken)
    {
        return _installationIdentityResolver.ResolveAsync(cancellationToken);
    }

    private static ManagedWindowsAccountBindingConfigurationStatus ToConfigurationStatus(
        ManagedWindowsAccountBindingStoreWriteStatus status)
    {
        return status switch
        {
            ManagedWindowsAccountBindingStoreWriteStatus.AlreadyExists => ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingAlreadyExists,
            ManagedWindowsAccountBindingStoreWriteStatus.NotFound => ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingNotFound,
            ManagedWindowsAccountBindingStoreWriteStatus.Conflict => ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingConflict,
            ManagedWindowsAccountBindingStoreWriteStatus.StoreInvalid => ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingsInvalid,
            ManagedWindowsAccountBindingStoreWriteStatus.VerificationFailed => ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingsInvalid,
            _ => ManagedWindowsAccountBindingConfigurationStatus.ManagedAccountBindingInvalid
        };
    }

    private static ManagedWindowsAccountBindingConfigurationResult Success(
        IReadOnlyList<ManagedWindowsAccountView> accounts)
    {
        return new ManagedWindowsAccountBindingConfigurationResult(
            ManagedWindowsAccountBindingConfigurationStatus.Success,
            true,
            accounts,
            null);
    }

    private static ManagedWindowsAccountBindingConfigurationResult Failure(
        ManagedWindowsAccountBindingConfigurationStatus status,
        string blockingReason)
    {
        return new ManagedWindowsAccountBindingConfigurationResult(
            status,
            false,
            Array.Empty<ManagedWindowsAccountView>(),
            blockingReason);
    }
}
