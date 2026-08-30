using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Network;
using GaltekClassroom.Agent.Service.Runtime;

namespace GaltekClassroom.Agent.Service;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ServiceRunMarker _runMarker;
    private readonly InstallationIdentityResolver _installationIdentityResolver;
    private readonly NetworkIdentityResolver _networkIdentityResolver;
    private readonly AgentRuntimeState _runtimeState;

    public Worker(
        ILogger<Worker> logger,
        ServiceRunMarker runMarker,
        InstallationIdentityResolver installationIdentityResolver,
        NetworkIdentityResolver networkIdentityResolver,
        AgentRuntimeState runtimeState)
    {
        _logger = logger;
        _runMarker = runMarker;
        _installationIdentityResolver = installationIdentityResolver;
        _networkIdentityResolver = networkIdentityResolver;
        _runtimeState = runtimeState;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("{ServiceName} starting.", ProductInfo.ServiceDisplayName);

        var marker = await _runMarker.MarkStartedAsync(cancellationToken);
        _runtimeState.ObservePreviousShutdown(marker.PreviousShutdownWasUnclean);

        if (marker.PreviousShutdownWasUnclean)
        {
            _logger.LogWarning(
                "Previous Agent Service shutdown was not clean. Recovery checks will run before network operation.");
        }

        if (!marker.MarkerWritten)
        {
            _logger.LogWarning(
                "Agent Service running marker could not be written at {FilePath}: {ErrorMessage}",
                marker.FilePath,
                marker.ErrorMessage);
        }

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

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{ServiceName} is active.", ProductInfo.ServiceDisplayName);

        try
        {
            await ResolveNetworkIdentityAsync(stoppingToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("{ServiceName} received shutdown signal.", ProductInfo.ServiceDisplayName);
        }
    }

    private async Task ResolveNetworkIdentityAsync(CancellationToken cancellationToken)
    {
        try
        {
            var networkIdentityResolution = await _networkIdentityResolver.ResolveAsync(
                _runtimeState.GetInstallationIdentity(),
                cancellationToken);

            if (networkIdentityResolution.Status != NetworkIdentityStatus.Ready)
            {
                _runtimeState.MarkDegraded();
                _logger.LogError(
                    "Network identity is not usable at {FilePath}. Status: {Status}. Reason: {ErrorMessage}",
                    networkIdentityResolution.FilePath,
                    networkIdentityResolution.ErrorCode ?? networkIdentityResolution.Status.ToCode(),
                    networkIdentityResolution.ErrorMessage);

                return;
            }

            _logger.LogInformation(
                "Network identity ready. NetworkIdentityId: {NetworkIdentityId}. PublicKeyFingerprint: {PublicKeyFingerprint}",
                networkIdentityResolution.Metadata!.NetworkIdentityId,
                networkIdentityResolution.Metadata.PublicKeyFingerprint);

            _runtimeState.SetNetworkIdentity(networkIdentityResolution.Metadata);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _runtimeState.MarkDegraded();
            _logger.LogError(exception, "Network identity resolution failed during startup recovery.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("{ServiceName} stopping cleanly.", ProductInfo.ServiceDisplayName);
        await base.StopAsync(cancellationToken);

        var marker = await _runMarker.MarkStoppedAsync(cancellationToken);
        if (!marker.Removed)
        {
            _logger.LogWarning(
                "Agent Service running marker could not be removed at {FilePath}: {ErrorMessage}",
                marker.FilePath,
                marker.ErrorMessage);
        }
    }
}
