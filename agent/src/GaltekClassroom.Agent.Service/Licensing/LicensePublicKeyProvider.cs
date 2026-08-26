using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace GaltekClassroom.Agent.Service.Licensing;

public sealed class LicensePublicKeyProvider : ILicensePublicKeyProvider
{
    public const string EnvironmentVariableName = "GALTEK_CLASSROOM_LICENSE_PUBLIC_KEY_PATH";

    public async Task<LicensePublicKeyResult> GetPublicKeyAsync(CancellationToken cancellationToken)
    {
        var configuredPath = Environment.GetEnvironmentVariable(EnvironmentVariableName);

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return LicensePublicKeyResult.NotConfigured(
                "No Galtek Hub public key is configured. Set GALTEK_CLASSROOM_LICENSE_PUBLIC_KEY_PATH for development.");
        }

        var fullPath = Path.GetFullPath(configuredPath);

        if (!File.Exists(fullPath))
        {
            return LicensePublicKeyResult.NotConfigured(
                $"Configured Galtek Hub public key does not exist: {fullPath}");
        }

        try
        {
            var pem = await File.ReadAllTextAsync(fullPath, cancellationToken);

            if (pem.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
            {
                return LicensePublicKeyResult.NotConfigured(
                    "Configured Galtek Hub key must be a public key, not a private key.");
            }

            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);

            return LicensePublicKeyResult.Configured(new RsaSecurityKey(rsa));
        }
        catch (ArgumentException exception)
        {
            return LicensePublicKeyResult.NotConfigured(
                $"Configured Galtek Hub public key is invalid: {exception.Message}");
        }
        catch (CryptographicException exception)
        {
            return LicensePublicKeyResult.NotConfigured(
                $"Configured Galtek Hub public key is invalid: {exception.Message}");
        }
        catch (IOException exception)
        {
            return LicensePublicKeyResult.NotConfigured(
                $"Configured Galtek Hub public key could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return LicensePublicKeyResult.NotConfigured(
                $"Configured Galtek Hub public key could not be read: {exception.Message}");
        }
    }
}
