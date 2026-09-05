using System.Text.Json;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.CredentialProviderBridge;

public sealed class CredentialProviderBridgeRequestHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedRequestFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "protocolVersion",
        "requestId",
        "operation"
    };

    private readonly CredentialProviderActivationService _activationService;

    public CredentialProviderBridgeRequestHandler(CredentialProviderActivationService activationService)
    {
        _activationService = activationService;
    }

    public Task<CredentialProviderBridgeResponse> HandleAsync(
        string requestJson,
        CredentialProviderCallerValidation caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestJson);
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();

        if (!caller.Authorized)
        {
            return Task.FromResult(CredentialProviderBridgeResponse.Error(
                string.Empty,
                CredentialProviderBridgeErrorCodes.Unauthorized));
        }

        using var document = JsonDocument.Parse(requestJson);
        if (!ValidateStrictRequestShape(document.RootElement))
        {
            return Task.FromResult(CredentialProviderBridgeResponse.Error(
                string.Empty,
                CredentialProviderBridgeErrorCodes.MalformedRequest));
        }

        var request = document.RootElement.Deserialize<CredentialProviderBridgeRequest>(JsonOptions);
        if (request is null || !Guid.TryParse(request.RequestId, out _))
        {
            return Task.FromResult(CredentialProviderBridgeResponse.Error(
                request?.RequestId ?? string.Empty,
                CredentialProviderBridgeErrorCodes.MalformedRequest));
        }

        if (request.ProtocolVersion != CredentialProviderBridgeProtocol.ProtocolVersion)
        {
            return Task.FromResult(CredentialProviderBridgeResponse.Error(
                request.RequestId,
                CredentialProviderBridgeErrorCodes.ProtocolUnsupported));
        }

        return Task.FromResult(Handle(request));
    }

    private CredentialProviderBridgeResponse Handle(CredentialProviderBridgeRequest request)
    {
        return request.Operation switch
        {
            CredentialProviderBridgeOperations.Ping =>
                CredentialProviderBridgeResponse.Success(request.RequestId),
            CredentialProviderBridgeOperations.GetPendingActivationMetadata =>
                CredentialProviderBridgeResponse.Activation(
                    request.RequestId,
                    _activationService.GetPendingMetadata()),
            _ => CredentialProviderBridgeResponse.Error(
                request.RequestId,
                CredentialProviderBridgeErrorCodes.OperationNotSupported)
        };
    }

    private static bool ValidateStrictRequestShape(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!AllowedRequestFields.Contains(property.Name) || IsForbiddenFieldName(property.Name))
            {
                return false;
            }
        }

        return element.TryGetProperty("protocolVersion", out _)
            && element.TryGetProperty("requestId", out _)
            && element.TryGetProperty("operation", out _);
    }

    private static bool IsForbiddenFieldName(string fieldName)
    {
        return fieldName.Contains("password", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("token", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("pin", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("pid", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("process", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("sid", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("user", StringComparison.OrdinalIgnoreCase);
    }
}
