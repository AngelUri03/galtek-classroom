namespace GaltekClassroom.Agent.Service.Identity;

public interface IHostNameProvider
{
    string GetHostName();
}

public sealed class SystemHostNameProvider : IHostNameProvider
{
    public string GetHostName()
    {
        return Environment.MachineName;
    }
}
