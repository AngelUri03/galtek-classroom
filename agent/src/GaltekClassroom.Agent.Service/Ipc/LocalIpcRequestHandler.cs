using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Service.Master;
using GaltekClassroom.Agent.Service.Runtime;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Ipc;

public sealed class LocalIpcRequestHandler : ILocalIpcRequestHandler
{
    private static readonly JsonSerializerOptions JsonOptions = LocalIpcJson.CreateOptions();
    private static readonly LocalIpcPingPayload PingPayload = new();

    private readonly AgentRuntimeState _runtimeState;
    private readonly CommercialLicenseManager _licenseManager;
    private readonly MachineCodeGenerator _machineCodeGenerator;
    private readonly IHostNameProvider _hostNameProvider;
    private readonly MasterAuthorizationService _masterAuthorizationService;
    private readonly ILogger<LocalIpcRequestHandler> _logger;

    public LocalIpcRequestHandler(
        AgentRuntimeState runtimeState,
        CommercialLicenseManager licenseManager,
        MachineCodeGenerator machineCodeGenerator,
        IHostNameProvider hostNameProvider,
        MasterAuthorizationService masterAuthorizationService,
        ILogger<LocalIpcRequestHandler> logger)
    {
        _runtimeState = runtimeState;
        _licenseManager = licenseManager;
        _machineCodeGenerator = machineCodeGenerator;
        _hostNameProvider = hostNameProvider;
        _masterAuthorizationService = masterAuthorizationService;
        _logger = logger;
    }

    public Task<string> HandleAsync(string requestJson, CancellationToken cancellationToken)
    {
        return HandleAsync(requestJson, LocalIpcClientContext.Unavailable(), cancellationToken);
    }

    public async Task<string> HandleAsync(
        string requestJson,
        LocalIpcClientContext clientContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LocalIpcResponse response;

        try
        {
            var request = JsonSerializer.Deserialize<LocalIpcRequest>(requestJson, JsonOptions);
            response = request is null
                ? LocalIpcResponse.Error(string.Empty, LocalIpcErrorCodes.MalformedRequest)
                : await HandleRequestAsync(request, clientContext, cancellationToken);
        }
        catch (JsonException)
        {
            _logger.LogWarning("IPC malformed request rejected.");
            response = LocalIpcResponse.Error(string.Empty, LocalIpcErrorCodes.MalformedRequest);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "IPC request failed.");
            response = LocalIpcResponse.Error(string.Empty, LocalIpcErrorCodes.InternalError);
        }

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private async Task<LocalIpcResponse> HandleRequestAsync(
        LocalIpcRequest request,
        LocalIpcClientContext clientContext,
        CancellationToken cancellationToken)
    {
        var requestId = request.RequestId ?? string.Empty;

        if (request.ProtocolVersion != LocalIpcProtocol.ProtocolVersion)
        {
            return LocalIpcResponse.Error(requestId, LocalIpcErrorCodes.ProtocolUnsupported);
        }

        if (!Guid.TryParse(requestId, out _) || string.IsNullOrWhiteSpace(request.Operation))
        {
            return LocalIpcResponse.Error(requestId, LocalIpcErrorCodes.InvalidRequest);
        }

        var response = request.Operation switch
        {
            LocalIpcOperations.Ping => LocalIpcResponse.Ok(requestId, PingPayload),
            LocalIpcOperations.GetDeviceStatus => LocalIpcResponse.Ok(requestId, GetDeviceStatus()),
            LocalIpcOperations.GetMachineCode => LocalIpcResponse.Ok(requestId, GetMachineCode()),
            LocalIpcOperations.GetMasterAuthorization => LocalIpcResponse.Ok(
                requestId,
                await _masterAuthorizationService.GetAuthorizationAsync(clientContext, cancellationToken)),
            _ => LocalIpcResponse.Error(requestId, LocalIpcErrorCodes.OperationNotSupported)
        };

        if (response.Success)
        {
            _logger.LogDebug("IPC request {Operation} completed.", request.Operation);
        }

        return response;
    }

    private LocalDeviceStatus GetDeviceStatus()
    {
        var identity = _runtimeState.GetInstallationIdentity();
        var licenseState = _licenseManager.CurrentState;
        var runtime = _runtimeState.Snapshot;

        return LocalDeviceStatus.From(
            identity,
            _hostNameProvider.GetHostName(),
            licenseState,
            runtime.StartupPhase.ToCode(),
            runtime.PreviousShutdownWasUnclean,
            runtime.RecoveryActive);
    }

    private LocalIpcMachineCodePayload GetMachineCode()
    {
        var identity = _runtimeState.GetInstallationIdentity();

        return new LocalIpcMachineCodePayload
        {
            MachineCode = _machineCodeGenerator.Generate(identity)
        };
    }
}
