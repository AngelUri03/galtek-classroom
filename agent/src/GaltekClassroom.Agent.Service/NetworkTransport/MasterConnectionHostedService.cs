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
            var installationIdentity = await _runtimeState.WaitForInstallationIdentityAsync(stoppingToken);
            var networkIdentity = await _runtimeState.WaitForNetworkIdentityAsync(stoppingToken);

            await _connectionClient.RunAsync(
                installationIdentity,
                networkIdentity,
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Master gRPC connection stopped.");
        }
    }
}
