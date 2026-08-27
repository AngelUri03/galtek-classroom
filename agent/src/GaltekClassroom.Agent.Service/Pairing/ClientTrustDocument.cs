using System.Text.Json.Serialization;
using GaltekClassroom.Agent.Service.Network;

namespace GaltekClassroom.Agent.Service.Pairing;

public sealed record ClientTrustDocument
{
    [JsonPropertyName("schemaVersion")]
    [JsonPropertyOrder(0)]
    public int SchemaVersion { get; init; } = PairingConstants.SchemaVersion;

    [JsonPropertyName("clientNetworkIdentityId")]
    [JsonPropertyOrder(1)]
    public Guid ClientNetworkIdentityId { get; init; }

    [JsonPropertyName("clientInstallationId")]
    [JsonPropertyOrder(2)]
    public Guid ClientInstallationId { get; init; }

    [JsonPropertyName("clientPublicKeyFingerprint")]
    [JsonPropertyOrder(3)]
    public string ClientPublicKeyFingerprint { get; init; } = string.Empty;

    [JsonPropertyName("authorizedMasters")]
    [JsonPropertyOrder(4)]
    public IReadOnlyList<AuthorizedMasterTrustRecord> AuthorizedMasters { get; init; } =
        Array.Empty<AuthorizedMasterTrustRecord>();

    [JsonPropertyName("consumedChallenges")]
    [JsonPropertyOrder(5)]
    public IReadOnlyList<ConsumedPairingChallengeRecord> ConsumedChallenges { get; init; } =
        Array.Empty<ConsumedPairingChallengeRecord>();

    public static ClientTrustDocument Empty(NetworkIdentityMetadata clientIdentity)
    {
        ArgumentNullException.ThrowIfNull(clientIdentity);

        return new ClientTrustDocument
        {
            SchemaVersion = PairingConstants.SchemaVersion,
            ClientNetworkIdentityId = clientIdentity.NetworkIdentityId,
            ClientInstallationId = clientIdentity.InstallationId,
            ClientPublicKeyFingerprint = clientIdentity.PublicKeyFingerprint,
            AuthorizedMasters = Array.Empty<AuthorizedMasterTrustRecord>(),
            ConsumedChallenges = Array.Empty<ConsumedPairingChallengeRecord>()
        };
    }
}

public sealed record AuthorizedMasterTrustRecord
{
    [JsonPropertyName("schemaVersion")]
    [JsonPropertyOrder(0)]
    public int SchemaVersion { get; init; } = PairingConstants.SchemaVersion;

    [JsonPropertyName("status")]
    [JsonPropertyOrder(1)]
    public string Status { get; init; } = PairingStatus.Unpaired.ToCode();

    [JsonPropertyName("masterNetworkIdentityId")]
    [JsonPropertyOrder(2)]
    public Guid MasterNetworkIdentityId { get; init; }

    [JsonPropertyName("clientNetworkIdentityId")]
    [JsonPropertyOrder(3)]
    public Guid ClientNetworkIdentityId { get; init; }

    [JsonPropertyName("clientInstallationId")]
    [JsonPropertyOrder(4)]
    public Guid ClientInstallationId { get; init; }

    [JsonPropertyName("masterPublicKeyFingerprint")]
    [JsonPropertyOrder(5)]
    public string MasterPublicKeyFingerprint { get; init; } = string.Empty;

    [JsonPropertyName("clientPublicKeyFingerprint")]
    [JsonPropertyOrder(6)]
    public string ClientPublicKeyFingerprint { get; init; } = string.Empty;

    [JsonPropertyName("masterPublicKeySubjectPublicKeyInfoBase64")]
    [JsonPropertyOrder(7)]
    public string MasterPublicKeySubjectPublicKeyInfoBase64 { get; init; } = string.Empty;

    [JsonPropertyName("pairedAtUtc")]
    [JsonPropertyOrder(8)]
    public DateTimeOffset? PairedAtUtc { get; init; }

    [JsonPropertyName("revokedAtUtc")]
    [JsonPropertyOrder(9)]
    public DateTimeOffset? RevokedAtUtc { get; init; }

    [JsonPropertyName("pairedChallengeId")]
    [JsonPropertyOrder(10)]
    public Guid? PairedChallengeId { get; init; }

    [JsonPropertyName("certificateThumbprint")]
    [JsonPropertyOrder(11)]
    public string? CertificateThumbprint { get; init; }
}

public sealed record ConsumedPairingChallengeRecord
{
    [JsonPropertyName("challengeId")]
    [JsonPropertyOrder(0)]
    public Guid ChallengeId { get; init; }

    [JsonPropertyName("masterNetworkIdentityId")]
    [JsonPropertyOrder(1)]
    public Guid MasterNetworkIdentityId { get; init; }

    [JsonPropertyName("nonceBase64")]
    [JsonPropertyOrder(2)]
    public string NonceBase64 { get; init; } = string.Empty;

    [JsonPropertyName("consumedAtUtc")]
    [JsonPropertyOrder(3)]
    public DateTimeOffset ConsumedAtUtc { get; init; }
}
