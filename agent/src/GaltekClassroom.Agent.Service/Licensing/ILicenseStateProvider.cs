using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Licensing;

public interface ILicenseStateProvider
{
    LicenseState CurrentState { get; }
}
