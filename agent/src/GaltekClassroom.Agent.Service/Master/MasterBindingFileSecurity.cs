using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GaltekClassroom.Agent.Service.Master;

public interface IMasterBindingFileSecurity
{
    void Apply(string filePath);
}

public sealed class NoOpMasterBindingFileSecurity : IMasterBindingFileSecurity
{
    public void Apply(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsMasterBindingFileSecurity : IMasterBindingFileSecurity
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
