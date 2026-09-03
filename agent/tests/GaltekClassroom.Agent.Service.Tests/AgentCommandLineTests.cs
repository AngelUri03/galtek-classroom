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

    [Fact]
    public void Parse_WhenApplicationBindListIsSelected_UsesApplicationListMode()
    {
        var commandLine = AgentCommandLine.Parse(["--application-bind-list"]);

        Assert.True(commandLine.IsValid);
        Assert.Equal(AgentCommandMode.ApplicationBindList, commandLine.Mode);
    }

    [Fact]
    public void Parse_WhenApplicationBindExeWithReplaceIsSelected_CapturesArguments()
    {
        var commandLine = AgentCommandLine.Parse([
            "--application-bind-exe",
            "conejito-lector",
            @"C:\Program Files\Conejito\Conejito.exe",
            "--replace-application-binding"
        ]);

        Assert.True(commandLine.IsValid);
        Assert.Equal(AgentCommandMode.ApplicationBindExe, commandLine.Mode);
        Assert.Equal("conejito-lector", commandLine.ApplicationId);
        Assert.Equal(@"C:\Program Files\Conejito\Conejito.exe", commandLine.ApplicationTarget);
        Assert.True(commandLine.ReplaceApplicationBinding);
    }

    [Fact]
    public void Parse_WhenApplicationBindAppPathIsSelected_CapturesArguments()
    {
        var commandLine = AgentCommandLine.Parse([
            "--application-bind-app-path",
            "microsoft-word",
            "WINWORD.EXE"
        ]);

        Assert.True(commandLine.IsValid);
        Assert.Equal(AgentCommandMode.ApplicationBindAppPath, commandLine.Mode);
        Assert.Equal("microsoft-word", commandLine.ApplicationId);
        Assert.Equal("WINWORD.EXE", commandLine.ApplicationTarget);
    }

    [Theory]
    [InlineData("--application-bind-disable", AgentCommandMode.ApplicationBindDisable)]
    [InlineData("--application-bind-enable", AgentCommandMode.ApplicationBindEnable)]
    [InlineData("--application-bind-remove", AgentCommandMode.ApplicationBindRemove)]
    public void Parse_WhenApplicationBindingSingleIdCommandIsSelected_CapturesApplicationId(
        string argument,
        AgentCommandMode expectedMode)
    {
        var commandLine = AgentCommandLine.Parse([argument, "scratch"]);

        Assert.True(commandLine.IsValid);
        Assert.Equal(expectedMode, commandLine.Mode);
        Assert.Equal("scratch", commandLine.ApplicationId);
    }

    [Fact]
    public void Parse_WhenReplaceApplicationBindingHasNoBindingCommand_IsInvalid()
    {
        var commandLine = AgentCommandLine.Parse(["--replace-application-binding"]);

        Assert.False(commandLine.IsValid);
        Assert.Contains("--replace-application-binding", commandLine.ErrorMessage);
    }

    [Fact]
    public void Parse_WhenApplicationBindExeHasNoValues_IsInvalid()
    {
        var commandLine = AgentCommandLine.Parse(["--application-bind-exe", "word"]);

        Assert.False(commandLine.IsValid);
        Assert.Contains("--application-bind-exe requires", commandLine.ErrorMessage);
    }
}
