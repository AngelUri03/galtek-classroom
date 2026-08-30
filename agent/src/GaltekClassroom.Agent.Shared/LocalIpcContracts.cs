using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaltekClassroom.Agent.Shared;

public sealed record LocalIpcRequest
{
    [JsonPropertyName("protocolVersion")]
    [JsonPropertyOrder(0)]
    public int ProtocolVersion { get; init; } = LocalIpcProtocol.ProtocolVersion;

    [JsonPropertyName("requestId")]
    [JsonPropertyOrder(1)]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("operation")]
    [JsonPropertyOrder(2)]
    public string Operation { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    [JsonPropertyOrder(3)]
    public object? Payload { get; init; } = new Dictionary<string, object?>();
}

public sealed record LocalIpcResponse
{
    [JsonPropertyName("protocolVersion")]
    [JsonPropertyOrder(0)]
    public int ProtocolVersion { get; init; } = LocalIpcProtocol.ProtocolVersion;

    [JsonPropertyName("requestId")]
    [JsonPropertyOrder(1)]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("success")]
    [JsonPropertyOrder(2)]
    public bool Success { get; init; }

    [JsonPropertyName("errorCode")]
    [JsonPropertyOrder(3)]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("payload")]
    [JsonPropertyOrder(4)]
    public object? Payload { get; init; }

    public static LocalIpcResponse Ok(string requestId, object payload)
    {
        return new LocalIpcResponse
        {
            RequestId = requestId,
            Success = true,
            ErrorCode = null,
            Payload = payload
        };
    }

    public static LocalIpcResponse Error(string requestId, string errorCode)
    {
        return new LocalIpcResponse
        {
            RequestId = requestId,
            Success = false,
            ErrorCode = errorCode,
            Payload = null
        };
    }
}

public sealed record LocalIpcPingPayload
{
    [JsonPropertyName("service")]
    [JsonPropertyOrder(0)]
    public string Service { get; init; } = ProductInfo.ServiceDisplayName;

    [JsonPropertyName("status")]
    [JsonPropertyOrder(1)]
    public string Status { get; init; } = "UP";

    [JsonPropertyName("protocolVersion")]
    [JsonPropertyOrder(2)]
    public int ProtocolVersion { get; init; } = LocalIpcProtocol.ProtocolVersion;
}

public sealed record LocalDeviceStatus
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyFeatures =
        new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(capacity: 0, StringComparer.Ordinal));

    [JsonPropertyName("product")]
    [JsonPropertyOrder(0)]
    public string Product { get; init; } = ProductInfo.ProductCode;

    [JsonPropertyName("installationId")]
    [JsonPropertyOrder(1)]
    public string InstallationId { get; init; } = string.Empty;

    [JsonPropertyName("hostname")]
    [JsonPropertyOrder(2)]
    public string Hostname { get; init; } = string.Empty;

    [JsonPropertyName("licenseStatus")]
    [JsonPropertyOrder(3)]
    public string LicenseStatus { get; init; } = string.Empty;

    [JsonPropertyName("active")]
    [JsonPropertyOrder(4)]
    public bool Active { get; init; }

    [JsonPropertyName("licenseId")]
    [JsonPropertyOrder(5)]
    public string? LicenseId { get; init; }

    [JsonPropertyName("organizationId")]
    [JsonPropertyOrder(6)]
    public string? OrganizationId { get; init; }

    [JsonPropertyName("expiresAtUtc")]
    [JsonPropertyOrder(7)]
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    [JsonPropertyName("lastValidatedAtUtc")]
    [JsonPropertyOrder(8)]
    public DateTimeOffset? LastValidatedAtUtc { get; init; }

    [JsonPropertyName("roles")]
    [JsonPropertyOrder(9)]
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    [JsonPropertyName("features")]
    [JsonPropertyOrder(10)]
    public IReadOnlyDictionary<string, object?> Features { get; init; } = EmptyFeatures;

    [JsonPropertyName("startupPhase")]
    [JsonPropertyOrder(11)]
    public string StartupPhase { get; init; } = "STARTING";

    [JsonPropertyName("previousShutdownWasUnclean")]
    [JsonPropertyOrder(12)]
    public bool PreviousShutdownWasUnclean { get; init; }

    [JsonPropertyName("recoveryActive")]
    [JsonPropertyOrder(13)]
    public bool RecoveryActive { get; init; }

    public static LocalDeviceStatus From(
        InstallationIdentity installationIdentity,
        string hostname,
        LicenseState licenseState,
        string startupPhase = "STARTING",
        bool previousShutdownWasUnclean = false,
        bool recoveryActive = false)
    {
        ArgumentNullException.ThrowIfNull(installationIdentity);
        ArgumentNullException.ThrowIfNull(licenseState);

        return new LocalDeviceStatus
        {
            Product = ProductInfo.ProductCode,
            InstallationId = installationIdentity.InstallationId.ToString("D"),
            Hostname = hostname,
            LicenseStatus = licenseState.Status.ToCode(),
            Active = licenseState.Active,
            LicenseId = licenseState.LicenseId,
            OrganizationId = licenseState.OrganizationId,
            ExpiresAtUtc = licenseState.ExpiresAtUtc,
            LastValidatedAtUtc = licenseState.LastValidatedAtUtc,
            Roles = licenseState.Roles,
            Features = CopyFeatures(licenseState.Features),
            StartupPhase = startupPhase,
            PreviousShutdownWasUnclean = previousShutdownWasUnclean,
            RecoveryActive = recoveryActive
        };
    }

    private static IReadOnlyDictionary<string, object?> CopyFeatures(CommercialLicenseFeatures features)
    {
        if (features.Values.Count == 0)
        {
            return EmptyFeatures;
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var item in features.Values)
        {
            values[item.Key] = ToSafeJsonValue(item.Value);
        }

        return values;
    }

    private static object? ToSafeJsonValue(CommercialLicenseFeatureValue value)
    {
        return value.Kind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.IntegerValue ?? value.NumberValue,
            JsonValueKind.String => value.StringValue,
            JsonValueKind.Null => null,
            _ => ParseRawJsonValue(value.RawJson)
        };
    }

    private static object? ParseRawJsonValue(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);

        return document.RootElement.Clone();
    }
}

public sealed record LocalIpcMachineCodePayload
{
    [JsonPropertyName("machineCode")]
    [JsonPropertyOrder(0)]
    public string MachineCode { get; init; } = string.Empty;
}

public sealed record LocalMasterAuthorization
{
    [JsonPropertyName("status")]
    [JsonPropertyOrder(0)]
    public string Status { get; init; } = MasterAuthorizationStatus.NotConfigured.ToCode();

    [JsonPropertyName("authorized")]
    [JsonPropertyOrder(1)]
    public bool Authorized { get; init; }

    [JsonPropertyName("configured")]
    [JsonPropertyOrder(2)]
    public bool Configured { get; init; }

    [JsonPropertyName("boundAccountDisplayName")]
    [JsonPropertyOrder(3)]
    public string? BoundAccountDisplayName { get; init; }

    [JsonPropertyName("currentAccountDisplayName")]
    [JsonPropertyOrder(4)]
    public string? CurrentAccountDisplayName { get; init; }
}
