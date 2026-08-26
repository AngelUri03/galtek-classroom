using GaltekClassroom.Agent.Session.Lifecycle;

namespace GaltekClassroom.Agent.Session.Tests;

public sealed class SessionAgentCommandLineTests
{
    [Fact]
    public void Parse_WhenNoArguments_UsesBackgroundMode()
    {
        var commandLine = SessionAgentCommandLine.Parse([]);

        Assert.True(commandLine.IsValid);
        Assert.Equal(SessionAgentCommandMode.Background, commandLine.Mode);
        Assert.False(commandLine.IsOneShotIpcCommand);
    }

    [Fact]
    public void Parse_WhenIpcPingIsSelected_UsesOneShotIpcMode()
    {
        var commandLine = SessionAgentCommandLine.Parse(["--ipc-ping"]);

        Assert.True(commandLine.IsValid);
        Assert.Equal(SessionAgentCommandMode.IpcPing, commandLine.Mode);
        Assert.True(commandLine.IsOneShotIpcCommand);
    }

    [Fact]
    public void Parse_WhenIpcStatusIsSelected_UsesOneShotIpcMode()
    {
        var commandLine = SessionAgentCommandLine.Parse(["--ipc-status"]);

        Assert.True(commandLine.IsValid);
        Assert.Equal(SessionAgentCommandMode.IpcStatus, commandLine.Mode);
        Assert.True(commandLine.IsOneShotIpcCommand);
    }

    [Fact]
    public void Parse_WhenBackgroundConflictsWithIpcCommand_IsInvalid()
    {
        var commandLine = SessionAgentCommandLine.Parse(["--background", "--ipc-ping"]);

        Assert.False(commandLine.IsValid);
    }
}
