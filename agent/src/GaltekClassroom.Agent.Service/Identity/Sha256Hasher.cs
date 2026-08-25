using System.Security.Cryptography;
using System.Text;

namespace GaltekClassroom.Agent.Service.Identity;

public static class Sha256Hasher
{
    public static string Hash(string normalizedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedValue);

        var bytes = Encoding.UTF8.GetBytes(normalizedValue);
        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
