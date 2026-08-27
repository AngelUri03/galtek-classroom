using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GaltekClassroom.Agent.Service.Network;

namespace GaltekClassroom.Agent.Service.Pairing;

public static class PairingCrypto
{
    public static string CreateNonceBase64()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(PairingConstants.NonceSizeBytes));
    }

    public static byte[] CanonicalChallengeBytes(PairingChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        return Encoding.UTF8.GetBytes(string.Join('\n',
            $"schemaVersion={challenge.SchemaVersion}",
            $"purpose={challenge.Purpose}",
            $"challengeId={challenge.ChallengeId:D}",
            $"masterNetworkIdentityId={challenge.MasterNetworkIdentityId:D}",
            $"clientNetworkIdentityId={challenge.ClientNetworkIdentityId:D}",
            $"clientInstallationId={challenge.ClientInstallationId:D}",
            $"masterPublicKeyFingerprint={challenge.MasterPublicKeyFingerprint}",
            $"clientPublicKeyFingerprint={challenge.ClientPublicKeyFingerprint}",
            $"masterPublicKeySubjectPublicKeyInfoBase64={challenge.MasterPublicKeySubjectPublicKeyInfoBase64}",
            $"clientPublicKeySubjectPublicKeyInfoBase64={challenge.ClientPublicKeySubjectPublicKeyInfoBase64}",
            $"nonceBase64={challenge.NonceBase64}",
            $"issuedAtUtc={FormatUtc(challenge.IssuedAtUtc)}",
            $"expiresAtUtc={FormatUtc(challenge.ExpiresAtUtc)}"));
    }

    public static byte[] CanonicalResponseBytes(PairingResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return Encoding.UTF8.GetBytes(string.Join('\n',
            $"schemaVersion={response.SchemaVersion}",
            $"purpose={response.Purpose}",
            $"challengeId={response.ChallengeId:D}",
            $"masterNetworkIdentityId={response.MasterNetworkIdentityId:D}",
            $"clientNetworkIdentityId={response.ClientNetworkIdentityId:D}",
            $"clientInstallationId={response.ClientInstallationId:D}",
            $"masterPublicKeyFingerprint={response.MasterPublicKeyFingerprint}",
            $"clientPublicKeyFingerprint={response.ClientPublicKeyFingerprint}",
            $"challengeNonceBase64={response.ChallengeNonceBase64}",
            $"responseNonceBase64={response.ResponseNonceBase64}",
            $"signedAtUtc={FormatUtc(response.SignedAtUtc)}"));
    }

    public static bool VerifySignature(
        string subjectPublicKeyInfoBase64,
        byte[] canonicalPayload,
        string signatureBase64)
    {
        ArgumentNullException.ThrowIfNull(canonicalPayload);

        try
        {
            var publicKey = Convert.FromBase64String(subjectPublicKeyInfoBase64);
            var signature = Convert.FromBase64String(signatureBase64);

            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(publicKey, out _);

            return rsa.VerifyData(
                canonicalPayload,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (Exception exception) when (
            exception is FormatException
                or ArgumentException
                or CryptographicException)
        {
            return false;
        }
    }

    public static bool TryComputePublicKeyFingerprint(
        string subjectPublicKeyInfoBase64,
        out string publicKeyFingerprint)
    {
        publicKeyFingerprint = string.Empty;

        try
        {
            var publicKey = Convert.FromBase64String(subjectPublicKeyInfoBase64);
            publicKeyFingerprint = Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();

            return NetworkIdentityValidator.IsValidSha256Hex(publicKeyFingerprint);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool IsValidNonce(string? nonceBase64)
    {
        if (string.IsNullOrWhiteSpace(nonceBase64))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(nonceBase64).Length >= 16;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string FormatUtc(DateTimeOffset value)
    {
        return value
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
    }
}
