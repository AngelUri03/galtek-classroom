using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Licensing;

public sealed record CommercialLicenseActivationResult(
    bool Activated,
    LicenseState CandidateState,
    LicenseState CurrentState);
