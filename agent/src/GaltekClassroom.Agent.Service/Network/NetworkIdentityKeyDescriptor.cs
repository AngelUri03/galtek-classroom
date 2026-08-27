using System.Security.Cryptography;
using System.Text;

namespace GaltekClassroom.Agent.Service.Network;

public sealed record NetworkIdentityKeyDescriptor(string KeyId, string KeyName)
{
    public static NetworkIdentityKeyDescriptor ForInstallation(Guid installationId)
    {
        if (installationId == Guid.Empty)
        {
            throw new ArgumentException("installationId cannot be empty.", nameof(installationId));
        }

        var source = $"{NetworkIdentityConstants.KeyDerivationNamespace}:{installationId:D}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        var keyId = Convert.ToHexString(hash).ToLowerInvariant();

        return new NetworkIdentityKeyDescriptor(
            keyId,
            $"{NetworkIdentityConstants.KeyNamePrefix}{keyId}");
    }
}
