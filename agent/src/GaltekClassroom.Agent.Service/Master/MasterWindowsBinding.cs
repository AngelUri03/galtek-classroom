using System.Text.Json.Serialization;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Master;

public sealed record MasterWindowsBinding
{
    [JsonPropertyName("schemaVersion")]
    [JsonPropertyOrder(0)]
    public int SchemaVersion { get; init; } = MasterBindingConstants.SchemaVersion;

    [JsonPropertyName("installationId")]
    [JsonPropertyOrder(1)]
    public Guid InstallationId { get; init; }

    [JsonPropertyName("windowsSid")]
    [JsonPropertyOrder(2)]
    public string WindowsSid { get; init; } = string.Empty;

    [JsonPropertyName("accountDisplayName")]
    [JsonPropertyOrder(3)]
    public string AccountDisplayName { get; init; } = string.Empty;

    [JsonPropertyName("boundAtUtc")]
    [JsonPropertyOrder(4)]
    public DateTimeOffset BoundAtUtc { get; init; }

    public static MasterWindowsBinding Create(
        Guid installationId,
        string windowsSid,
        string accountDisplayName,
        DateTimeOffset boundAtUtc)
    {
        return new MasterWindowsBinding
        {
            SchemaVersion = MasterBindingConstants.SchemaVersion,
            InstallationId = installationId,
            WindowsSid = windowsSid,
            AccountDisplayName = accountDisplayName,
            BoundAtUtc = boundAtUtc.ToUniversalTime()
        };
    }
}
