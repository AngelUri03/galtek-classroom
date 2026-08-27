namespace GaltekClassroom.Agent.Service.Network;

public static class NetworkIdentityValidator
{
    public static bool IsValid(NetworkIdentityMetadata? metadata, out string error)
    {
        if (metadata is null)
        {
            error = "network identity payload is empty";
            return false;
        }

        if (metadata.SchemaVersion != NetworkIdentityConstants.SchemaVersion)
        {
            error = $"unsupported schemaVersion {metadata.SchemaVersion}";
            return false;
        }

        if (metadata.NetworkIdentityId == Guid.Empty)
        {
            error = "networkIdentityId is empty";
            return false;
        }

        if (metadata.InstallationId == Guid.Empty)
        {
            error = "installationId is empty";
            return false;
        }

        if (!IsValidSha256Hex(metadata.KeyId))
        {
            error = "keyId is missing or invalid";
            return false;
        }

        if (!IsValidKeyName(metadata.KeyName))
        {
            error = "keyName is missing or invalid";
            return false;
        }

        var expectedDescriptor = NetworkIdentityKeyDescriptor.ForInstallation(metadata.InstallationId);
        if (!string.Equals(metadata.KeyId, expectedDescriptor.KeyId, StringComparison.Ordinal))
        {
            error = "keyId does not match installationId";
            return false;
        }

        if (!string.Equals(metadata.KeyName, expectedDescriptor.KeyName, StringComparison.Ordinal))
        {
            error = "keyName does not match installationId";
            return false;
        }

        if (!IsValidSha256Hex(metadata.PublicKeyFingerprint))
        {
            error = "publicKeyFingerprint is missing or invalid";
            return false;
        }

        if (metadata.CreatedAtUtc == default)
        {
            error = "createdAtUtc is missing";
            return false;
        }

        if (metadata.CreatedAtUtc.Offset != TimeSpan.Zero)
        {
            error = "createdAtUtc must be UTC";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool IsValidSha256Hex(string? value)
    {
        return value is { Length: 64 }
            && value.All(character =>
                character is >= '0' and <= '9'
                || character is >= 'a' and <= 'f');
    }

    public static bool IsValidKeyName(string? value)
    {
        return value is { Length: > 0 and <= 512 }
            && value.StartsWith(NetworkIdentityConstants.KeyNamePrefix, StringComparison.Ordinal)
            && !value.Any(character => character is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|');
    }
}
