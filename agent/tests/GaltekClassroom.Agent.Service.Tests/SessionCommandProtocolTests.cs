using System.Text.Json;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class SessionCommandProtocolTests
{
    [Fact]
    public void ProtocolVersion_IsV1()
    {
        Assert.Equal(1, SessionCommandProtocol.ProtocolVersion);
    }

    [Fact]
    public void PipeNameForSession_IncludesExpectedSessionId()
    {
        Assert.Equal(
            "GaltekClassroom.Agent.SessionCommand.v1.3",
            SessionCommandProtocol.PipeNameForSession(3));
    }

    [Fact]
    public void PipeNameForSession_RejectsSessionZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SessionCommandProtocol.PipeNameForSession(0));
    }

    [Fact]
    public void FrameJson_WhenPayloadIsTooLarge_IsRejected()
    {
        var oversizedJson = new string('a', SessionCommandProtocol.MaxMessageBytes + 1);

        Assert.Throws<SessionCommandFramingException>(
            () => SessionCommandFraming.FrameJson(oversizedJson));
    }

    [Fact]
    public void RequestJson_DoesNotContainGenericPayload()
    {
        var request = new SessionCommandRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            CommandType = SessionCommandTypes.ChannelPing
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"commandType\":\"CHANNEL_PING\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("payload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("arguments", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalIpcV1_RemainsReadOnlyOperationsOnly()
    {
        var operations = new[]
        {
            LocalIpcOperations.Ping,
            LocalIpcOperations.GetDeviceStatus,
            LocalIpcOperations.GetMachineCode,
            LocalIpcOperations.GetMasterAuthorization,
            LocalIpcOperations.GetRuntimeDiagnostics
        };

        Assert.DoesNotContain(SessionCommandTypes.ChannelPing, operations);
        Assert.DoesNotContain("OPEN_URL", operations);
        Assert.DoesNotContain("EXECUTE_COMMAND", operations);
    }
}
