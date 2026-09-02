namespace GaltekClassroom.Agent.Shared;

public sealed record OpenUrlValidationResult(
    bool IsValid,
    string? ErrorCode,
    Uri? Uri)
{
    public static OpenUrlValidationResult Valid(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return new OpenUrlValidationResult(true, null, uri);
    }

    public static OpenUrlValidationResult Invalid(string errorCode)
    {
        return new OpenUrlValidationResult(false, errorCode, null);
    }
}

public sealed class OpenUrlSafetyPolicy
{
    public const int MaxUrlLength = 4096;

    public OpenUrlValidationResult Validate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return OpenUrlValidationResult.Invalid(SessionCommandErrorCodes.InvalidUrl);
        }

        if (url.Length > MaxUrlLength || ContainsControlCharacter(url))
        {
            return OpenUrlValidationResult.Invalid(SessionCommandErrorCodes.InvalidUrl);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return OpenUrlValidationResult.Invalid(SessionCommandErrorCodes.InvalidUrl);
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return OpenUrlValidationResult.Invalid(SessionCommandErrorCodes.InvalidUrl);
        }

        if (string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return OpenUrlValidationResult.Invalid(SessionCommandErrorCodes.InvalidUrl);
        }

        return OpenUrlValidationResult.Valid(uri);
    }

    private static bool ContainsControlCharacter(string value)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }
}
