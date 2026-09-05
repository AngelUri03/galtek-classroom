using System.Text.Json;

namespace GaltekClassroom.Agent.Shared;

public static class ManagedWindowsCredentialConstants
{
    public const int SchemaVersion = 1;
    public const string FileName = "managed-windows-credentials.dat";
    public const int MaximumPasswordCharacters = 1024;
    public const string EntropyContextPrefix = "GaltekClassroom.ManagedWindowsCredential.v1";
}

public static class ManagedWindowsCredentialErrorCodes
{
    public const string ManagedCredentialStoreInvalid = "MANAGED_CREDENTIAL_STORE_INVALID";
    public const string ManagedCredentialProtectionFailed = "MANAGED_CREDENTIAL_PROTECTION_FAILED";
    public const string ManagedCredentialAlreadyExists = "MANAGED_CREDENTIAL_ALREADY_EXISTS";
    public const string ManagedCredentialInvalid = "MANAGED_CREDENTIAL_INVALID";
    public const string AccountNotConfigured = "ACCOUNT_NOT_CONFIGURED";
    public const string AccountNotFound = "ACCOUNT_NOT_FOUND";
    public const string ManagedCredentialNotConfigured = "MANAGED_CREDENTIAL_NOT_CONFIGURED";
    public const string ManagedAccountBindingsInvalid = "MANAGED_ACCOUNT_BINDINGS_INVALID";
}

public sealed record ManagedWindowsCredentialEntry(
    string AccountId,
    string ProtectedData,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public override string ToString()
    {
        return $"ManagedWindowsCredentialEntry(AccountId={AccountId}, CreatedAtUtc={CreatedAtUtc:O}, UpdatedAtUtc={UpdatedAtUtc:O})";
    }
}

public sealed record ManagedWindowsCredentialCatalogDocument(
    int SchemaVersion,
    Guid InstallationId,
    IReadOnlyList<ManagedWindowsCredentialEntry> Entries)
{
    public override string ToString()
    {
        return $"ManagedWindowsCredentialCatalogDocument(SchemaVersion={SchemaVersion}, InstallationId={InstallationId:D}, EntryCount={Entries.Count})";
    }
}

public sealed class ManagedWindowsCredentialDocumentGuardConverter : System.Text.Json.Serialization.JsonConverter<ManagedWindowsCredentialCatalogDocument>
{
    private static readonly HashSet<string> AllowedRootProperties = new(StringComparer.Ordinal)
    {
        "schemaVersion",
        "installationId",
        "entries"
    };

    private static readonly HashSet<string> AllowedEntryProperties = new(StringComparer.Ordinal)
    {
        "accountId",
        "protectedData",
        "createdAtUtc",
        "updatedAtUtc"
    };

    public override ManagedWindowsCredentialCatalogDocument Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        RejectUnexpectedOrSecretFields(root);
        return JsonSerializer.Deserialize<ManagedWindowsCredentialCatalogDocument>(
            root.GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    public override void Write(
        Utf8JsonWriter writer,
        ManagedWindowsCredentialCatalogDocument value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static void RejectUnexpectedOrSecretFields(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("managed-windows-credentials.dat root must be an object.");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!AllowedRootProperties.Contains(property.Name))
            {
                throw new JsonException("managed-windows-credentials.dat contains an unsupported field.");
            }

            if (property.Name == "entries" && property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in property.Value.EnumerateArray())
                {
                    RejectUnexpectedEntryFields(entry);
                }
            }
        }
    }

    private static void RejectUnexpectedEntryFields(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("managed-windows-credentials.dat entry must be an object.");
        }

        foreach (var property in entry.EnumerateObject())
        {
            if (!AllowedEntryProperties.Contains(property.Name))
            {
                throw new JsonException("managed-windows-credentials.dat contains an unsupported entry field.");
            }
        }
    }
}
