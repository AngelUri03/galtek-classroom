using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Serialization;

namespace GaltekClassroom.Agent.Shared;

public static class SessionCommandProtocol
{
    public const int ProtocolVersion = 1;
    public const string PipeNamePrefix = "GaltekClassroom.Agent.SessionCommand.v1.";
    public const int MaxMessageBytes = 16 * 1024;
    public const string LocalSystemSid = "S-1-5-18";

    public static string PipeNameForSession(int sessionId)
    {
        if (sessionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId), "Interactive session id must be greater than zero.");
        }

        return PipeNamePrefix + sessionId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

public static class SessionCommandTypes
{
    public const string ChannelPing = "CHANNEL_PING";
    public const string OpenUrl = "OPEN_URL";
    public const string OpenApplication = "OPEN_APPLICATION";
}

public static class SessionCommandStatuses
{
    public const string Success = "SUCCESS";
    public const string Failed = "FAILED";
}

public static class SessionCommandErrorCodes
{
    public const string SessionAgentUnavailable = "SESSION_AGENT_UNAVAILABLE";
    public const string SessionChannelTimeout = "SESSION_CHANNEL_TIMEOUT";
    public const string SessionChannelUnauthorized = "SESSION_CHANNEL_UNAUTHORIZED";
    public const string SessionChannelProtocolMismatch = "SESSION_CHANNEL_PROTOCOL_MISMATCH";
    public const string SessionChannelInvalidResponse = "SESSION_CHANNEL_INVALID_RESPONSE";
    public const string SessionCommandNotSupported = "SESSION_COMMAND_NOT_SUPPORTED";
    public const string SessionChannelMalformedRequest = "SESSION_CHANNEL_MALFORMED_REQUEST";
    public const string SessionCommandResultUnknown = "SESSION_COMMAND_RESULT_UNKNOWN";
    public const string InvalidUrl = "INVALID_URL";
    public const string UrlLaunchFailed = "URL_LAUNCH_FAILED";
    public const string ApplicationBindingsInvalid = "APPLICATION_BINDINGS_INVALID";
    public const string ApplicationBindingNotFound = "APPLICATION_BINDING_NOT_FOUND";
    public const string ApplicationBindingInvalid = "APPLICATION_BINDING_INVALID";
    public const string ApplicationDisabled = "APPLICATION_DISABLED";
    public const string ApplicationExecutableNotFound = "APPLICATION_EXECUTABLE_NOT_FOUND";
    public const string ApplicationLaunchFailed = "APPLICATION_LAUNCH_FAILED";
}

public sealed record SessionCommandRequest
{
    [JsonPropertyName("protocolVersion")]
    [JsonPropertyOrder(0)]
    public int ProtocolVersion { get; init; } = SessionCommandProtocol.ProtocolVersion;

    [JsonPropertyName("requestId")]
    [JsonPropertyOrder(1)]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("commandType")]
    [JsonPropertyOrder(2)]
    public string CommandType { get; init; } = string.Empty;

    [JsonPropertyName("openUrl")]
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionOpenUrlCommand? OpenUrl { get; init; }

    [JsonPropertyName("openApplication")]
    [JsonPropertyOrder(4)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionOpenApplicationCommand? OpenApplication { get; init; }
}

public sealed record SessionOpenUrlCommand
{
    [JsonPropertyName("operationId")]
    [JsonPropertyOrder(0)]
    public string OperationId { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    [JsonPropertyOrder(1)]
    public string Url { get; init; } = string.Empty;
}

public sealed record SessionOpenApplicationCommand
{
    [JsonPropertyName("applicationId")]
    [JsonPropertyOrder(0)]
    public string ApplicationId { get; init; } = string.Empty;
}

public sealed record SessionCommandResponse
{
    [JsonPropertyName("protocolVersion")]
    [JsonPropertyOrder(0)]
    public int ProtocolVersion { get; init; } = SessionCommandProtocol.ProtocolVersion;

    [JsonPropertyName("requestId")]
    [JsonPropertyOrder(1)]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    [JsonPropertyOrder(2)]
    public string Status { get; init; } = SessionCommandStatuses.Failed;

    [JsonPropertyName("errorCode")]
    [JsonPropertyOrder(3)]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("message")]
    [JsonPropertyOrder(4)]
    public string? Message { get; init; }

    public static SessionCommandResponse Success(string requestId)
    {
        return new SessionCommandResponse
        {
            RequestId = requestId,
            Status = SessionCommandStatuses.Success,
            ErrorCode = null,
            Message = null
        };
    }

    public static SessionCommandResponse Error(string requestId, string errorCode, string? message = null)
    {
        return new SessionCommandResponse
        {
            RequestId = requestId,
            Status = SessionCommandStatuses.Failed,
            ErrorCode = errorCode,
            Message = message
        };
    }
}

public sealed class SessionCommandFramingException : Exception
{
    public SessionCommandFramingException(string message)
        : base(message)
    {
    }
}

public static class SessionCommandFraming
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
            throw new SessionCommandFramingException("Session command message length must be positive.");
        }

        if (length > SessionCommandProtocol.MaxMessageBytes)
        {
            throw new SessionCommandFramingException("Session command message length exceeds the configured limit.");
        }
    }
}
