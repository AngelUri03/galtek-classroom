using GaltekClassroom.Agent.Service.Runtime;

namespace GaltekClassroom.Agent.Service.NetworkTransport;

public sealed class MasterConnectionHostedService : BackgroundService
{
    private readonly ILogger<MasterConnectionHostedService> _logger;
    private readonly MasterConnectionOptions _options;
    private readonly AgentRuntimeState _runtimeState;
    private readonly MasterGrpcConnectionClient _connectionClient;

    public MasterConnectionHostedService(
        ILogger<MasterConnectionHostedService> logger,
        MasterConnectionOptions options,
        AgentRuntimeState runtimeState,
        MasterGrpcConnectionClient connectionClient)
    {
        _logger = logger;
        _options = options;
        _runtimeState = runtimeState;
        _connectionClient = connectionClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            await _connectionClient.RunAsync(
                _runtimeState.GetInstallationIdentity(),
                _runtimeState.GetNetworkIdentity(),
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Master gRPC connection stopped.");
        }
    }
}
