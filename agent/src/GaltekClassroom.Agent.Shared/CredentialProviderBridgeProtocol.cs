using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Serialization;

namespace GaltekClassroom.Agent.Shared;

public static class CredentialProviderBridgeProtocol
{
    public const int ProtocolVersion = 1;
    public const string PipeName = "GaltekClassroom.CredentialProvider.v1";
    public const int MaxMessageBytes = 8 * 1024;
    public const int MaxConcurrentConnections = 2;
    public const string LocalSystemSid = "S-1-5-18";
}

public static class CredentialProviderBridgeOperations
{
    public const string Ping = "PING";
    public const string GetPendingActivationMetadata = "GET_PENDING_ACTIVATION_METADATA";
}

public static class CredentialProviderBridgeStatuses
{
    public const string Success = "SUCCESS";
    public const string Failed = "FAILED";
}

public static class CredentialProviderActivationStatuses
{
    public const string None = "NONE";
    public const string Pending = "PENDING";
}

public static class CredentialProviderBridgeErrorCodes
{
    public const string ProtocolUnsupported = "CREDENTIAL_PROVIDER_PROTOCOL_UNSUPPORTED";
    public const string OperationNotSupported = "CREDENTIAL_PROVIDER_OPERATION_NOT_SUPPORTED";
    public const string MalformedRequest = "CREDENTIAL_PROVIDER_MALFORMED_REQUEST";
    public const string Unauthorized = "CREDENTIAL_PROVIDER_CHANNEL_UNAUTHORIZED";
    public const string InternalError = "CREDENTIAL_PROVIDER_INTERNAL_ERROR";
}

public sealed record CredentialProviderActivationMetadata
{
    [JsonPropertyName("activationId")]
    [JsonPropertyOrder(0)]
    public string ActivationId { get; init; } = string.Empty;

    [JsonPropertyName("accountId")]
    [JsonPropertyOrder(1)]
    public string AccountId { get; init; } = string.Empty;

    [JsonPropertyName("createdAtUtc")]
    [JsonPropertyOrder(2)]
    public DateTimeOffset CreatedAtUtc { get; init; }

    [JsonPropertyName("expiresAtUtc")]
    [JsonPropertyOrder(3)]
    public DateTimeOffset ExpiresAtUtc { get; init; }
}

public sealed record CredentialProviderBridgeRequest
{
    [JsonPropertyName("protocolVersion")]
    [JsonPropertyOrder(0)]
    public int ProtocolVersion { get; init; } = CredentialProviderBridgeProtocol.ProtocolVersion;

    [JsonPropertyName("requestId")]
    [JsonPropertyOrder(1)]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("operation")]
    [JsonPropertyOrder(2)]
    public string Operation { get; init; } = string.Empty;
}

public sealed record CredentialProviderBridgeResponse
{
    [JsonPropertyName("protocolVersion")]
    [JsonPropertyOrder(0)]
    public int ProtocolVersion { get; init; } = CredentialProviderBridgeProtocol.ProtocolVersion;

    [JsonPropertyName("requestId")]
    [JsonPropertyOrder(1)]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    [JsonPropertyOrder(2)]
    public string Status { get; init; } = CredentialProviderBridgeStatuses.Failed;

    [JsonPropertyName("activationStatus")]
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActivationStatus { get; init; }

    [JsonPropertyName("pendingActivation")]
    [JsonPropertyOrder(4)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CredentialProviderActivationMetadata? PendingActivation { get; init; }

    [JsonPropertyName("errorCode")]
    [JsonPropertyOrder(5)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("message")]
    [JsonPropertyOrder(6)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    public static CredentialProviderBridgeResponse Success(string requestId)
    {
        return new CredentialProviderBridgeResponse
        {
            RequestId = requestId,
            Status = CredentialProviderBridgeStatuses.Success
        };
    }

    public static CredentialProviderBridgeResponse Activation(
        string requestId,
        CredentialProviderActivationMetadata? activation)
    {
        return new CredentialProviderBridgeResponse
        {
            RequestId = requestId,
            Status = CredentialProviderBridgeStatuses.Success,
            ActivationStatus = activation is null
                ? CredentialProviderActivationStatuses.None
                : CredentialProviderActivationStatuses.Pending,
            PendingActivation = activation
        };
    }

    public static CredentialProviderBridgeResponse Error(
        string requestId,
        string errorCode,
        string? message = null)
    {
        return new CredentialProviderBridgeResponse
        {
            RequestId = requestId,
            Status = CredentialProviderBridgeStatuses.Failed,
            ErrorCode = errorCode,
            Message = message
        };
    }
}

public sealed class CredentialProviderBridgeFramingException : Exception
{
    public CredentialProviderBridgeFramingException(string message)
        : base(message)
    {
    }
}

public static class CredentialProviderBridgeFraming
{
    public static byte[] FrameJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return FramePayload(Encoding.UTF8.GetBytes(json));
    }

    public static byte[] FramePayload(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidatePayloadLength(payload.Length);

        var frame = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, sizeof(int)), payload.Length);
        payload.CopyTo(frame.AsSpan(sizeof(int)));

        return frame;
    }

    public static async Task WriteJsonAsync(
        Stream stream,
        string json,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(json);

        var payload = Encoding.UTF8.GetBytes(json);
        ValidatePayloadLength(payload.Length);

        var lengthBuffer = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, payload.Length);

        await stream.WriteAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string> ReadJsonAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var lengthBuffer = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);

        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
        ValidatePayloadLength(length);

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);

        return Encoding.UTF8.GetString(payload);
    }

    private static void ValidatePayloadLength(int length)
    {
        if (length <= 0)
        {
            throw new CredentialProviderBridgeFramingException(
                "Credential Provider bridge message length must be positive.");
        }

        if (length > CredentialProviderBridgeProtocol.MaxMessageBytes)
        {
            throw new CredentialProviderBridgeFramingException(
                "Credential Provider bridge message length exceeds the configured limit.");
        }
    }
}
