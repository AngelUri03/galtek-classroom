using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Identity;

public static class InstallationIdentityValidator
{
    public static bool IsValid(InstallationIdentity? identity, out string error)
    {
        if (identity is null)
        {
            error = "identity payload is empty";
            return false;
        }

        if (identity.SchemaVersion != InstallationIdentityConstants.SchemaVersion)
        {
            error = $"unsupported schemaVersion {identity.SchemaVersion}";
            return false;
        }

        if (identity.InstallationId == Guid.Empty)
        {
            error = "installationId is empty";
            return false;
        }

        if (!IsValidSha256Hex(identity.CpuHash))
        {
            error = "cpuHash is missing or invalid";
            return false;
        }

        if (!IsValidSha256Hex(identity.MotherboardHash))
        {
            error = "motherboardHash is missing or invalid";
            return false;
        }

        if (!IsValidSha256Hex(identity.MacHash))
        {
            error = "macHash is missing or invalid";
            return false;
        }

        if (!IsValidSha256Hex(identity.DiskHash))
        {
            error = "diskHash is missing or invalid";
            return false;
        }

        if (identity.CreatedAtUtc == default)
        {
            error = "createdAtUtc is missing";
            return false;
        }

        if (identity.CreatedAtUtc.Offset != TimeSpan.Zero)
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
}
