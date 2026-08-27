using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;

namespace GaltekClassroom.Agent.Service.Network;

public enum NetworkIdentityKeyCreationStatus
{
    Created,
    AlreadyExists,
    Failed
}

public sealed record NetworkIdentityKeyCreationResult(
    NetworkIdentityKeyCreationStatus Status,
    string? PublicKeyFingerprint,
    string? ErrorMessage)
{
    public bool Created => Status == NetworkIdentityKeyCreationStatus.Created;

    public static NetworkIdentityKeyCreationResult Success(string publicKeyFingerprint)
    {
        return new NetworkIdentityKeyCreationResult(
            NetworkIdentityKeyCreationStatus.Created,
            publicKeyFingerprint,
            null);
    }

    public static NetworkIdentityKeyCreationResult AlreadyExists(string keyName)
    {
        return new NetworkIdentityKeyCreationResult(
            NetworkIdentityKeyCreationStatus.AlreadyExists,
            null,
            $"CNG key already exists: {keyName}");
    }

    public static NetworkIdentityKeyCreationResult Failed(string errorMessage)
    {
        return new NetworkIdentityKeyCreationResult(
            NetworkIdentityKeyCreationStatus.Failed,
            null,
            errorMessage);
    }
}

public enum NetworkIdentityKeyLookupStatus
{
    Found,
    Missing,
    Invalid
}

public sealed record NetworkIdentityKeyLookupResult(
    NetworkIdentityKeyLookupStatus Status,
    string? PublicKeyFingerprint,
    string? ErrorMessage)
{
    public static NetworkIdentityKeyLookupResult Found(string publicKeyFingerprint)
    {
        return new NetworkIdentityKeyLookupResult(
            NetworkIdentityKeyLookupStatus.Found,
            publicKeyFingerprint,
            null);
    }

    public static NetworkIdentityKeyLookupResult Missing(string keyName)
    {
        return new NetworkIdentityKeyLookupResult(
            NetworkIdentityKeyLookupStatus.Missing,
            null,
            $"CNG key is missing: {keyName}");
    }

    public static NetworkIdentityKeyLookupResult Invalid(string errorMessage)
    {
        return new NetworkIdentityKeyLookupResult(
            NetworkIdentityKeyLookupStatus.Invalid,
            null,
            errorMessage);
    }
}

public enum NetworkIdentityKeyDeleteStatus
{
    Deleted,
    Missing,
    Failed
}

public sealed record NetworkIdentityKeyDeleteResult(
    NetworkIdentityKeyDeleteStatus Status,
    string? ErrorMessage)
{
    public bool Deleted => Status is NetworkIdentityKeyDeleteStatus.Deleted or NetworkIdentityKeyDeleteStatus.Missing;

    public static NetworkIdentityKeyDeleteResult Success()
    {
        return new NetworkIdentityKeyDeleteResult(NetworkIdentityKeyDeleteStatus.Deleted, null);
    }

    public static NetworkIdentityKeyDeleteResult Missing()
    {
        return new NetworkIdentityKeyDeleteResult(NetworkIdentityKeyDeleteStatus.Missing, null);
    }

    public static NetworkIdentityKeyDeleteResult Failed(string errorMessage)
    {
        return new NetworkIdentityKeyDeleteResult(NetworkIdentityKeyDeleteStatus.Failed, errorMessage);
    }
}

public interface INetworkIdentityKeyStore
{
    bool Exists(string keyName);

    NetworkIdentityKeyCreationResult Create(string keyName);

    NetworkIdentityKeyLookupResult GetPublicKeyFingerprint(string keyName);

    NetworkIdentityKeyDeleteResult Delete(string keyName);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsCngNetworkIdentityKeyStore : INetworkIdentityKeyStore
{
    private static readonly CngProvider Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider;
    private const CngKeyOpenOptions OpenOptions = CngKeyOpenOptions.MachineKey;
    private const string RsaKeyLengthPropertyName = "Length";
    private const string SecurityDescriptorPropertyName = "Security Descr";
    private const string LocalSystemAndAdministratorsFullControlSddl = "D:P(A;;GA;;;SY)(A;;GA;;;BA)";

    public bool Exists(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return CngKey.Exists(keyName, Provider, OpenOptions);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public NetworkIdentityKeyCreationResult Create(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        if (!OperatingSystem.IsWindows())
        {
            return NetworkIdentityKeyCreationResult.Failed("Windows CNG/KSP is required for Network Identity.");
        }

        if (Exists(keyName))
        {
            return NetworkIdentityKeyCreationResult.AlreadyExists(keyName);
        }

        try
        {
            using var key = CngKey.Create(
                CngAlgorithm.Rsa,
                keyName,
                CreateKeyParameters());

            return NetworkIdentityKeyCreationResult.Success(ComputePublicKeyFingerprint(key));
        }
        catch (CryptographicException exception)
        {
            return NetworkIdentityKeyCreationResult.Failed(
                $"CNG network identity key could not be created: {exception.Message}");
        }
        catch (PlatformNotSupportedException exception)
        {
            return NetworkIdentityKeyCreationResult.Failed(
                $"CNG network identity key could not be created: {exception.Message}");
        }
    }

    public NetworkIdentityKeyLookupResult GetPublicKeyFingerprint(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        if (!OperatingSystem.IsWindows())
        {
            return NetworkIdentityKeyLookupResult.Invalid("Windows CNG/KSP is required for Network Identity.");
        }

        if (!Exists(keyName))
        {
            return NetworkIdentityKeyLookupResult.Missing(keyName);
        }

        try
        {
            using var key = CngKey.Open(keyName, Provider, OpenOptions);

            return NetworkIdentityKeyLookupResult.Found(ComputePublicKeyFingerprint(key));
        }
        catch (CryptographicException exception)
        {
            return NetworkIdentityKeyLookupResult.Invalid(
                $"CNG network identity key could not be opened: {exception.Message}");
        }
        catch (PlatformNotSupportedException exception)
        {
            return NetworkIdentityKeyLookupResult.Invalid(
                $"CNG network identity key could not be opened: {exception.Message}");
        }
    }

    public NetworkIdentityKeyDeleteResult Delete(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        if (!OperatingSystem.IsWindows())
        {
            return NetworkIdentityKeyDeleteResult.Missing();
        }

        if (!Exists(keyName))
        {
            return NetworkIdentityKeyDeleteResult.Missing();
        }

        try
        {
            using var key = CngKey.Open(keyName, Provider, OpenOptions);
            key.Delete();

            return NetworkIdentityKeyDeleteResult.Success();
        }
        catch (CryptographicException exception)
        {
            return NetworkIdentityKeyDeleteResult.Failed(
                $"CNG network identity key could not be deleted: {exception.Message}");
        }
        catch (PlatformNotSupportedException exception)
        {
            return NetworkIdentityKeyDeleteResult.Failed(
                $"CNG network identity key could not be deleted: {exception.Message}");
        }
    }

    private static CngKeyCreationParameters CreateKeyParameters()
    {
        var parameters = new CngKeyCreationParameters
        {
            Provider = Provider,
            KeyCreationOptions = CngKeyCreationOptions.MachineKey,
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.Signing
        };

        parameters.Parameters.Add(new CngProperty(
            RsaKeyLengthPropertyName,
            BitConverter.GetBytes(NetworkIdentityConstants.RsaKeySizeBits),
            CngPropertyOptions.None));
        parameters.Parameters.Add(new CngProperty(
            SecurityDescriptorPropertyName,
            CreateRestrictedSecurityDescriptor(),
            CngPropertyOptions.Persist));

        return parameters;
    }

    private static byte[] CreateRestrictedSecurityDescriptor()
    {
        var descriptor = new RawSecurityDescriptor(LocalSystemAndAdministratorsFullControlSddl);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);

        return bytes;
    }

    private static string ComputePublicKeyFingerprint(CngKey key)
    {
        using var rsa = new RSACng(key);
        var publicKey = rsa.ExportSubjectPublicKeyInfo();
        var fingerprint = SHA256.HashData(publicKey);

        return Convert.ToHexString(fingerprint).ToLowerInvariant();
    }
}

public sealed class UnsupportedNetworkIdentityKeyStore : INetworkIdentityKeyStore
{
    public bool Exists(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        return false;
    }

    public NetworkIdentityKeyCreationResult Create(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        return NetworkIdentityKeyCreationResult.Failed(
            "Windows CNG/KSP is required for Network Identity.");
    }

    public NetworkIdentityKeyLookupResult GetPublicKeyFingerprint(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        return NetworkIdentityKeyLookupResult.Invalid(
            "Windows CNG/KSP is required for Network Identity.");
    }

    public NetworkIdentityKeyDeleteResult Delete(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        return NetworkIdentityKeyDeleteResult.Missing();
    }
}
