using System.Buffers.Binary;
using System.Text;

namespace GaltekClassroom.Agent.Shared;

public sealed class LocalIpcFramingException : Exception
{
    public LocalIpcFramingException(string message)
        : base(message)
    {
    }
}

public static class LocalIpcFraming
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

        var frame = FrameJson(json);
        await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<string> ReadJsonAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var lengthBuffer = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(lengthBuffer, cancellationToken);

        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
        ValidatePayloadLength(length);

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);

        return Encoding.UTF8.GetString(payload);
    }

    private static void ValidatePayloadLength(int length)
    {
        if (length <= 0)
        {
            throw new LocalIpcFramingException("IPC message length must be positive.");
        }

        if (length > LocalIpcProtocol.MaxMessageBytes)
        {
            throw new LocalIpcFramingException("IPC message length exceeds the configured limit.");
        }
    }
}
