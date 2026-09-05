using System.Text.Json;
using System.Security.Cryptography;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.CredentialProviderBridge;

public sealed record CredentialProviderBridgeHandlerResult(
    CredentialProviderBridgeResponse? JsonResponse,
    byte[]? BinaryPayload) : IDisposable
{
    public static CredentialProviderBridgeHandlerResult Json(CredentialProviderBridgeResponse response)
    {
        return new CredentialProviderBridgeHandlerResult(response, null);
    }

    public static CredentialProviderBridgeHandlerResult Binary(byte[] payload)
    {
        return new CredentialProviderBridgeHandlerResult(null, payload);
    }

    public void Dispose()
    {
        if (BinaryPayload is not null)
        {
            CryptographicOperations.ZeroMemory(BinaryPayload);
        }
    }
}

public sealed class CredentialProviderBridgeRequestHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedRequestFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "protocolVersion",
        "requestId",
        "operation",
        "activationId"
    };

    private readonly CredentialProviderActivationService _activationService;

    public CredentialProviderBridgeRequestHandler(CredentialProviderActivationService activationService)
    {
        _activationService = activationService;
    }

    public async Task<CredentialProviderBridgeHandlerResult> HandleFrameAsync(
        string requestJson,
        CredentialProviderCallerValidation caller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestJson);
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();

        if (!caller.Authorized)
        {
            return CredentialProviderBridgeHandlerResult.Json(CredentialProviderBridgeResponse.Error(
                string.Empty,
                CredentialProviderBridgeErrorCodes.Unauthorized));
        }

        using var document = JsonDocument.Parse(requestJson);
        if (!ValidateStrictRequestShape(document.RootElement))
        {
            return CredentialProviderBridgeHandlerResult.Json(CredentialProviderBridgeResponse.Error(
                string.Empty,
                CredentialProviderBridgeErrorCodes.MalformedRequest));
        }

        var request = document.RootElement.Deserialize<CredentialProviderBridgeRequest>(JsonOptions);
        if (request is null || !Guid.TryParse(request.RequestId, out _))
        {
            return CredentialProviderBridgeHandlerResult.Json(CredentialProviderBridgeResponse.Error(
                request?.RequestId ?? string.Empty,
                CredentialProviderBridgeErrorCodes.MalformedRequest));
        }

        if (request.ProtocolVersion != CredentialProviderBridgeProtocol.ProtocolVersion)
        {
            return CredentialProviderBridgeHandlerResult.Json(CredentialProviderBridgeResponse.Error(
                request.RequestId,
                CredentialProviderBridgeErrorCodes.ProtocolUnsupported));
        }

        return await HandleAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CredentialProviderBridgeResponse> HandleAsync(
        string requestJson,
        CredentialProviderCallerValidation caller,
        CancellationToken cancellationToken)
    {
        using var result = await HandleFrameAsync(requestJson, caller, cancellationToken).ConfigureAwait(false);
        return result.JsonResponse
            ?? CredentialProviderBridgeResponse.Error(
                string.Empty,
                CredentialProviderBridgeErrorCodes.OperationNotSupported);
    }

    private async Task<CredentialProviderBridgeHandlerResult> HandleAsync(
        CredentialProviderBridgeRequest request,
        CancellationToken cancellationToken)
    {
        return request.Operation switch
        {
            CredentialProviderBridgeOperations.Ping =>
                CredentialProviderBridgeHandlerResult.Json(CredentialProviderBridgeResponse.Success(request.RequestId)),
            CredentialProviderBridgeOperations.GetPendingActivationMetadata =>
                CredentialProviderBridgeHandlerResult.Json(CredentialProviderBridgeResponse.Activation(
                    request.RequestId,
                    _activationService.GetPendingMetadata())),
            CredentialProviderBridgeOperations.GetPendingActivationIdentity =>
                CredentialProviderBridgeHandlerResult.Json(CredentialProviderBridgeResponse.Identity(
                    request.RequestId,
                    await _activationService.GetPendingIdentityAsync(cancellationToken).ConfigureAwait(false))),
            CredentialProviderBridgeOperations.AcquirePendingCredential =>
                CredentialProviderBridgeHandlerResult.Binary(
                    await BuildAcquireResponseAsync(request.ActivationId, cancellationToken).ConfigureAwait(false)),
            _ => CredentialProviderBridgeHandlerResult.Json(CredentialProviderBridgeResponse.Error(
                request.RequestId,
                CredentialProviderBridgeErrorCodes.OperationNotSupported))
        };
    }

    private async Task<byte[]> BuildAcquireResponseAsync(
        string? activationId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(activationId, out _))
        {
            return CredentialProviderSecretResponse.Failure(Guid.Empty.ToString("D"));
        }

        return await _activationService.AcquirePendingCredentialAsync(activationId, cancellationToken)
            .ConfigureAwait(false)
            ?? CredentialProviderSecretResponse.Failure(activationId);
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
