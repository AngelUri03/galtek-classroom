using System.Text.Json.Serialization;

namespace GaltekClassroom.Agent.Shared;

public sealed record MachineCodePayload
{
    [JsonPropertyName("product")]
    [JsonPropertyOrder(0)]
    public string Product { get; init; } = ProductInfo.ProductCode;

    [JsonPropertyName("schemaVersion")]
    [JsonPropertyOrder(1)]
    public int SchemaVersion { get; init; } = InstallationIdentityConstants.SchemaVersion;

    [JsonPropertyName("installationId")]
    [JsonPropertyOrder(2)]
    public Guid InstallationId { get; init; }

    [JsonPropertyName("cpuHash")]
    [JsonPropertyOrder(3)]
    public string CpuHash { get; init; } = string.Empty;

    [JsonPropertyName("motherboardHash")]
    [JsonPropertyOrder(4)]
    public string MotherboardHash { get; init; } = string.Empty;

    [JsonPropertyName("macHash")]
    [JsonPropertyOrder(5)]
    public string MacHash { get; init; } = string.Empty;

    [JsonPropertyName("diskHash")]
    [JsonPropertyOrder(6)]
    public string DiskHash { get; init; } = string.Empty;

    [JsonPropertyName("hostname")]
    [JsonPropertyOrder(7)]
    public string Hostname { get; init; } = string.Empty;

    public static MachineCodePayload FromIdentity(InstallationIdentity identity, string hostname)
    {
        return new MachineCodePayload
        {
            Product = ProductInfo.ProductCode,
            SchemaVersion = InstallationIdentityConstants.SchemaVersion,
            InstallationId = identity.InstallationId,
            CpuHash = identity.CpuHash,
            MotherboardHash = identity.MotherboardHash,
            MacHash = identity.MacHash,
            DiskHash = identity.DiskHash,
            Hostname = hostname
        };
    }
}
