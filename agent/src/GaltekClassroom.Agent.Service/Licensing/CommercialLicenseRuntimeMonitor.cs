using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Agent.Service.Runtime;

namespace GaltekClassroom.Agent.Service.Licensing;

public sealed class CommercialLicenseRuntimeMonitor : BackgroundService
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);

    private readonly CommercialLicenseManager _licenseManager;
    private readonly AgentRuntimeState _runtimeState;
    private readonly ILogger<CommercialLicenseRuntimeMonitor> _logger;

    public CommercialLicenseRuntimeMonitor(
        CommercialLicenseManager licenseManager,
        AgentRuntimeState runtimeState,
        ILogger<CommercialLicenseRuntimeMonitor> logger)
    {
        _licenseManager = licenseManager;
        _runtimeState = runtimeState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        await ResolveInitialLicenseAsync(stoppingToken);

        using var timer = new PeriodicTimer(CheckInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var before = _licenseManager.CurrentState;
                var after = _licenseManager.RefreshExpirationOnly();

                if (before.Active && after.Status == CommercialLicenseStatus.LicenseExpired)
                {
                    _logger.LogWarning(
                        "Commercial license expired at {ExpiresAtUtc}.",
                        after.ExpiresAtUtc);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Commercial license runtime monitor stopping.");
        }
    }

    private async Task ResolveInitialLicenseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var licenseState = await _licenseManager.ResolveAsync(
                _runtimeState.GetInstallationIdentity(),
                cancellationToken);

            if (licenseState.Active)
            {
                _runtimeState.MarkOperationReady();
                _logger.LogInformation(
                    "Commercial license active. LicenseId: {LicenseId}. ExpiresAtUtc: {ExpiresAtUtc}. Roles: {Roles}.",
                    licenseState.LicenseId,
                    licenseState.ExpiresAtUtc,
                    string.Join(",", licenseState.Roles));
            }
            else
            {
                _logger.LogWarning(
                    "Commercial license is not active. Status: {LicenseStatus}. Reason: {BlockingReason}",
                    licenseState.Status.ToCode(),
                    licenseState.BlockingReason);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _runtimeState.MarkDegraded();
            _logger.LogError(exception, "Commercial license validation failed during startup recovery.");
        }
    }
}
