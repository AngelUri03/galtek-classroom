using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GaltekClassroom.Agent.Service.Applications;

public interface IApplicationBindingFileSecurity
{
    void Apply(string filePath);
}

public sealed class NoOpApplicationBindingFileSecurity : IApplicationBindingFileSecurity
{
    public void Apply(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsApplicationBindingFileSecurity : IApplicationBindingFileSecurity
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
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Read,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            FileSystemRights.Read,
            AccessControlType.Allow));

        FileSystemAclExtensions.SetAccessControl(new FileInfo(filePath), security);
    }
}
