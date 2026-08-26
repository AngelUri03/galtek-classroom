using System.Text.Json;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Service.Runtime;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Ipc;

public sealed class LocalIpcRequestHandler : ILocalIpcRequestHandler
{
    private static readonly JsonSerializerOptions JsonOptions = LocalIpcJson.CreateOptions();

    private readonly AgentRuntimeState _runtimeState;
    private readonly CommercialLicenseManager _licenseManager;
    private readonly MachineCodeGenerator _machineCodeGenerator;
    private readonly IHostNameProvider _hostNameProvider;
    private readonly ILogger<LocalIpcRequestHandler> _logger;

    public LocalIpcRequestHandler(
        AgentRuntimeState runtimeState,
        CommercialLicenseManager licenseManager,
        MachineCodeGenerator machineCodeGenerator,
        IHostNameProvider hostNameProvider,
        ILogger<LocalIpcRequestHandler> logger)
    {
        _runtimeState = runtimeState;
        _licenseManager = licenseManager;
        _machineCodeGenerator = machineCodeGenerator;
        _hostNameProvider = hostNameProvider;
        _logger = logger;
    }

    public Task<string> HandleAsync(string requestJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LocalIpcResponse response;

        try
        {
            var request = JsonSerializer.Deserialize<LocalIpcRequest>(requestJson, JsonOptions);
            response = request is null
                ? LocalIpcResponse.Error(string.Empty, LocalIpcErrorCodes.MalformedRequest)
                : HandleRequest(request);
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

        return Task.FromResult(JsonSerializer.Serialize(response, JsonOptions));
    }

    private LocalIpcResponse HandleRequest(LocalIpcRequest request)
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
            LocalIpcOperations.Ping => LocalIpcResponse.Ok(requestId, new LocalIpcPingPayload()),
            LocalIpcOperations.GetDeviceStatus => LocalIpcResponse.Ok(requestId, GetDeviceStatus()),
            LocalIpcOperations.GetMachineCode => LocalIpcResponse.Ok(requestId, GetMachineCode()),
            _ => LocalIpcResponse.Error(requestId, LocalIpcErrorCodes.OperationNotSupported)
        };

        if (response.Success)
        {
            _logger.LogInformation("IPC request {Operation} completed.", request.Operation);
        }

        return response;
    }

    private LocalDeviceStatus GetDeviceStatus()
    {
        var identity = _runtimeState.GetInstallationIdentity();
        var licenseState = _licenseManager.CurrentState;

        return LocalDeviceStatus.From(
            identity,
            _hostNameProvider.GetHostName(),
            licenseState);
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
