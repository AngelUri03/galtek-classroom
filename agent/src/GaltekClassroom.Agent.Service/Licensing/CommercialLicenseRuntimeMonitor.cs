using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Licensing;

public sealed class CommercialLicenseRuntimeMonitor : BackgroundService
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);

    private readonly CommercialLicenseManager _licenseManager;
    private readonly ILogger<CommercialLicenseRuntimeMonitor> _logger;

    public CommercialLicenseRuntimeMonitor(
        CommercialLicenseManager licenseManager,
        ILogger<CommercialLicenseRuntimeMonitor> logger)
    {
        _licenseManager = licenseManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
}
