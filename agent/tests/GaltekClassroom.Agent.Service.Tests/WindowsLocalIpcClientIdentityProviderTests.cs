using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using GaltekClassroom.Agent.Service.Ipc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaltekClassroom.Agent.Service.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsLocalIpcClientIdentityProviderTests
{
    [Fact]
    public async Task GetClientContext_UsesNamedPipeClientToken()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pipeName = $"GaltekClassroom.Agent.Identity.Tests.{Guid.NewGuid():N}";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var provider = new WindowsLocalIpcClientIdentityProvider(
            NullLogger<WindowsLocalIpcClientIdentityProvider>.Instance);

        var serverTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync(cancellation.Token);
            _ = server.ReadByte();
            return provider.GetClientContext(server);
        }, cancellation.Token);

        await using (var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(2000, cancellation.Token);
            client.WriteByte(1);
            await client.FlushAsync(cancellation.Token);
        }

        var context = await serverTask;
        using var currentIdentity = WindowsIdentity.GetCurrent();

        Assert.Equal(currentIdentity.User?.Value, context.WindowsSid);
        Assert.Equal(currentIdentity.Name, context.AccountName);
    }
}
