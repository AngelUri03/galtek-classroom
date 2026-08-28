using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GaltekClassroom.Agent.Service.Pairing;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class MasterCertificatePinningPolicy
{
    public bool IsCertificateTrusted(
        X509Certificate2? certificate,
        AuthorizedMasterTrustRecord trustedMaster,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(trustedMaster);

        if (certificate is null)
        {
            return false;
        }

        if (nowUtc.UtcDateTime < certificate.NotBefore.ToUniversalTime()
            || nowUtc.UtcDateTime > certificate.NotAfter.ToUniversalTime())
        {
            return false;
        }

        try
        {
            using var rsa = certificate.GetRSAPublicKey();
            if (rsa is null)
            {
                return false;
            }

            var spki = rsa.ExportSubjectPublicKeyInfo();
            var fingerprint = Convert.ToHexString(SHA256.HashData(spki)).ToLowerInvariant();
            var spkiBase64 = Convert.ToBase64String(spki);

            return string.Equals(
                    fingerprint,
                    trustedMaster.MasterPublicKeyFingerprint,
                    StringComparison.Ordinal)
                && string.Equals(
                    spkiBase64,
                    trustedMaster.MasterPublicKeySubjectPublicKeyInfoBase64,
                    StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
