using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaltekClassroom.Agent.Service.Network;

public static class NetworkIdentityConsoleJsonSerializer
{
    private static readonly JsonSerializerOptions ConsoleJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string SerializeStatus(NetworkIdentityResolution resolution)
    {
        return JsonSerializer.Serialize(
            NetworkIdentityStatusConsoleDto.FromResolution(resolution),
            ConsoleJsonOptions);
    }

    private sealed record NetworkIdentityStatusConsoleDto(
        string Status,
        string? ErrorCode,
        string? NetworkIdentityId,
        string? PublicKeyFingerprint)
    {
        public static NetworkIdentityStatusConsoleDto FromResolution(
            NetworkIdentityResolution resolution)
        {
            return new NetworkIdentityStatusConsoleDto(
                resolution.Status.ToCode(),
                resolution.ErrorCode,
                resolution.Metadata?.NetworkIdentityId.ToString("D"),
                resolution.Metadata?.PublicKeyFingerprint);
        }
    }
}
