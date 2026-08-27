using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GaltekClassroom.Agent.Service.Network;

public interface INetworkIdentityFileSecurity
{
    void Apply(string filePath);
}

public sealed class NoOpNetworkIdentityFileSecurity : INetworkIdentityFileSecurity
{
    public void Apply(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkIdentityFileSecurity : INetworkIdentityFileSecurity
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
