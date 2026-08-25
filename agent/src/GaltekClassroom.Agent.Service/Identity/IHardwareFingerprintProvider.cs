using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Identity;

public interface IHardwareFingerprintProvider
{
    Task<HardwareFingerprint> GetCurrentAsync(CancellationToken cancellationToken);
}
