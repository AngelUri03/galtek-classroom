using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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

public enum NetworkIdentityPublicKeyStatus
{
    Found,
    Missing,
    Invalid
}

public sealed record NetworkIdentityPublicKeyResult(
    NetworkIdentityPublicKeyStatus Status,
    string? PublicKeyFingerprint,
    string? SubjectPublicKeyInfoBase64,
    string? ErrorMessage)
{
    public static NetworkIdentityPublicKeyResult Found(
        string publicKeyFingerprint,
        string subjectPublicKeyInfoBase64)
    {
        return new NetworkIdentityPublicKeyResult(
            NetworkIdentityPublicKeyStatus.Found,
            publicKeyFingerprint,
            subjectPublicKeyInfoBase64,
            null);
    }

    public static NetworkIdentityPublicKeyResult Missing(string keyName)
    {
        return new NetworkIdentityPublicKeyResult(
            NetworkIdentityPublicKeyStatus.Missing,
            null,
            null,
            $"CNG key is missing: {keyName}");
    }

    public static NetworkIdentityPublicKeyResult Invalid(string errorMessage)
    {
        return new NetworkIdentityPublicKeyResult(
            NetworkIdentityPublicKeyStatus.Invalid,
            null,
            null,
            errorMessage);
    }
}

public enum NetworkIdentitySignatureStatus
{
    Signed,
    Missing,
    Invalid
}

public sealed record NetworkIdentitySignatureResult(
    NetworkIdentitySignatureStatus Status,
    string? SignatureBase64,
    string? ErrorMessage)
{
    public bool Signed => Status == NetworkIdentitySignatureStatus.Signed;

    public static NetworkIdentitySignatureResult Success(string signatureBase64)
    {
        return new NetworkIdentitySignatureResult(
            NetworkIdentitySignatureStatus.Signed,
            signatureBase64,
            null);
    }

    public static NetworkIdentitySignatureResult Missing(string keyName)
    {
        return new NetworkIdentitySignatureResult(
            NetworkIdentitySignatureStatus.Missing,
            null,
            $"CNG key is missing: {keyName}");
    }

    public static NetworkIdentitySignatureResult Invalid(string errorMessage)
    {
        return new NetworkIdentitySignatureResult(
            NetworkIdentitySignatureStatus.Invalid,
            null,
            errorMessage);
    }
}

public enum NetworkIdentityCertificateStatus
{
    Created,
    Missing,
    Invalid
}

public sealed record NetworkIdentityCertificateResult(
    NetworkIdentityCertificateStatus Status,
    string? PublicKeyFingerprint,
    X509Certificate2? Certificate,
    string? ErrorMessage)
{
    public bool Created => Status == NetworkIdentityCertificateStatus.Created;

    public static NetworkIdentityCertificateResult Success(
        string publicKeyFingerprint,
        X509Certificate2 certificate)
    {
        return new NetworkIdentityCertificateResult(
            NetworkIdentityCertificateStatus.Created,
            publicKeyFingerprint,
            certificate,
            null);
    }

    public static NetworkIdentityCertificateResult Missing(string keyName)
    {
        return new NetworkIdentityCertificateResult(
            NetworkIdentityCertificateStatus.Missing,
            null,
            null,
            $"CNG key is missing: {keyName}");
    }

    public static NetworkIdentityCertificateResult Invalid(string errorMessage)
    {
        return new NetworkIdentityCertificateResult(
            NetworkIdentityCertificateStatus.Invalid,
            null,
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

    NetworkIdentityPublicKeyResult GetPublicKey(string keyName);

    NetworkIdentitySignatureResult Sign(string keyName, byte[] data);

    NetworkIdentityCertificateResult CreateSelfSignedCertificate(
        string keyName,
        string subjectName,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter);

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
            var publicKey = ExportPublicKey(key);

            return NetworkIdentityKeyLookupResult.Found(publicKey.Fingerprint);
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

    public NetworkIdentityPublicKeyResult GetPublicKey(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        if (!OperatingSystem.IsWindows())
        {
            return NetworkIdentityPublicKeyResult.Invalid("Windows CNG/KSP is required for Network Identity.");
        }

        if (!Exists(keyName))
        {
            return NetworkIdentityPublicKeyResult.Missing(keyName);
        }

        try
        {
            using var key = CngKey.Open(keyName, Provider, OpenOptions);
            var publicKey = ExportPublicKey(key);

            return NetworkIdentityPublicKeyResult.Found(
                publicKey.Fingerprint,
                publicKey.SubjectPublicKeyInfoBase64);
        }
        catch (CryptographicException exception)
        {
            return NetworkIdentityPublicKeyResult.Invalid(
                $"CNG network identity key could not be opened: {exception.Message}");
        }
        catch (PlatformNotSupportedException exception)
        {
            return NetworkIdentityPublicKeyResult.Invalid(
                $"CNG network identity key could not be opened: {exception.Message}");
        }
    }

    public NetworkIdentitySignatureResult Sign(string keyName, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        ArgumentNullException.ThrowIfNull(data);

        if (!OperatingSystem.IsWindows())
        {
            return NetworkIdentitySignatureResult.Invalid("Windows CNG/KSP is required for Network Identity.");
        }

        if (!Exists(keyName))
        {
            return NetworkIdentitySignatureResult.Missing(keyName);
        }

        try
        {
            using var key = CngKey.Open(keyName, Provider, OpenOptions);
            using var rsa = new RSACng(key);
            var signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            return NetworkIdentitySignatureResult.Success(Convert.ToBase64String(signature));
        }
        catch (CryptographicException exception)
        {
            return NetworkIdentitySignatureResult.Invalid(
                $"CNG network identity key could not sign data: {exception.Message}");
        }
        catch (PlatformNotSupportedException exception)
        {
            return NetworkIdentitySignatureResult.Invalid(
                $"CNG network identity key could not sign data: {exception.Message}");
        }
    }

    public NetworkIdentityCertificateResult CreateSelfSignedCertificate(
        string keyName,
        string subjectName,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);

        if (!OperatingSystem.IsWindows())
        {
            return NetworkIdentityCertificateResult.Invalid("Windows CNG/KSP is required for Network Identity.");
        }

        if (!Exists(keyName))
        {
            return NetworkIdentityCertificateResult.Missing(keyName);
        }

        if (notAfter <= notBefore)
        {
            return NetworkIdentityCertificateResult.Invalid("Network Identity certificate validity window is invalid.");
        }

        try
        {
            using var key = CngKey.Open(keyName, Provider, OpenOptions);
            using var rsa = new RSACng(key);
            var request = new CertificateRequest(
                new X500DistinguishedName($"CN={SanitizeSubjectName(subjectName)}"),
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                critical: true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new Oid("1.3.6.1.5.5.7.3.1"),
                    new Oid("1.3.6.1.5.5.7.3.2")
                },
                critical: false));

            var certificate = request.CreateSelfSigned(notBefore, notAfter);
            var publicKey = ExportPublicKey(key);

            return NetworkIdentityCertificateResult.Success(publicKey.Fingerprint, certificate);
        }
        catch (CryptographicException exception)
        {
            return NetworkIdentityCertificateResult.Invalid(
                $"Network Identity certificate could not be created: {exception.Message}");
        }
        catch (PlatformNotSupportedException exception)
        {
            return NetworkIdentityCertificateResult.Invalid(
                $"Network Identity certificate could not be created: {exception.Message}");
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

    private static NetworkIdentityPublicKey ExportPublicKey(CngKey key)
    {
        using var rsa = new RSACng(key);
        var publicKey = rsa.ExportSubjectPublicKeyInfo();
        var fingerprint = SHA256.HashData(publicKey);

        return new NetworkIdentityPublicKey(
            Convert.ToHexString(fingerprint).ToLowerInvariant(),
            Convert.ToBase64String(publicKey));
    }

    private static string ComputePublicKeyFingerprint(CngKey key)
    {
        return ExportPublicKey(key).Fingerprint;
    }

    private static string SanitizeSubjectName(string subjectName)
    {
        var sanitized = new char[subjectName.Length];
        for (var index = 0; index < subjectName.Length; index++)
        {
            var value = subjectName[index];
            sanitized[index] = char.IsLetterOrDigit(value) || value is ' ' or '-' or '_' or '.'
                ? value
                : '_';
        }

        return new string(sanitized).Trim();
    }

    private sealed record NetworkIdentityPublicKey(
        string Fingerprint,
        string SubjectPublicKeyInfoBase64);
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

    public NetworkIdentityPublicKeyResult GetPublicKey(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        return NetworkIdentityPublicKeyResult.Invalid(
            "Windows CNG/KSP is required for Network Identity.");
    }

    public NetworkIdentitySignatureResult Sign(string keyName, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        ArgumentNullException.ThrowIfNull(data);

        return NetworkIdentitySignatureResult.Invalid(
            "Windows CNG/KSP is required for Network Identity.");
    }

    public NetworkIdentityCertificateResult CreateSelfSignedCertificate(
        string keyName,
        string subjectName,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);

        return NetworkIdentityCertificateResult.Invalid(
            "Windows CNG/KSP is required for Network Identity.");
    }

    public NetworkIdentityKeyDeleteResult Delete(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        return NetworkIdentityKeyDeleteResult.Missing();
    }
}
