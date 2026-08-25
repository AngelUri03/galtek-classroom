using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Identity;

public sealed class WindowsHardwareFingerprintProvider : IHardwareFingerprintProvider
{
    private static readonly string[] VirtualNetworkInterfaceKeywords =
    [
        "VIRTUAL",
        "VMWARE",
        "VBOX",
        "HYPER-V",
        "LOOPBACK",
        "TUNNEL",
        "VPN",
        "TAP",
        "TEREDO",
        "ISATAP",
        "PSEUDO",
        "DOCKER",
        "WSL"
    ];

    public Task<HardwareFingerprint> GetCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fingerprint = HardwareFingerprintFactory.FromRawValues(
            QueryFirstAvailableWmiValuePerInstance("Win32_Processor", "ProcessorId", "UniqueId"),
            QueryFirstAvailableWmiValuePerInstance("Win32_BaseBoard", "SerialNumber", "Product"),
            GetPhysicalMacAddresses(),
            QueryFirstAvailableWmiValuePerInstance("Win32_DiskDrive", "SerialNumber", "PNPDeviceID"));

        return Task.FromResult(fingerprint);
    }

    private static IReadOnlyList<string?> QueryFirstAvailableWmiValuePerInstance(
        string wmiClassName,
        params string[] propertyNames)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            var properties = string.Join(", ", propertyNames);
            using var searcher = new ManagementObjectSearcher($"SELECT {properties} FROM {wmiClassName}");
            using var results = searcher.Get();
            var values = new List<string?>();

            foreach (ManagementObject result in results)
            {
                using (result)
                {
                    foreach (var propertyName in propertyNames)
                    {
                        var value = result[propertyName]?.ToString();

                        if (HardwareIdentifierNormalizer.NormalizeSingle(value) is not null)
                        {
                            values.Add(value);
                            break;
                        }
                    }
                }
            }

            return values;
        }
        catch (ManagementException)
        {
            return [];
        }
        catch (COMException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string?> GetPhysicalMacAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsPhysicalNetworkInterfaceCandidate)
                .Select(networkInterface => networkInterface.GetPhysicalAddress().ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    private static bool IsPhysicalNetworkInterfaceCandidate(NetworkInterface networkInterface)
    {
        var addressLength = networkInterface.GetPhysicalAddress().GetAddressBytes().Length;

        if (addressLength < 6)
        {
            return false;
        }

        if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback
            or NetworkInterfaceType.Tunnel
            or NetworkInterfaceType.Unknown)
        {
            return false;
        }

        var identityText = $"{networkInterface.Name} {networkInterface.Description}".ToUpperInvariant();

        return !VirtualNetworkInterfaceKeywords.Any(identityText.Contains);
    }
}
