using System.Text;
using System.Buffers.Binary;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class LocalIpcFramingTests
{
    [Fact]
    public void FrameJson_UsesBigEndianLengthPrefix()
    {
        var frame = LocalIpcFraming.FrameJson("{}");

        Assert.Equal(6, frame.Length);
        Assert.Equal([0, 0, 0, 2], frame.Take(4).ToArray());
        Assert.Equal("{}", Encoding.UTF8.GetString(frame.Skip(4).ToArray()));
    }

    [Fact]
    public async Task ReadJsonAsync_ReadsExactPayload()
    {
        await using var stream = new MemoryStream();

        await LocalIpcFraming.WriteJsonAsync(stream, """{"operation":"PING"}""", CancellationToken.None);
        stream.Position = 0;

        var json = await LocalIpcFraming.ReadJsonAsync(stream, CancellationToken.None);

        Assert.Equal("""{"operation":"PING"}""", json);
    }

    [Fact]
    public void FrameJson_WhenMessageExceedsLimit_RejectsMessage()
    {
        var oversizedJson = new string('a', LocalIpcProtocol.MaxMessageBytes + 1);

        Assert.Throws<LocalIpcFramingException>(() => LocalIpcFraming.FrameJson(oversizedJson));
    }

    [Fact]
    public void FrameJson_WhenMessageIsAtLimit_AcceptsMessage()
    {
        var maxSizeJson = new string('a', LocalIpcProtocol.MaxMessageBytes);

        var frame = LocalIpcFraming.FrameJson(maxSizeJson);

        Assert.Equal(sizeof(int) + LocalIpcProtocol.MaxMessageBytes, frame.Length);
        Assert.Equal(0, frame[0]);
        Assert.Equal(1, frame[1]);
        Assert.Equal(0, frame[2]);
        Assert.Equal(0, frame[3]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReadJsonAsync_WhenLengthIsInvalid_RejectsFrame(int length)
    {
        var frame = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(frame, length);
        await using var stream = new MemoryStream(frame);

        await Assert.ThrowsAsync<LocalIpcFramingException>(
            () => LocalIpcFraming.ReadJsonAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadJsonAsync_WhenLengthExceedsLimit_RejectsFrame()
    {
        var frame = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(frame, LocalIpcProtocol.MaxMessageBytes + 1);
        await using var stream = new MemoryStream(frame);

        await Assert.ThrowsAsync<LocalIpcFramingException>(
            () => LocalIpcFraming.ReadJsonAsync(stream, CancellationToken.None));
    }
}
