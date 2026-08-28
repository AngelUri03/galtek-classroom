using Microsoft.Extensions.Configuration;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class MasterConnectionOptions
{
    public const string SectionName = "Galtek:Classroom:Agent:MasterConnection";

    public bool Enabled { get; init; }

    public string? MasterEndpoint { get; init; }

    public Guid? MasterNetworkIdentityId { get; init; }

    public string? DeviceId { get; init; }

    public string? DisplayName { get; init; }

    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan CertificateLifetime { get; init; } = TimeSpan.FromDays(7);

    public int MaxMessageBytes { get; init; } = 64 * 1024;

    public IReadOnlyList<TimeSpan> ReconnectDelays { get; init; } = new[]
    {
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    };

    public static MasterConnectionOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        return new MasterConnectionOptions
        {
            Enabled = ReadBool(section, "Enabled", false),
            MasterEndpoint = EmptyToNull(section["MasterEndpoint"]),
            MasterNetworkIdentityId = ReadGuid(section, "MasterNetworkIdentityId"),
            DeviceId = EmptyToNull(section["DeviceId"]),
            DisplayName = EmptyToNull(section["DisplayName"]),
            HeartbeatInterval = ReadTimeSpan(section, "HeartbeatInterval", TimeSpan.FromSeconds(15)),
            ConnectTimeout = ReadTimeSpan(section, "ConnectTimeout", TimeSpan.FromSeconds(15)),
            CertificateLifetime = ReadTimeSpan(section, "CertificateLifetime", TimeSpan.FromDays(7)),
            MaxMessageBytes = Math.Clamp(ReadInt(section, "MaxMessageBytes", 64 * 1024), 1024, 1024 * 1024),
            ReconnectDelays = ReadReconnectDelays(section)
        };
    }

    private static bool ReadBool(IConfiguration section, string key, bool fallback)
    {
        return bool.TryParse(section[key], out var value) ? value : fallback;
    }

    private static int ReadInt(IConfiguration section, string key, int fallback)
    {
        return int.TryParse(section[key], out var value) ? value : fallback;
    }

    private static Guid? ReadGuid(IConfiguration section, string key)
    {
        return Guid.TryParse(section[key], out var value) && value != Guid.Empty ? value : null;
    }

    private static TimeSpan ReadTimeSpan(IConfiguration section, string key, TimeSpan fallback)
    {
        return TimeSpan.TryParse(section[key], out var value) && value > TimeSpan.Zero ? value : fallback;
    }

    private static IReadOnlyList<TimeSpan> ReadReconnectDelays(IConfiguration section)
    {
        var configured = section.GetSection("ReconnectDelays")
            .GetChildren()
            .Select(item => TimeSpan.TryParse(item.Value, out var value) && value > TimeSpan.Zero
                ? value
                : TimeSpan.Zero)
            .Where(value => value > TimeSpan.Zero)
            .ToArray();

        return configured.Length > 0
            ? configured
            : new[]
            {
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30)
            };
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
