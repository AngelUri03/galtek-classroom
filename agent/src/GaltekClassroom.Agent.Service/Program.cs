using GaltekClassroom.Agent.Service;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.Ipc;
using GaltekClassroom.Agent.Service.Licensing;
using GaltekClassroom.Agent.Shared;

var commandLine = AgentCommandLine.Parse(args);

if (!commandLine.IsValid)
{
    await Console.Error.WriteLineAsync(commandLine.ErrorMessage);
    Environment.ExitCode = 2;
    return;
}

var builder = Host.CreateApplicationBuilder(commandLine.HostArgs);
builder.Services.AddInstallationIdentityServices();
builder.Services.AddCommercialLicenseServices();
builder.Services.AddLocalIpcServices();

if (commandLine.Mode == AgentCommandMode.MachineCode)
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

if (commandLine.Mode == AgentCommandMode.LicenseStatus)
{
    builder.Logging.ClearProviders();

    await using var serviceProvider = builder.Services.BuildServiceProvider();
    var state = await ResolveLicenseStateAsync(serviceProvider, CancellationToken.None);

    if (state is null)
    {
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine(LicenseConsoleJsonSerializer.SerializeState(state));
    return;
}

if (commandLine.Mode is AgentCommandMode.ActivateLicenseFromStdin or AgentCommandMode.ActivateLicenseFromFile)
{
    builder.Logging.ClearProviders();

    await using var serviceProvider = builder.Services.BuildServiceProvider();
    var candidateToken = commandLine.Mode == AgentCommandMode.ActivateLicenseFromFile
        ? await ReadLicenseFromFileAsync(commandLine.LicenseFilePath, CancellationToken.None)
        : await Console.In.ReadToEndAsync(CancellationToken.None);

    if (candidateToken is null)
    {
        Environment.ExitCode = 1;
        return;
    }

    var activationService = serviceProvider.GetRequiredService<CommercialLicenseActivationService>();
    var activationResult = await activationService.ActivateAsync(candidateToken, CancellationToken.None);

    Console.WriteLine(LicenseConsoleJsonSerializer.SerializeActivationResult(activationResult));

    if (!activationResult.Activated)
    {
        Environment.ExitCode = 1;
    }

    return;
}

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = ProductInfo.ServiceName;
});

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<CommercialLicenseRuntimeMonitor>();
builder.Services.AddHostedService<LocalIpcServer>();

var host = builder.Build();
await host.RunAsync();

static async Task<LicenseState?> ResolveLicenseStateAsync(
    IServiceProvider serviceProvider,
    CancellationToken cancellationToken)
{
    var installationIdentityResolver = serviceProvider.GetRequiredService<InstallationIdentityResolver>();
    var licenseManager = serviceProvider.GetRequiredService<CommercialLicenseManager>();
    var resolution = await installationIdentityResolver.ResolveAsync(cancellationToken);

    if (resolution.Status != InstallationIdentityResolutionStatus.Ready)
    {
        await Console.Error.WriteLineAsync(
            $"Installation identity is not usable at {resolution.FilePath}: {resolution.ErrorMessage}");

        return null;
    }

    return await licenseManager.ResolveAsync(resolution.Identity!, cancellationToken);
}

static async Task<string?> ReadLicenseFromFileAsync(
    string? filePath,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(filePath))
    {
        await Console.Error.WriteLineAsync("--activate-license-file requires a file path.");
        return null;
    }

    try
    {
        return await File.ReadAllTextAsync(Path.GetFullPath(filePath), cancellationToken);
    }
    catch (IOException exception)
    {
        await Console.Error.WriteLineAsync($"License file could not be read: {exception.Message}");
        return null;
    }
    catch (UnauthorizedAccessException exception)
    {
        await Console.Error.WriteLineAsync($"License file could not be read: {exception.Message}");
        return null;
    }
}
