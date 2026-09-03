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
    public void OpenUrlRequestJson_UsesTypedOpenUrlShape()
    {
        var request = new SessionCommandRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            CommandType = SessionCommandTypes.OpenUrl,
            OpenUrl = new SessionOpenUrlCommand
            {
                OperationId = Guid.NewGuid().ToString("D"),
                Url = "https://example.test/activity"
            }
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"commandType\":\"OPEN_URL\"", json, StringComparison.Ordinal);
        Assert.Contains("\"openUrl\":", json, StringComparison.Ordinal);
        Assert.Contains("\"operationId\":", json, StringComparison.Ordinal);
        Assert.Contains("\"url\":\"https://example.test/activity\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("payload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("arguments", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("executablePath", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenApplicationRequestJson_UsesOnlyTypedApplicationIdShape()
    {
        var request = new SessionCommandRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            CommandType = SessionCommandTypes.OpenApplication,
            OpenApplication = new SessionOpenApplicationCommand
            {
                ApplicationId = "conejito-lector"
            }
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"commandType\":\"OPEN_APPLICATION\"", json, StringComparison.Ordinal);
        Assert.Contains("\"openApplication\":", json, StringComparison.Ordinal);
        Assert.Contains("\"applicationId\":\"conejito-lector\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("executablePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("arguments", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workingDirectory", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shell", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uri", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(SessionCommandTypes.LockInput)]
    [InlineData(SessionCommandTypes.UnlockInput)]
    public void InputControlRequestJson_UsesTypedCommandWithoutFunctionalPayload(string commandType)
    {
        var request = new SessionCommandRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            CommandType = commandType
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains($"\"commandType\":\"{commandType}\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("payload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("keyCodes", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("keyboardOnly", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mouseOnly", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("duration", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("timeout", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command\":", json, StringComparison.OrdinalIgnoreCase);
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
        Assert.DoesNotContain("OPEN_APPLICATION", operations);
        Assert.DoesNotContain("LOCK_INPUT", operations);
        Assert.DoesNotContain("UNLOCK_INPUT", operations);
        Assert.DoesNotContain("EXECUTE_COMMAND", operations);
    }
}
