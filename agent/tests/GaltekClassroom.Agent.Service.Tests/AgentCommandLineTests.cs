using GaltekClassroom.Agent.Service;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class AgentCommandLineTests
{
    [Fact]
    public void Parse_WhenBindMasterCurrentUserIsSelected_UsesBindingMode()
    {
        var commandLine = AgentCommandLine.Parse(["--bind-master-current-user"]);

        Assert.True(commandLine.IsValid);
        Assert.Equal(AgentCommandMode.BindMasterCurrentUser, commandLine.Mode);
        Assert.False(commandLine.ReplaceMasterBinding);
    }

    [Fact]
    public void Parse_WhenBindMasterAccountWithReplaceIsSelected_CapturesAccountAndReplace()
    {
        var commandLine = AgentCommandLine.Parse([
            "--bind-master-account",
            "AULA\\MaestraPrimaria",
            "--replace-master-binding"
        ]);

        Assert.True(commandLine.IsValid);
        Assert.Equal(AgentCommandMode.BindMasterAccount, commandLine.Mode);
        Assert.Equal("AULA\\MaestraPrimaria", commandLine.MasterAccountName);
        Assert.True(commandLine.ReplaceMasterBinding);
    }

    [Fact]
    public void Parse_WhenReplaceBindingHasNoBindingCommand_IsInvalid()
    {
        var commandLine = AgentCommandLine.Parse(["--replace-master-binding"]);

        Assert.False(commandLine.IsValid);
        Assert.Contains("--replace-master-binding", commandLine.ErrorMessage);
    }

    [Fact]
    public void Parse_WhenBindMasterAccountHasNoValue_IsInvalid()
    {
        var commandLine = AgentCommandLine.Parse(["--bind-master-account"]);

        Assert.False(commandLine.IsValid);
        Assert.Contains("--bind-master-account requires", commandLine.ErrorMessage);
    }

    [Fact]
    public void Parse_WhenNetworkIdentityStatusIsSelected_UsesStatusMode()
    {
        var commandLine = AgentCommandLine.Parse(["--network-identity-status"]);

        Assert.True(commandLine.IsValid);
        Assert.Equal(AgentCommandMode.NetworkIdentityStatus, commandLine.Mode);
    }

    [Fact]
    public void Parse_WhenRuntimeDiagnosticsIsSelected_UsesRuntimeDiagnosticsMode()
    {
        var commandLine = AgentCommandLine.Parse(["--runtime-diagnostics"]);

        Assert.True(commandLine.IsValid);
        Assert.Equal(AgentCommandMode.RuntimeDiagnostics, commandLine.Mode);
    }
}
