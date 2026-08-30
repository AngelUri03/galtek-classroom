using System.IO.Pipes;
using System.Text.Json;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Ipc;

public sealed class LocalIpcServer : BackgroundService
{
    private readonly LocalIpcServerOptions _options;
    private readonly ILocalIpcPipeStreamFactory _pipeStreamFactory;
    private readonly ILocalIpcClientIdentityProvider _clientIdentityProvider;
    private readonly ILocalIpcRequestHandler _requestHandler;
    private readonly ILogger<LocalIpcServer> _logger;

    public LocalIpcServer(
        LocalIpcServerOptions options,
        ILocalIpcPipeStreamFactory pipeStreamFactory,
        ILocalIpcClientIdentityProvider clientIdentityProvider,
        ILocalIpcRequestHandler requestHandler,
        ILogger<LocalIpcServer> logger)
    {
        _options = options;
        _pipeStreamFactory = pipeStreamFactory;
        _clientIdentityProvider = clientIdentityProvider;
        _requestHandler = requestHandler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Local IPC server starting on pipe {PipeName}.",
            _options.PipeName);

        var acceptLoops = new Task[_options.MaxConcurrentConnections];
        for (var index = 0; index < acceptLoops.Length; index++)
        {
            acceptLoops[index] = AcceptLoopAsync(stoppingToken);
        }

        await Task.WhenAll(acceptLoops);
    }

    private async Task AcceptLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe = _pipeStreamFactory.CreateServerStream(_options);

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
                _logger.LogDebug("IPC client connected.");
                await ProcessConnectionAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "IPC client disconnected unexpectedly.");
            }
            catch (LocalIpcFramingException exception)
            {
                _logger.LogWarning(exception, "IPC malformed frame rejected.");
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "IPC malformed JSON rejected.");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "IPC connection failed.");
            }
        }
    }

    private async Task ProcessConnectionAsync(
        PipeStream pipe,
        CancellationToken cancellationToken)
    {
        var requestJson = await LocalIpcFraming.ReadJsonAsync(pipe, cancellationToken);
        var clientContext = _clientIdentityProvider.GetClientContext(pipe);
        var responseJson = await _requestHandler.HandleAsync(requestJson, clientContext, cancellationToken);
        await LocalIpcFraming.WriteJsonAsync(pipe, responseJson, cancellationToken);
    }
}
