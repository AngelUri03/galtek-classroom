using System.Text.Json.Serialization;

namespace GaltekClassroom.Agent.Shared;

public sealed record InstallationIdentity
{
    [JsonPropertyName("schemaVersion")]
    [JsonPropertyOrder(0)]
    public int SchemaVersion { get; init; } = InstallationIdentityConstants.SchemaVersion;

    [JsonPropertyName("installationId")]
    [JsonPropertyOrder(1)]
    public Guid InstallationId { get; init; }

    [JsonPropertyName("cpuHash")]
    [JsonPropertyOrder(2)]
    public string CpuHash { get; init; } = string.Empty;

    [JsonPropertyName("motherboardHash")]
    [JsonPropertyOrder(3)]
    public string MotherboardHash { get; init; } = string.Empty;

    [JsonPropertyName("macHash")]
    [JsonPropertyOrder(4)]
    public string MacHash { get; init; } = string.Empty;

    [JsonPropertyName("diskHash")]
    [JsonPropertyOrder(5)]
    public string DiskHash { get; init; } = string.Empty;

    [JsonPropertyName("createdAtUtc")]
    [JsonPropertyOrder(6)]
    public DateTimeOffset CreatedAtUtc { get; init; }

    public static InstallationIdentity Create(
        Guid installationId,
        HardwareFingerprint fingerprint,
        DateTimeOffset createdAtUtc)
    {
        return new InstallationIdentity
        {
            SchemaVersion = InstallationIdentityConstants.SchemaVersion,
            InstallationId = installationId,
            CpuHash = fingerprint.CpuHash,
            MotherboardHash = fingerprint.MotherboardHash,
            MacHash = fingerprint.MacHash,
            DiskHash = fingerprint.DiskHash,
            CreatedAtUtc = createdAtUtc.ToUniversalTime()
        };
    }
}
