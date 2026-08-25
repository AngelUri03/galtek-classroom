using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("{ServiceName} starting.", ProductInfo.ServiceDisplayName);
        return base.StartAsync(cancellationToken);
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
