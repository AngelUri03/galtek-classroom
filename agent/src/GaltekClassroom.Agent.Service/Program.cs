using GaltekClassroom.Agent.Service;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Shared;

const string machineCodeArgument = "--machine-code";

var machineCodeMode = args.Contains(machineCodeArgument, StringComparer.OrdinalIgnoreCase);
var hostArgs = args
    .Where(argument => !string.Equals(argument, machineCodeArgument, StringComparison.OrdinalIgnoreCase))
    .ToArray();

var builder = Host.CreateApplicationBuilder(hostArgs);
builder.Services.AddInstallationIdentityServices();

if (machineCodeMode)
{
    builder.Logging.ClearProviders();

    await using var serviceProvider = builder.Services.BuildServiceProvider();
    var resolver = serviceProvider.GetRequiredService<InstallationIdentityResolver>();
    var machineCodeGenerator = serviceProvider.GetRequiredService<MachineCodeGenerator>();
    var resolution = await resolver.ResolveAsync(CancellationToken.None);

    if (resolution.Status != InstallationIdentityResolutionStatus.Ready)
    {
        await Console.Error.WriteLineAsync(
            $"Installation identity is not usable at {resolution.FilePath}: {resolution.ErrorMessage}");

        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine(machineCodeGenerator.Generate(resolution.Identity!));
    return;
}

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = ProductInfo.ServiceName;
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
