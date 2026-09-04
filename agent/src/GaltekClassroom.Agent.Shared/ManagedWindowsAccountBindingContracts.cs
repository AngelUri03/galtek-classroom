using System.Text.Json;

namespace GaltekClassroom.Agent.Shared;

public static class ManagedWindowsAccountBindingConstants
{
    public const int SchemaVersion = 1;
    public const string FileName = "managed-windows-accounts.json";
}

public static class ManagedWindowsAccountBindingErrorCodes
{
    public const string ManagedAccountBindingsInvalid = "MANAGED_ACCOUNT_BINDINGS_INVALID";
    public const string ManagedAccountBindingAlreadyExists = "MANAGED_ACCOUNT_BINDING_ALREADY_EXISTS";
    public const string ManagedAccountBindingNotFound = "MANAGED_ACCOUNT_BINDING_NOT_FOUND";
    public const string ManagedAccountBindingConflict = "MANAGED_ACCOUNT_BINDING_CONFLICT";
    public const string ManagedAccountBindingInvalid = "MANAGED_ACCOUNT_BINDING_INVALID";
    public const string WindowsAccountNotFound = "WINDOWS_ACCOUNT_NOT_FOUND";
    public const string AdministratorRequired = "ADMINISTRATOR_REQUIRED";
    public const string InstallationIdentityInvalid = "INSTALLATION_IDENTITY_INVALID";
}

public sealed record ManagedWindowsAccountBinding(
    string AccountId,
    string WindowsSid,
    string AccountReference,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static ManagedWindowsAccountBinding Create(
        string accountId,
        string windowsSid,
        string accountReference,
        DateTimeOffset nowUtc)
    {
        return new ManagedWindowsAccountBinding(
            NormalizeAccountId(accountId),
            windowsSid.Trim(),
            accountReference.Trim(),
            nowUtc.ToUniversalTime(),
            nowUtc.ToUniversalTime());
    }

    public ManagedWindowsAccountBinding WithUpdatedTarget(
        string windowsSid,
        string accountReference,
        DateTimeOffset nowUtc)
    {
        return this with
        {
            WindowsSid = windowsSid.Trim(),
            AccountReference = accountReference.Trim(),
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public static bool IsValidAccountId(string? accountId)
    {
        return string.Equals(accountId?.Trim(), ClassroomManagedWindowsAccountTypes.Primary, StringComparison.OrdinalIgnoreCase)
            || string.Equals(accountId?.Trim(), ClassroomManagedWindowsAccountTypes.Secondary, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeAccountId(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var trimmed = accountId.Trim();
        if (string.Equals(trimmed, ClassroomManagedWindowsAccountTypes.Primary, StringComparison.OrdinalIgnoreCase))
        {
            return ClassroomManagedWindowsAccountTypes.Primary;
        }

        if (string.Equals(trimmed, ClassroomManagedWindowsAccountTypes.Secondary, StringComparison.OrdinalIgnoreCase))
        {
            return ClassroomManagedWindowsAccountTypes.Secondary;
        }

        throw new ArgumentException("Managed Windows accountId must be PRIMARY or SECONDARY.", nameof(accountId));
    }
}

public sealed record ManagedWindowsAccountBindingCatalogDocument(
    int SchemaVersion,
    Guid InstallationId,
    IReadOnlyList<ManagedWindowsAccountBinding> Bindings);

public sealed record ManagedWindowsAccountView(
    string AccountId,
    bool Configured,
    string? AccountReference,
    bool CredentialConfigured,
    string Status);

public sealed class ManagedWindowsAccountBindingDocumentGuardConverter : System.Text.Json.Serialization.JsonConverter<ManagedWindowsAccountBindingCatalogDocument>
{
    public override ManagedWindowsAccountBindingCatalogDocument Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        RejectSecretFields(root);
        return JsonSerializer.Deserialize<ManagedWindowsAccountBindingCatalogDocument>(
            root.GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    public override void Write(
        Utf8JsonWriter writer,
        ManagedWindowsAccountBindingCatalogDocument value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static void RejectSecretFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (IsSecretFieldName(property.Name))
                {
                    throw new JsonException("managed-windows-accounts.json contains a forbidden credential field.");
                }

                RejectSecretFields(property.Value);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                RejectSecretFields(item);
            }
        }
    }

    private static bool IsSecretFieldName(string fieldName)
    {
        return fieldName.Contains("password", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("token", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("pin", StringComparison.OrdinalIgnoreCase);
    }
}
