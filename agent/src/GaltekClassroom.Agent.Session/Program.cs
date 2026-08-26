using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Agent.Session.Ipc;
using System.Text.Json;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true
};

if (args.Any(argument => string.Equals(argument, "--ipc-status", StringComparison.OrdinalIgnoreCase)))
{
    var client = new LocalAgentIpcClient();
    var status = await client.GetDeviceStatusAsync(CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(status, jsonOptions));
    return;
}

if (args.Any(argument => string.Equals(argument, "--ipc-ping", StringComparison.OrdinalIgnoreCase)))
{
    var client = new LocalAgentIpcClient();
    var ping = await client.PingAsync(CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(ping, jsonOptions));
    return;
}

Console.WriteLine($"{ProductInfo.SessionAgentName} starting.");
Console.WriteLine($"{ProductInfo.SessionAgentName} stopped cleanly.");
