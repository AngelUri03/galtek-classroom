using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace GaltekClassroom.Agent.Service.Ipc;

public interface ILocalIpcClientIdentityProvider
{
    LocalIpcClientContext GetClientContext(PipeStream pipe);
}

public sealed class UnavailableLocalIpcClientIdentityProvider : ILocalIpcClientIdentityProvider
{
    public LocalIpcClientContext GetClientContext(PipeStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        return LocalIpcClientContext.Unavailable();
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsLocalIpcClientIdentityProvider : ILocalIpcClientIdentityProvider
{
    private readonly ILogger<WindowsLocalIpcClientIdentityProvider> _logger;

    public WindowsLocalIpcClientIdentityProvider(
        ILogger<WindowsLocalIpcClientIdentityProvider> logger)
    {
        _logger = logger;
    }

    public LocalIpcClientContext GetClientContext(PipeStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        if (pipe is not NamedPipeServerStream serverStream)
        {
            _logger.LogWarning("IPC client identity is unavailable because the pipe is not a server stream.");
            return LocalIpcClientContext.Unavailable();
        }

        try
        {
            string? windowsSid = null;
            string? accountName = null;

            serverStream.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                windowsSid = identity.User?.Value;
                accountName = string.IsNullOrWhiteSpace(identity.Name)
                    ? null
                    : identity.Name;
            });

            return new LocalIpcClientContext(windowsSid, accountName);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or SystemException)
        {
            _logger.LogWarning(exception, "IPC client identity could not be resolved.");
            return LocalIpcClientContext.Unavailable();
        }
    }
}
