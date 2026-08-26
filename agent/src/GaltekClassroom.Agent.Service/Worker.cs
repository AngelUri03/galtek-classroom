using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Service.Runtime;

namespace GaltekClassroom.Agent.Service;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly InstallationIdentityResolver _installationIdentityResolver;
    private readonly CommercialLicenseManager _licenseManager;
    private readonly AgentRuntimeState _runtimeState;

    public Worker(
        ILogger<Worker> logger,
        InstallationIdentityResolver installationIdentityResolver,
        CommercialLicenseManager licenseManager,
        AgentRuntimeState runtimeState)
    {
        _logger = logger;
        _installationIdentityResolver = installationIdentityResolver;
        _licenseManager = licenseManager;
        _runtimeState = runtimeState;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("{ServiceName} starting.", ProductInfo.ServiceDisplayName);

        var resolution = await _installationIdentityResolver.ResolveAsync(cancellationToken);

        if (resolution.Status != InstallationIdentityResolutionStatus.Ready)
        {
            _logger.LogError(
                "Installation identity is not usable at {FilePath}: {ErrorMessage}",
                resolution.FilePath,
                resolution.ErrorMessage);

            throw new InvalidOperationException(
                $"Installation identity is not usable at {resolution.FilePath}: {resolution.ErrorMessage}");
        }

        _logger.LogInformation(
            "Installation identity ready. InstallationId: {InstallationId}",
            resolution.Identity!.InstallationId);

        _runtimeState.SetInstallationIdentity(resolution.Identity);

        var licenseState = await _licenseManager.ResolveAsync(resolution.Identity, cancellationToken);

        if (licenseState.Active)
        {
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

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{ServiceName} is active.", ProductInfo.ServiceDisplayName);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("{ServiceName} received shutdown signal.", ProductInfo.ServiceDisplayName);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("{ServiceName} stopping cleanly.", ProductInfo.ServiceDisplayName);
        await base.StopAsync(cancellationToken);
    }
}
