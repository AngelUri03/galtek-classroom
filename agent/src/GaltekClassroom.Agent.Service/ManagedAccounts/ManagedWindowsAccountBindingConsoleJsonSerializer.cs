using System.Text.Json;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.ManagedAccounts;

public static class ManagedWindowsAccountBindingConsoleJsonSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string SerializeConfigurationResult(
        ManagedWindowsAccountBindingConfigurationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var payload = new ManagedWindowsAccountBindingConfigurationConsoleResult(
            result.Status.ToCode(),
            result.Succeeded,
            result.Accounts,
            result.BlockingReason);

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private sealed record ManagedWindowsAccountBindingConfigurationConsoleResult(
        string Status,
        bool Succeeded,
        IReadOnlyList<ManagedWindowsAccountView> Accounts,
        string? BlockingReason);
}
