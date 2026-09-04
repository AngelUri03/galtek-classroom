using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GaltekClassroom.Agent.Service.ManagedAccounts;

public interface IManagedWindowsAccountBindingFileSecurity
{
    void Apply(string filePath);
}

public sealed class NoOpManagedWindowsAccountBindingFileSecurity : IManagedWindowsAccountBindingFileSecurity
{
    public void Apply(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsManagedWindowsAccountBindingFileSecurity : IManagedWindowsAccountBindingFileSecurity
{
    public void Apply(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        FileSystemAclExtensions.SetAccessControl(new FileInfo(filePath), security);
    }
}
