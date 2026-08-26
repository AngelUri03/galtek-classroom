using System.Collections.ObjectModel;

namespace GaltekClassroom.Agent.Shared;

public sealed record CommercialLicenseFeatures
{
    public CommercialLicenseFeatures(IReadOnlyDictionary<string, CommercialLicenseFeatureValue>? values = null)
    {
        Values = new ReadOnlyDictionary<string, CommercialLicenseFeatureValue>(
            new Dictionary<string, CommercialLicenseFeatureValue>(
                values ?? new Dictionary<string, CommercialLicenseFeatureValue>(),
                StringComparer.Ordinal));
    }

    public IReadOnlyDictionary<string, CommercialLicenseFeatureValue> Values { get; }

    public bool? ScreenMonitoring => GetBoolean("screenMonitoring");
    public bool? ScreenProjection => GetBoolean("screenProjection");
    public bool? InputLock => GetBoolean("inputLock");
    public bool? RemoteAppLaunch => GetBoolean("remoteAppLaunch");
    public bool? RemoteShutdown => GetBoolean("remoteShutdown");
    public bool? MultiMaster => GetBoolean("multiMaster");
    public int? MaxManagedClients => GetInt32("maxManagedClients");

    public static CommercialLicenseFeatures Empty { get; } = new();

    public bool? GetBoolean(string featureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        return Values.TryGetValue(featureName, out var value)
            ? value.BooleanValue
            : null;
    }

    public int? GetInt32(string featureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        if (!Values.TryGetValue(featureName, out var value) || value.IntegerValue is null)
        {
            return null;
        }

        if (value.IntegerValue is < int.MinValue or > int.MaxValue)
        {
            return null;
        }

        return (int)value.IntegerValue.Value;
    }
}
