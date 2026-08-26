using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;

namespace GaltekClassroom.Agent.Service.Master;

public interface IAdministratorPrivilegeChecker
{
    bool IsElevatedAdministrator();
}

public sealed class UnsupportedAdministratorPrivilegeChecker : IAdministratorPrivilegeChecker
{
    public bool IsElevatedAdministrator()
    {
        return false;
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsAdministratorPrivilegeChecker : IAdministratorPrivilegeChecker
{
    public bool IsElevatedAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);

            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException
            or SecurityException
            or SystemException)
        {
            return false;
        }
    }
}
