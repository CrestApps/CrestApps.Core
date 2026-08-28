using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrestApps.Core.AI.Tooling.Parameters;

/// <summary>
/// Coerces a raw parameter value onto its declared <see cref="AIToolParameterType"/>. Values reach a tool
/// as loosely typed JSON — an AI model routinely sends a quoted number for an integer — so a declared
/// type is only worth having if a convertible value is accepted and one that is not produces a clear
/// error the model can correct on a retry.
/// </summary>
public static class AIToolParameterValueConverter
{
    /// <summary>
    /// Attempts to convert a raw value to the declared parameter type.
    /// </summary>
    /// <param name="value">The raw value, which may be a <see cref="JsonElement"/>, a <see cref="JsonNode"/>, a CLR primitive, or a string.</param>
    /// <param name="type">The declared parameter type.</param>
    /// <param name="converted">The converted value when conversion succeeds.</param>
    /// <returns><see langword="true"/> when the value was converted; otherwise <see langword="false"/>.</returns>
    public static bool TryConvert(object value, AIToolParameterType type, out object converted)
    {
        converted = null;

        if (value is null)
        {
            return false;
        }

        // Unwrap the JSON representations first so the conversions below only deal with CLR values.
        if (value is JsonElement element)
        {
            return TryConvertJsonElement(element, type, out converted);
        }

        if (value is JsonNode node)
        {
            return TryConvertJsonElement(JsonSerializer.SerializeToElement(node), type, out converted);
        }

        switch (type)
        {
            case AIToolParameterType.String:
                converted = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);

                return converted is not null;

            case AIToolParameterType.Integer:
                return TryConvertInteger(value, out converted);

            case AIToolParameterType.Number:
                return TryConvertNumber(value, out converted);

            case AIToolParameterType.Boolean:
                return TryConvertBoolean(value, out converted);

            case AIToolParameterType.Array:
            case AIToolParameterType.Object:
                // Structured values pass through untouched; the source decides how to serialize them.
                converted = value;

                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Renders a converted value as the string form used when placing it into a URL path, a query string,
    /// or a header. Structured values are serialized as compact JSON.
    /// </summary>
    /// <param name="value">The converted value.</param>
    /// <returns>The string form, or <see langword="null"/> when the value is null.</returns>
    public static string ToStringValue(object value)
    {
        return value switch
        {
            null => null,
            string text => text,
            bool flag => flag ? "true" : "false",
            JsonElement element => element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.GetRawText(),
            JsonNode node => node.ToJsonString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    private static bool TryConvertJsonElement(JsonElement element, AIToolParameterType type, out object converted)
    {
        converted = null;

        switch (type)
        {
            case AIToolParameterType.String:
                converted = element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
                    _ => null,
                };

                return converted is not null;

            case AIToolParameterType.Integer:
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var int64))
                {
                    converted = int64;

                    return true;
                }

                return element.ValueKind == JsonValueKind.String && TryConvertInteger(element.GetString(), out converted);

            case AIToolParameterType.Number:
                if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var real))
                {
                    converted = real;

                    return true;
                }

                return element.ValueKind == JsonValueKind.String && TryConvertNumber(element.GetString(), out converted);

            case AIToolParameterType.Boolean:
                if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    converted = element.GetBoolean();

                    return true;
                }

                return element.ValueKind == JsonValueKind.String && TryConvertBoolean(element.GetString(), out converted);

            case AIToolParameterType.Array:
                if (element.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                converted = element;

                return true;

            case AIToolParameterType.Object:
                if (element.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                converted = element;

                return true;

            default:
                return false;
        }
    }

    private static bool TryConvertInteger(object value, out object converted)
    {
        converted = null;

        switch (value)
        {
            case long or int or short or byte:
                converted = Convert.ToInt64(value, CultureInfo.InvariantCulture);

                return true;

            case double or float or decimal:
                var real = Convert.ToDecimal(value, CultureInfo.InvariantCulture);

                // Only accept a fractional value when it represents a whole number exactly.
                if (decimal.Truncate(real) != real)
                {
                    return false;
                }

                converted = (long)real;

                return true;

            case string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                converted = parsed;

                return true;

            default:
                return false;
        }
    }

    private static bool TryConvertNumber(object value, out object converted)
    {
        converted = null;

        switch (value)
        {
            case double or float or decimal or long or int or short or byte:
                converted = Convert.ToDouble(value, CultureInfo.InvariantCulture);

                return true;

            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                converted = parsed;

                return true;

            default:
                return false;
        }
    }

    private static bool TryConvertBoolean(object value, out object converted)
    {
        converted = null;

        switch (value)
        {
            case bool flag:
                converted = flag;

                return true;

            case string text when bool.TryParse(text, out var parsed):
                converted = parsed;

                return true;

            default:
                return false;
        }
    }
}
