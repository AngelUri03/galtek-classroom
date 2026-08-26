using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Session.Tests;

public sealed class SessionProductInfoTests
{
    [Fact]
    public void SessionAgentIdentity_IsStable()
    {
        Assert.Equal("Galtek Classroom Session Agent", ProductInfo.SessionAgentName);
        Assert.Equal("GaltekClassroom.Agent.Session.exe", ProductInfo.SessionAgentExecutableName);
        Assert.Equal("GaltekClassroomSessionAgent", ProductInfo.SessionAgentTaskName);
        Assert.Equal(@"Local\GaltekClassroom.Agent.Session", ProductInfo.SessionAgentMutexName);
        Assert.Equal("Session", ProductInfo.SessionAgentInstallSubdirectory);
    }
}
