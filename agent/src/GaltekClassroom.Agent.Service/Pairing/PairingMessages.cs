using System.Text.Json.Serialization;

namespace GaltekClassroom.Agent.Service.Pairing;

public sealed record PairingChallenge
{
    [JsonPropertyName("schemaVersion")]
    [JsonPropertyOrder(0)]
    public int SchemaVersion { get; init; } = PairingConstants.SchemaVersion;

    [JsonPropertyName("purpose")]
    [JsonPropertyOrder(1)]
    public string Purpose { get; init; } = PairingConstants.Purpose;

    [JsonPropertyName("challengeId")]
    [JsonPropertyOrder(2)]
    public Guid ChallengeId { get; init; }

    [JsonPropertyName("masterNetworkIdentityId")]
    [JsonPropertyOrder(3)]
    public Guid MasterNetworkIdentityId { get; init; }

    [JsonPropertyName("clientNetworkIdentityId")]
    [JsonPropertyOrder(4)]
    public Guid ClientNetworkIdentityId { get; init; }

    [JsonPropertyName("clientInstallationId")]
    [JsonPropertyOrder(5)]
    public Guid ClientInstallationId { get; init; }

    [JsonPropertyName("masterPublicKeyFingerprint")]
    [JsonPropertyOrder(6)]
    public string MasterPublicKeyFingerprint { get; init; } = string.Empty;

    [JsonPropertyName("clientPublicKeyFingerprint")]
    [JsonPropertyOrder(7)]
    public string ClientPublicKeyFingerprint { get; init; } = string.Empty;

    [JsonPropertyName("masterPublicKeySubjectPublicKeyInfoBase64")]
    [JsonPropertyOrder(8)]
    public string MasterPublicKeySubjectPublicKeyInfoBase64 { get; init; } = string.Empty;

    [JsonPropertyName("clientPublicKeySubjectPublicKeyInfoBase64")]
    [JsonPropertyOrder(9)]
    public string ClientPublicKeySubjectPublicKeyInfoBase64 { get; init; } = string.Empty;

    [JsonPropertyName("nonceBase64")]
    [JsonPropertyOrder(10)]
    public string NonceBase64 { get; init; } = string.Empty;

    [JsonPropertyName("issuedAtUtc")]
    [JsonPropertyOrder(11)]
    public DateTimeOffset IssuedAtUtc { get; init; }

    [JsonPropertyName("expiresAtUtc")]
    [JsonPropertyOrder(12)]
    public DateTimeOffset ExpiresAtUtc { get; init; }

    [JsonPropertyName("masterSignatureBase64")]
    [JsonPropertyOrder(13)]
    public string MasterSignatureBase64 { get; init; } = string.Empty;
}

public sealed record PairingResponse
{
    [JsonPropertyName("schemaVersion")]
    [JsonPropertyOrder(0)]
    public int SchemaVersion { get; init; } = PairingConstants.SchemaVersion;

    [JsonPropertyName("purpose")]
    [JsonPropertyOrder(1)]
    public string Purpose { get; init; } = PairingConstants.Purpose;

    [JsonPropertyName("challengeId")]
    [JsonPropertyOrder(2)]
    public Guid ChallengeId { get; init; }

    [JsonPropertyName("masterNetworkIdentityId")]
    [JsonPropertyOrder(3)]
    public Guid MasterNetworkIdentityId { get; init; }

    [JsonPropertyName("clientNetworkIdentityId")]
    [JsonPropertyOrder(4)]
    public Guid ClientNetworkIdentityId { get; init; }

    [JsonPropertyName("clientInstallationId")]
    [JsonPropertyOrder(5)]
    public Guid ClientInstallationId { get; init; }

    [JsonPropertyName("masterPublicKeyFingerprint")]
    [JsonPropertyOrder(6)]
    public string MasterPublicKeyFingerprint { get; init; } = string.Empty;

    [JsonPropertyName("clientPublicKeyFingerprint")]
    [JsonPropertyOrder(7)]
    public string ClientPublicKeyFingerprint { get; init; } = string.Empty;

    [JsonPropertyName("challengeNonceBase64")]
    [JsonPropertyOrder(8)]
    public string ChallengeNonceBase64 { get; init; } = string.Empty;

    [JsonPropertyName("responseNonceBase64")]
    [JsonPropertyOrder(9)]
    public string ResponseNonceBase64 { get; init; } = string.Empty;

    [JsonPropertyName("signedAtUtc")]
    [JsonPropertyOrder(10)]
    public DateTimeOffset SignedAtUtc { get; init; }

    [JsonPropertyName("clientSignatureBase64")]
    [JsonPropertyOrder(11)]
    public string ClientSignatureBase64 { get; init; } = string.Empty;
}
