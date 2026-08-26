using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;

namespace GaltekClassroom.Agent.Service.Master;

public sealed record WindowsAccountIdentity(
    string WindowsSid,
    string AccountDisplayName);

public sealed record WindowsAccountResolution(
    bool Found,
    WindowsAccountIdentity? Identity,
    string? ErrorMessage)
{
    public static WindowsAccountResolution Resolved(WindowsAccountIdentity identity)
    {
        return new WindowsAccountResolution(true, identity, null);
    }

    public static WindowsAccountResolution NotFound(string errorMessage)
    {
        return new WindowsAccountResolution(false, null, errorMessage);
    }
}

public interface IWindowsAccountResolver
{
    WindowsAccountResolution ResolveCurrentUser();

    WindowsAccountResolution ResolveAccount(string accountName);
}

public sealed class UnsupportedWindowsAccountResolver : IWindowsAccountResolver
{
    public WindowsAccountResolution ResolveCurrentUser()
    {
        return WindowsAccountResolution.NotFound("Windows account resolution is only supported on Windows.");
    }

    public WindowsAccountResolution ResolveAccount(string accountName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        return WindowsAccountResolution.NotFound("Windows account resolution is only supported on Windows.");
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsAccountResolver : IWindowsAccountResolver
{
    public WindowsAccountResolution ResolveCurrentUser()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value;

            if (string.IsNullOrWhiteSpace(sid))
            {
                return WindowsAccountResolution.NotFound("Current Windows user SID could not be resolved.");
            }

            return WindowsAccountResolution.Resolved(new WindowsAccountIdentity(
                sid,
                string.IsNullOrWhiteSpace(identity.Name) ? sid : identity.Name));
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException
            or SecurityException
            or SystemException)
        {
            return WindowsAccountResolution.NotFound(
                $"Current Windows user could not be resolved: {exception.Message}");
        }
    }

    public WindowsAccountResolution ResolveAccount(string accountName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        try
        {
            var ntAccount = new NTAccount(accountName.Trim());
            var sid = (SecurityIdentifier)ntAccount.Translate(typeof(SecurityIdentifier));
            var displayName = ResolveDisplayName(sid, ntAccount.Value);

            return WindowsAccountResolution.Resolved(new WindowsAccountIdentity(
                sid.Value,
                displayName));
        }
        catch (Exception exception) when (
            exception is IdentityNotMappedException
            or ArgumentException
            or SystemException)
        {
            return WindowsAccountResolution.NotFound(
                $"Windows account was not found: {exception.Message}");
        }
    }

    private static string ResolveDisplayName(SecurityIdentifier sid, string fallback)
    {
        try
        {
            return ((NTAccount)sid.Translate(typeof(NTAccount))).Value;
        }
        catch (Exception exception) when (
            exception is IdentityNotMappedException
            or SystemException)
        {
            return fallback;
        }
    }
}
