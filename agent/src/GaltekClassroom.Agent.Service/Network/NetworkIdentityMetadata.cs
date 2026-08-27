using System.Text.Json.Serialization;

namespace GaltekClassroom.Agent.Service.Network;

public sealed record NetworkIdentityMetadata
{
    [JsonPropertyName("schemaVersion")]
    [JsonPropertyOrder(0)]
    public int SchemaVersion { get; init; } = NetworkIdentityConstants.SchemaVersion;

    [JsonPropertyName("networkIdentityId")]
    [JsonPropertyOrder(1)]
    public Guid NetworkIdentityId { get; init; }

    [JsonPropertyName("installationId")]
    [JsonPropertyOrder(2)]
    public Guid InstallationId { get; init; }

    [JsonPropertyName("keyId")]
    [JsonPropertyOrder(3)]
    public string KeyId { get; init; } = string.Empty;

    [JsonPropertyName("keyName")]
    [JsonPropertyOrder(4)]
    public string KeyName { get; init; } = string.Empty;

    [JsonPropertyName("publicKeyFingerprint")]
    [JsonPropertyOrder(5)]
    public string PublicKeyFingerprint { get; init; } = string.Empty;

    [JsonPropertyName("createdAtUtc")]
    [JsonPropertyOrder(6)]
    public DateTimeOffset CreatedAtUtc { get; init; }

    public static NetworkIdentityMetadata Create(
        Guid networkIdentityId,
        Guid installationId,
        NetworkIdentityKeyDescriptor keyDescriptor,
        string publicKeyFingerprint,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(keyDescriptor);

        return new NetworkIdentityMetadata
        {
            SchemaVersion = NetworkIdentityConstants.SchemaVersion,
            NetworkIdentityId = networkIdentityId,
            InstallationId = installationId,
            KeyId = keyDescriptor.KeyId,
            KeyName = keyDescriptor.KeyName,
            PublicKeyFingerprint = publicKeyFingerprint,
            CreatedAtUtc = createdAtUtc.ToUniversalTime()
        };
    }
}
