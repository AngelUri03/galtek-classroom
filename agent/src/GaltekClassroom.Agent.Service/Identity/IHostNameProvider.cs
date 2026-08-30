namespace GaltekClassroom.Agent.Service.Identity;

public interface IHostNameProvider
{
    string GetHostName();
}

public sealed class SystemHostNameProvider : IHostNameProvider
{
    private readonly string _hostName = Environment.MachineName;

    public string GetHostName()
    {
        return _hostName;
    }
}
