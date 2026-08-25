using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Agent.Service.Identity;

namespace GaltekClassroom.Agent.Service;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly InstallationIdentityResolver _installationIdentityResolver;

    public Worker(
        ILogger<Worker> logger,
        InstallationIdentityResolver installationIdentityResolver)
    {
        _logger = logger;
        _installationIdentityResolver = installationIdentityResolver;
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
