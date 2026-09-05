using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace GaltekClassroom.Agent.Service.Master;

public enum WindowsAccountSidNameUse
{
    Unknown = 0,
    User = 1,
    Group = 2,
    Domain = 3,
    Alias = 4,
    WellKnownGroup = 5,
    DeletedAccount = 6,
    Invalid = 7,
    Computer = 9
}

public sealed record WindowsAccountIdentity(
    string WindowsSid,
    string AccountDisplayName,
    WindowsAccountSidNameUse SidNameUse = WindowsAccountSidNameUse.User)
{
    public string? Domain { get; init; }

    public string? Username { get; init; }
}

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

    WindowsAccountResolution ResolveSid(string windowsSid);
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

    public WindowsAccountResolution ResolveSid(string windowsSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsSid);

        return WindowsAccountResolution.NotFound("Windows SID resolution is only supported on Windows.");
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsAccountResolver : IWindowsAccountResolver
{
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoneMapped = 1332;
    private const int ErrorInvalidSid = 1337;

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

            return ResolveSid(sid);
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

        var normalizedName = WindowsAccountNameNormalizer.NormalizeAccountName(
            accountName,
            Environment.MachineName);

        try
        {
            var sidLength = 0;
            var domainLength = 0;

            _ = LookupAccountName(
                null,
                normalizedName,
                null,
                ref sidLength,
                null,
                ref domainLength,
                out _);

            var initialError = Marshal.GetLastWin32Error();
            if (initialError == ErrorNoneMapped)
            {
                return WindowsAccountResolution.NotFound("Windows account was not found.");
            }

            if (initialError != ErrorInsufficientBuffer || sidLength <= 0 || domainLength <= 0)
            {
                return WindowsAccountResolution.NotFound(
                    $"Windows account lookup failed: {new Win32Exception(initialError).Message}");
            }

            var sid = new byte[sidLength];
            var domain = new StringBuilder(domainLength);
            var use = NativeSidNameUse.Unknown;
            var resolved = LookupAccountName(
                null,
                normalizedName,
                sid,
                ref sidLength,
                domain,
                ref domainLength,
                out use);

            if (!resolved)
            {
                var error = Marshal.GetLastWin32Error();
                return WindowsAccountResolution.NotFound(
                    $"Windows account lookup failed: {new Win32Exception(error).Message}");
            }

            if (use != NativeSidNameUse.User)
            {
                return WindowsAccountResolution.NotFound(
                    $"Windows account '{normalizedName}' resolved to {use}, not SidTypeUser.");
            }

            var sidString = ConvertSidToString(sid);
            if (sidString is null)
            {
                return WindowsAccountResolution.NotFound("Windows account SID could not be converted to string.");
            }

            var canonical = ResolveSid(sidString);
            if (!canonical.Found || canonical.Identity is null)
            {
                return WindowsAccountResolution.NotFound(
                    canonical.ErrorMessage ?? "Windows account canonical reference could not be resolved.");
            }

            return canonical;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or SystemException)
        {
            return WindowsAccountResolution.NotFound(
                $"Windows account was not found: {exception.Message}");
        }
    }

    public WindowsAccountResolution ResolveSid(string windowsSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsSid);

        var nativeSid = IntPtr.Zero;
        try
        {
            if (!ConvertStringSidToSid(windowsSid.Trim(), out nativeSid))
            {
                var error = Marshal.GetLastWin32Error();
                return WindowsAccountResolution.NotFound(
                    error == ErrorInvalidSid
                        ? "Windows SID is invalid."
                        : $"Windows SID conversion failed: {new Win32Exception(error).Message}");
            }

            var nameLength = 0;
            var domainLength = 0;

            _ = LookupAccountSid(
                null,
                nativeSid,
                null,
                ref nameLength,
                null,
                ref domainLength,
                out _);

            var initialError = Marshal.GetLastWin32Error();
            if (initialError == ErrorNoneMapped)
            {
                return WindowsAccountResolution.NotFound("Windows SID is not mapped to an account.");
            }

            if (initialError != ErrorInsufficientBuffer || nameLength <= 0 || domainLength <= 0)
            {
                return WindowsAccountResolution.NotFound(
                    $"Windows SID lookup failed: {new Win32Exception(initialError).Message}");
            }

            var name = new StringBuilder(nameLength);
            var domain = new StringBuilder(domainLength);
            var use = NativeSidNameUse.Unknown;
            var resolved = LookupAccountSid(
                null,
                nativeSid,
                name,
                ref nameLength,
                domain,
                ref domainLength,
                out use);

            if (!resolved)
            {
                var error = Marshal.GetLastWin32Error();
                return WindowsAccountResolution.NotFound(
                    $"Windows SID lookup failed: {new Win32Exception(error).Message}");
            }

            if (use != NativeSidNameUse.User)
            {
                return WindowsAccountResolution.NotFound(
                    $"Windows SID resolved to {use}, not SidTypeUser.");
            }

            var sidString = ConvertNativeSidToString(nativeSid);
            if (sidString is null)
            {
                return WindowsAccountResolution.NotFound("Windows SID could not be converted to canonical string.");
            }

            return WindowsAccountResolution.Resolved(
                new WindowsAccountIdentity(
                    sidString,
                    $"{domain}\\{name}",
                    WindowsAccountSidNameUse.User)
                {
                    Domain = domain.ToString(),
                    Username = name.ToString()
                });
        }
        catch (Exception exception) when (exception is ArgumentException or SystemException)
        {
            return WindowsAccountResolution.NotFound(
                $"Windows SID could not be resolved: {exception.Message}");
        }
        finally
        {
            if (nativeSid != IntPtr.Zero)
            {
                LocalFree(nativeSid);
            }
        }
    }

    private static string? ConvertSidToString(byte[] sid)
    {
        var sidPointer = IntPtr.Zero;
        try
        {
            sidPointer = Marshal.AllocHGlobal(sid.Length);
            Marshal.Copy(sid, 0, sidPointer, sid.Length);
            return ConvertNativeSidToString(sidPointer);
        }
        finally
        {
            if (sidPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(sidPointer);
            }
        }
    }

    private static string? ConvertNativeSidToString(IntPtr sid)
    {
        if (!ConvertSidToStringSid(sid, out var stringSidPointer))
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(stringSidPointer);
        }
        finally
        {
            LocalFree(stringSidPointer);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LookupAccountNameW")]
    private static extern bool LookupAccountName(
        string? lpSystemName,
        string lpAccountName,
        byte[]? Sid,
        ref int cbSid,
        StringBuilder? ReferencedDomainName,
        ref int cchReferencedDomainName,
        out NativeSidNameUse peUse);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LookupAccountSidW")]
    private static extern bool LookupAccountSid(
        string? lpSystemName,
        IntPtr Sid,
        StringBuilder? Name,
        ref int cchName,
        StringBuilder? ReferencedDomainName,
        ref int cchReferencedDomainName,
        out NativeSidNameUse peUse);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "ConvertSidToStringSidW")]
    private static extern bool ConvertSidToStringSid(
        IntPtr Sid,
        out IntPtr StringSid);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "ConvertStringSidToSidW")]
    private static extern bool ConvertStringSidToSid(
        string StringSid,
        out IntPtr Sid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private enum NativeSidNameUse
    {
        User = 1,
        Group = 2,
        Domain = 3,
        Alias = 4,
        WellKnownGroup = 5,
        DeletedAccount = 6,
        Invalid = 7,
        Unknown = 8,
        Computer = 9,
        Label = 10,
        LogonSession = 11
    }
}

public static class WindowsAccountNameNormalizer
{
    public static string NormalizeAccountName(string accountName, string machineName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);

        var trimmed = accountName.Trim();
        if (trimmed.Contains('\\', StringComparison.Ordinal)
            || trimmed.Contains('@', StringComparison.Ordinal))
        {
            return trimmed;
        }

        return $"{machineName.Trim()}\\{trimmed}";
    }
}
