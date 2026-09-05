using System.Buffers.Binary;
using System.Security.Cryptography;
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
    public const int SecretResponseVersion = 1;
    public const int MaxSecretResponsePasswordBytes = ManagedWindowsCredentialConstants.MaximumPasswordCharacters * 2;
}

public static class CredentialProviderBridgeOperations
{
    public const string Ping = "PING";
    public const string GetPendingActivationMetadata = "GET_PENDING_ACTIVATION_METADATA";
    public const string GetPendingActivationIdentity = "GET_PENDING_ACTIVATION_IDENTITY";
    public const string AcquirePendingCredential = "ACQUIRE_PENDING_CREDENTIAL";
    public const string WaitForActivationChange = "WAIT_FOR_ACTIVATION_CHANGE";
    public const string ReportLogonResult = "REPORT_LOGON_RESULT";
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

public static class CredentialProviderSecretResponseStatuses
{
    public const ushort Success = 0;
    public const ushort Failed = 1;
}

public static class CredentialProviderBridgeErrorCodes
{
    public const string ProtocolUnsupported = "CREDENTIAL_PROVIDER_PROTOCOL_UNSUPPORTED";
    public const string OperationNotSupported = "CREDENTIAL_PROVIDER_OPERATION_NOT_SUPPORTED";
    public const string MalformedRequest = "CREDENTIAL_PROVIDER_MALFORMED_REQUEST";
    public const string Unauthorized = "CREDENTIAL_PROVIDER_CHANNEL_UNAUTHORIZED";
    public const string InternalError = "CREDENTIAL_PROVIDER_INTERNAL_ERROR";
}

public static class CredentialProviderLogonResultOutcomes
{
    public const string Success = "SUCCESS";
    public const string Failed = "FAILED";
    public const string LocalSerializationFailed = "LOCAL_SERIALIZATION_FAILED";
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

public sealed record CredentialProviderActivationIdentity
{
    [JsonPropertyName("activationId")]
    [JsonPropertyOrder(0)]
    public string ActivationId { get; init; } = string.Empty;

    [JsonPropertyName("accountId")]
    [JsonPropertyOrder(1)]
    public string AccountId { get; init; } = string.Empty;

    [JsonPropertyName("userSid")]
    [JsonPropertyOrder(2)]
    public string UserSid { get; init; } = string.Empty;

    [JsonPropertyName("domain")]
    [JsonPropertyOrder(3)]
    public string Domain { get; init; } = string.Empty;

    [JsonPropertyName("username")]
    [JsonPropertyOrder(4)]
    public string Username { get; init; } = string.Empty;

    [JsonPropertyName("autoSubmitRequested")]
    [JsonPropertyOrder(5)]
    public bool AutoSubmitRequested { get; init; }
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

    [JsonPropertyName("activationId")]
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActivationId { get; init; }

    [JsonPropertyName("observedGeneration")]
    [JsonPropertyOrder(4)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ObservedGeneration { get; init; }

    [JsonPropertyName("outcome")]
    [JsonPropertyOrder(5)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Outcome { get; init; }
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

    [JsonPropertyName("pendingIdentity")]
    [JsonPropertyOrder(5)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CredentialProviderActivationIdentity? PendingIdentity { get; init; }

    [JsonPropertyName("errorCode")]
    [JsonPropertyOrder(6)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("message")]
    [JsonPropertyOrder(7)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    [JsonPropertyName("generation")]
    [JsonPropertyOrder(8)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Generation { get; init; }

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

    public static CredentialProviderBridgeResponse Identity(
        string requestId,
        CredentialProviderActivationIdentity? identity)
    {
        return new CredentialProviderBridgeResponse
        {
            RequestId = requestId,
            Status = CredentialProviderBridgeStatuses.Success,
            ActivationStatus = identity is null
                ? CredentialProviderActivationStatuses.None
                : CredentialProviderActivationStatuses.Pending,
            PendingIdentity = identity
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

    public static CredentialProviderBridgeResponse ActivationGeneration(
        string requestId,
        long generation)
    {
        return new CredentialProviderBridgeResponse
        {
            RequestId = requestId,
            Status = CredentialProviderBridgeStatuses.Success,
            Generation = generation
        };
    }
}

public sealed record CredentialProviderSecretResponsePayload(
    ushort Version,
    ushort Status,
    string ActivationId,
    byte[] PasswordUtf16LittleEndian) : IDisposable
{
    public bool Succeeded => Status == CredentialProviderSecretResponseStatuses.Success;

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(PasswordUtf16LittleEndian);
    }
}

public static class CredentialProviderSecretResponse
{
    private static readonly byte[] Magic = "GCPAS"u8.ToArray();

    public static byte[] Success(
        string activationId,
        ReadOnlySpan<byte> passwordUtf16LittleEndian)
    {
        ValidateActivationId(activationId);
        ValidatePassword(passwordUtf16LittleEndian);

        return Build(
            CredentialProviderSecretResponseStatuses.Success,
            activationId,
            passwordUtf16LittleEndian);
    }

    public static byte[] Failure(string activationId)
    {
        if (!Guid.TryParse(activationId, out _))
        {
            activationId = Guid.Empty.ToString("D");
        }

        return Build(
            CredentialProviderSecretResponseStatuses.Failed,
            activationId,
            ReadOnlySpan<byte>.Empty);
    }

    public static bool TryParse(
        ReadOnlySpan<byte> payload,
        out CredentialProviderSecretResponsePayload? parsed)
    {
        parsed = null;
        var offset = 0;

        if (payload.Length < Magic.Length + sizeof(ushort) + sizeof(ushort) + sizeof(ushort) + sizeof(int))
        {
            return false;
        }

        if (!payload.Slice(offset, Magic.Length).SequenceEqual(Magic))
        {
            return false;
        }

        offset += Magic.Length;
        var version = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(offset, sizeof(ushort)));
        offset += sizeof(ushort);
        if (version != CredentialProviderBridgeProtocol.SecretResponseVersion)
        {
            return false;
        }

        var status = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(offset, sizeof(ushort)));
        offset += sizeof(ushort);
        if (status is not CredentialProviderSecretResponseStatuses.Success
            and not CredentialProviderSecretResponseStatuses.Failed)
        {
            return false;
        }

        var activationIdLength = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(offset, sizeof(ushort)));
        offset += sizeof(ushort);
        if (activationIdLength == 0 || payload.Length - offset < activationIdLength)
        {
            return false;
        }

        string activationId;
        try
        {
            activationId = Encoding.UTF8.GetString(payload.Slice(offset, activationIdLength));
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!Guid.TryParse(activationId, out _))
        {
            return false;
        }

        offset += activationIdLength;
        if (payload.Length - offset < sizeof(int))
        {
            return false;
        }

        var passwordLength = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(offset, sizeof(int)));
        offset += sizeof(int);
        if (passwordLength < 0
            || passwordLength % 2 != 0
            || passwordLength > CredentialProviderBridgeProtocol.MaxSecretResponsePasswordBytes
            || payload.Length - offset != passwordLength)
        {
            return false;
        }

        if (status == CredentialProviderSecretResponseStatuses.Success && passwordLength == 0)
        {
            return false;
        }

        if (status == CredentialProviderSecretResponseStatuses.Failed && passwordLength != 0)
        {
            return false;
        }

        parsed = new CredentialProviderSecretResponsePayload(
            version,
            status,
            activationId,
            payload.Slice(offset, passwordLength).ToArray());
        return true;
    }

    private static byte[] Build(
        ushort status,
        string activationId,
        ReadOnlySpan<byte> passwordUtf16LittleEndian)
    {
        var activationIdBytes = Encoding.UTF8.GetBytes(activationId);
        var payloadLength = Magic.Length
            + sizeof(ushort)
            + sizeof(ushort)
            + sizeof(ushort)
            + activationIdBytes.Length
            + sizeof(int)
            + passwordUtf16LittleEndian.Length;

        if (payloadLength > CredentialProviderBridgeProtocol.MaxMessageBytes)
        {
            throw new CredentialProviderBridgeFramingException(
                "Credential Provider secret response length exceeds the configured limit.");
        }

        var payload = new byte[payloadLength];
        var offset = 0;
        Magic.CopyTo(payload, offset);
        offset += Magic.Length;

        BinaryPrimitives.WriteUInt16BigEndian(
            payload.AsSpan(offset, sizeof(ushort)),
            CredentialProviderBridgeProtocol.SecretResponseVersion);
        offset += sizeof(ushort);

        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset, sizeof(ushort)), status);
        offset += sizeof(ushort);

        BinaryPrimitives.WriteUInt16BigEndian(
            payload.AsSpan(offset, sizeof(ushort)),
            checked((ushort)activationIdBytes.Length));
        offset += sizeof(ushort);
        activationIdBytes.CopyTo(payload.AsSpan(offset));
        offset += activationIdBytes.Length;

        BinaryPrimitives.WriteInt32BigEndian(
            payload.AsSpan(offset, sizeof(int)),
            passwordUtf16LittleEndian.Length);
        offset += sizeof(int);
        passwordUtf16LittleEndian.CopyTo(payload.AsSpan(offset));

        return payload;
    }

    private static void ValidateActivationId(string activationId)
    {
        if (!Guid.TryParse(activationId, out _))
        {
            throw new ArgumentException("Credential Provider activationId is invalid.", nameof(activationId));
        }
    }

    private static void ValidatePassword(ReadOnlySpan<byte> passwordUtf16LittleEndian)
    {
        if (passwordUtf16LittleEndian.Length == 0)
        {
            throw new ArgumentException("Credential Provider password payload is empty.", nameof(passwordUtf16LittleEndian));
        }

        if (passwordUtf16LittleEndian.Length % 2 != 0)
        {
            throw new ArgumentException("Credential Provider password payload is not valid UTF-16LE.", nameof(passwordUtf16LittleEndian));
        }

        if (passwordUtf16LittleEndian.Length > CredentialProviderBridgeProtocol.MaxSecretResponsePasswordBytes)
        {
            throw new ArgumentException("Credential Provider password payload exceeds the configured limit.", nameof(passwordUtf16LittleEndian));
        }
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

    public static async Task WritePayloadAsync(
        Stream stream,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(payload);
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
