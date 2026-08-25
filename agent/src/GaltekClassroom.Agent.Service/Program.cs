using GaltekClassroom.Agent.Service;
using GaltekClassroom.Agent.Shared;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = ProductInfo.ServiceName;
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
