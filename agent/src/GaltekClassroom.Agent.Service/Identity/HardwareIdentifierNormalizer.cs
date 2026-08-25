using System.Text.RegularExpressions;

namespace GaltekClassroom.Agent.Service.Identity;

public static partial class HardwareIdentifierNormalizer
{
    public const string UnavailableValue = "UNAVAILABLE";

    private static readonly HashSet<string> PlaceholderValues = new(StringComparer.Ordinal)
    {
        "TOBEFILLEDBYOEM",
        "DEFAULTSTRING",
        "SYSTEMSERIALNUMBER",
        "NOSERIALNUMBER",
        "NOTAPPLICABLE",
        "NOTAVAILABLE",
        "UNKNOWN",
        "NONE",
        "NULL",
        "NA"
    };

    public static string? NormalizeSingle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = WhitespaceRegex()
            .Replace(value.Trim(), " ")
            .ToUpperInvariant();

        return IsPlaceholder(normalized) ? null : normalized;
    }

    public static IReadOnlyList<string> NormalizeMany(IEnumerable<string?> values)
    {
        return values
            .Select(NormalizeSingle)
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static string NormalizeCollectionOrUnavailable(IEnumerable<string?> values)
    {
        var normalizedValues = NormalizeMany(values);

        return normalizedValues.Count == 0
            ? UnavailableValue
            : string.Join("|", normalizedValues);
    }

    private static bool IsPlaceholder(string normalized)
    {
        var compact = PlaceholderRegex().Replace(normalized, string.Empty);

        if (compact.Length == 0)
        {
            return true;
        }

        if (compact.Length >= 4
            && (compact.All(character => character == '0') || compact.All(character => character == 'F')))
        {
            return true;
        }

        return PlaceholderValues.Contains(compact);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex PlaceholderRegex();
}
