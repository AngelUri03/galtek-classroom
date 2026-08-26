using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Licensing;

public sealed record CommercialLicenseValidationResult(LicenseState State)
{
    public bool IsActive => State.Active;
}
