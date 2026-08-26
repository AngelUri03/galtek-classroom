using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GaltekClassroom.Agent.Service.Ipc;

public interface ILocalIpcPipeStreamFactory
{
    NamedPipeServerStream CreateServerStream(LocalIpcServerOptions options);
}

public sealed class UnsupportedLocalIpcPipeStreamFactory : ILocalIpcPipeStreamFactory
{
    public NamedPipeServerStream CreateServerStream(LocalIpcServerOptions options)
    {
        throw new PlatformNotSupportedException("Galtek Classroom Local IPC uses Windows Named Pipes.");
    }
}

[SupportedOSPlatform("windows")]
public sealed class LocalIpcPipeStreamFactory : ILocalIpcPipeStreamFactory
{
    public NamedPipeServerStream CreateServerStream(LocalIpcServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return NamedPipeServerStreamAcl.Create(
            options.PipeName,
            PipeDirection.InOut,
            options.MaxConcurrentConnections,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            inBufferSize: 0,
            outBufferSize: 0,
            CreatePipeSecurity());
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
            AccessControlType.Allow));

        return security;
    }
}
