using System.Text.Json;

namespace GaltekClassroom.Agent.Shared;

public sealed record CommercialLicenseFeatureValue
{
    public JsonValueKind Kind { get; init; }
    public bool? BooleanValue { get; init; }
    public long? IntegerValue { get; init; }
    public double? NumberValue { get; init; }
    public string? StringValue { get; init; }
    public string RawJson { get; init; } = "null";

    public static CommercialLicenseFeatureValue FromJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => new CommercialLicenseFeatureValue
            {
                Kind = JsonValueKind.True,
                BooleanValue = true,
                RawJson = element.GetRawText()
            },
            JsonValueKind.False => new CommercialLicenseFeatureValue
            {
                Kind = JsonValueKind.False,
                BooleanValue = false,
                RawJson = element.GetRawText()
            },
            JsonValueKind.Number => CreateNumberFeatureValue(element),
            JsonValueKind.String => new CommercialLicenseFeatureValue
            {
                Kind = JsonValueKind.String,
                StringValue = element.GetString(),
                RawJson = element.GetRawText()
            },
            _ => new CommercialLicenseFeatureValue
            {
                Kind = element.ValueKind,
                RawJson = element.GetRawText()
            }
        };
    }

    private static CommercialLicenseFeatureValue CreateNumberFeatureValue(JsonElement element)
    {
        var value = new CommercialLicenseFeatureValue
        {
            Kind = JsonValueKind.Number,
            RawJson = element.GetRawText()
        };

        if (element.TryGetInt64(out var integerValue))
        {
            value = value with { IntegerValue = integerValue };
        }

        if (element.TryGetDouble(out var numberValue))
        {
            value = value with { NumberValue = numberValue };
        }

        return value;
    }
}
