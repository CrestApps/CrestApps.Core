using System.Text.Json.Nodes;

namespace CrestApps.Core.Support;

public static class JsonNodeExtensions
{
    /// <summary>
    /// Gets string value.
    /// </summary>
    /// <param name="node">The node.</param>
    public static string GetStringValue(this JsonNode node)
    {
        if (node == null)
        {
            return null;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }

            return jsonValue.ToString();
        }

        return node.ToJsonString();
    }

    /// <summary>
    /// Gets boolean value.
    /// </summary>
    /// <param name="node">The node.</param>
    public static bool GetBooleanValue(this JsonNode node)
        => node.TryGetBooleanValue(out var value) && value;

    /// <summary>
    /// Tries to get boolean value.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="value">The value.</param>
    public static bool TryGetBooleanValue(this JsonNode node, out bool value)
    {
        value = default;

        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue(out value))
        {
            return true;
        }

        return jsonValue.TryGetValue<string>(out var stringValue) &&
            bool.TryParse(stringValue, out value);
    }

    /// <summary>
    /// Gets raw value.
    /// </summary>
    /// <param name="node">The node.</param>
    public static object GetRawValue(this JsonNode node)
    {
        if (node == null)
        {
            return null;
        }

        return node switch
        {
            JsonObject jsonObject => jsonObject.ToDictionary(
                property => property.Key,
                property => property.Value.GetRawValue(),
                StringComparer.OrdinalIgnoreCase),
            JsonArray jsonArray => jsonArray.Select(static item => item.GetRawValue()).ToList(),
            JsonValue jsonValue when jsonValue.TryGetValue<string>(out var stringValue) => stringValue,
            JsonValue jsonValue when jsonValue.TryGetValue<long>(out var longValue) => longValue,
            JsonValue jsonValue when jsonValue.TryGetValue<double>(out var doubleValue) => doubleValue,
            JsonValue jsonValue when jsonValue.TryGetValue<bool>(out var boolValue) => boolValue,
            JsonValue jsonValue when jsonValue.TryGetValue<DateTime>(out var dateValue) => dateValue,
            _ => node.ToJsonString(),
        };
    }
}
