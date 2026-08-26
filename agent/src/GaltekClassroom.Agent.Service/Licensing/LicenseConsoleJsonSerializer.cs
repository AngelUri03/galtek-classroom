using System.Text.Json;
using System.Text.Json.Serialization;
using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Licensing;

public static class LicenseConsoleJsonSerializer
{
    private static readonly JsonSerializerOptions ConsoleJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string SerializeState(LicenseState state)
    {
        return JsonSerializer.Serialize(
            LicenseStateConsoleDto.FromState(state),
            ConsoleJsonOptions);
    }

    public static string SerializeActivationResult(CommercialLicenseActivationResult activationResult)
    {
        return JsonSerializer.Serialize(
            LicenseActivationConsoleDto.FromResult(activationResult),
            ConsoleJsonOptions);
    }

    private static object? ToConsoleFeatureValue(CommercialLicenseFeatureValue value)
    {
        if (value.BooleanValue is not null)
        {
            return value.BooleanValue.Value;
        }

        if (value.IntegerValue is not null)
        {
            return value.IntegerValue.Value;
        }

        if (value.NumberValue is not null)
        {
            return value.NumberValue.Value;
        }

        if (value.StringValue is not null)
        {
            return value.StringValue;
        }

        return JsonSerializer.Deserialize<JsonElement>(value.RawJson);
    }

    private static IReadOnlyDictionary<string, object?>? ToConsoleFeatures(CommercialLicenseFeatures features)
    {
        if (features.Values.Count == 0)
        {
            return null;
        }

        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);

        foreach (var feature in features.Values)
        {
            values[feature.Key] = ToConsoleFeatureValue(feature.Value);
        }

        return values;
    }

    private sealed record LicenseStateConsoleDto(
        string Status,
        bool Active,
        string? LicenseId,
        string? OrganizationId,
        DateTimeOffset? ExpiresAtUtc,
        IReadOnlyList<string>? Roles,
        IReadOnlyDictionary<string, object?>? Features,
        DateTimeOffset LastValidatedAtUtc,
        string? BlockingReason)
    {
        public static LicenseStateConsoleDto FromState(LicenseState state)
        {
            return new LicenseStateConsoleDto(
                state.Status.ToCode(),
                state.Active,
                state.LicenseId,
                state.OrganizationId,
                state.ExpiresAtUtc,
                state.Roles.Count == 0 ? null : state.Roles,
                ToConsoleFeatures(state.Features),
                state.LastValidatedAtUtc,
                state.BlockingReason);
        }
    }

    private sealed record LicenseActivationConsoleDto(
        bool Activated,
        string Status,
        bool Active,
        string CurrentStatus,
        string? LicenseId,
        DateTimeOffset? ExpiresAtUtc,
        string? BlockingReason)
    {
        public static LicenseActivationConsoleDto FromResult(CommercialLicenseActivationResult result)
        {
            return new LicenseActivationConsoleDto(
                result.Activated,
                result.CandidateState.Status.ToCode(),
                result.CandidateState.Active,
                result.CurrentState.Status.ToCode(),
                result.CurrentState.LicenseId,
                result.CurrentState.ExpiresAtUtc,
                result.CandidateState.BlockingReason);
        }
    }
}
