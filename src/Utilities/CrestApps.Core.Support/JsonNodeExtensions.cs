using System.Text.Json;
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
    /// Tries to get a boolean property value from a JSON object.
    /// </summary>
    /// <param name="node">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The parsed boolean value.</param>
    public static bool TryGetBooleanValue(this JsonObject node, string propertyName, out bool value)
    {
        value = default;

        if (node is null || !node.TryGetPropertyValue(propertyName, out var propertyNode) || propertyNode is null)
        {
            return false;
        }

        return propertyNode.TryGetBooleanValue(out value);
    }

    /// <summary>
    /// Tries to get a trimmed string property value from a JSON object.
    /// </summary>
    /// <param name="node">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The trimmed value.</param>
    public static bool TryGetTrimmedStringValue(this JsonObject node, string propertyName, out string value)
    {
        value = null;

        if (node is null || !node.TryGetPropertyValue(propertyName, out var propertyNode))
        {
            return false;
        }

        if (propertyNode is null)
        {
            return true;
        }

        value = propertyNode.GetStringValue()?.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            value = null;
        }

        return true;
    }

    /// <summary>
    /// Tries to get a trimmed string property value from a primary or fallback JSON object.
    /// </summary>
    /// <param name="node">The primary JSON object.</param>
    /// <param name="fallbackNode">The fallback JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The trimmed value.</param>
    public static bool TryGetTrimmedStringValue(this JsonObject node, JsonObject fallbackNode, string propertyName, out string value)
    {
        if (node.TryGetTrimmedStringValue(propertyName, out value))
        {
            return true;
        }

        return fallbackNode.TryGetTrimmedStringValue(propertyName, out value);
    }

    /// <summary>
    /// Tries to update a trimmed string property value from a JSON object.
    /// </summary>
    /// <param name="node">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="update">The update callback.</param>
    public static bool TryUpdateTrimmedStringValue(this JsonObject node, string propertyName, Action<string> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (!node.TryGetTrimmedStringValue(propertyName, out var value))
        {
            return false;
        }

        update(value);

        return true;
    }

    /// <summary>
    /// Tries to update a trimmed string property value from a primary or fallback JSON object.
    /// </summary>
    /// <param name="node">The primary JSON object.</param>
    /// <param name="fallbackNode">The fallback JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="update">The update callback.</param>
    public static bool TryUpdateTrimmedStringValue(this JsonObject node, JsonObject fallbackNode, string propertyName, Action<string> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (!node.TryGetTrimmedStringValue(fallbackNode, propertyName, out var value))
        {
            return false;
        }

        update(value);

        return true;
    }

    /// <summary>
    /// Tries to get a date time property value from a JSON object.
    /// </summary>
    /// <param name="node">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The parsed date time value.</param>
    public static bool TryGetDateTimeValue(this JsonObject node, string propertyName, out DateTime value)
    {
        value = default;

        if (node is null || !node.TryGetPropertyValue(propertyName, out var propertyNode) || propertyNode is null)
        {
            return false;
        }

        if (propertyNode is JsonValue jsonValue && jsonValue.TryGetValue(out DateTime dateTime))
        {
            value = dateTime;

            return true;
        }

        return propertyNode is JsonValue stringValue &&
            stringValue.TryGetValue(out string text) &&
            DateTime.TryParse(text, out value);
    }

    /// <summary>
    /// Tries to get a nested JSON object property value from a JSON object.
    /// </summary>
    /// <param name="node">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The nested JSON object.</param>
    public static bool TryGetObjectValue(this JsonObject node, string propertyName, out JsonObject value)
    {
        value = null;

        if (node is null || !node.TryGetPropertyValue(propertyName, out var propertyNode))
        {
            return false;
        }

        if (propertyNode is null)
        {
            return true;
        }

        value = propertyNode as JsonObject;

        return value is not null;
    }

    /// <summary>
    /// Tries to get a nullable 32-bit integer property value from a JSON object.
    /// </summary>
    /// <param name="node">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The parsed integer value.</param>
    public static bool TryGetNullableInt32Value(this JsonObject node, string propertyName, out int? value)
    {
        value = null;

        if (node is null || !node.TryGetPropertyValue(propertyName, out var propertyNode))
        {
            return false;
        }

        if (propertyNode is null)
        {
            return true;
        }

        if (propertyNode is JsonValue intValue && intValue.TryGetValue(out int parsedInt))
        {
            value = parsedInt;

            return true;
        }

        if (propertyNode is JsonValue longValue && longValue.TryGetValue(out long parsedLong))
        {
            value = (int)parsedLong;

            return true;
        }

        return propertyNode is JsonValue stringValue &&
            stringValue.TryGetValue(out string text) &&
            int.TryParse(text, out parsedInt) &&
            (value = parsedInt) is not null;
    }

    /// <summary>
    /// Tries to get a nullable single-precision floating-point property value from a JSON object.
    /// </summary>
    /// <param name="node">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The parsed floating-point value.</param>
    public static bool TryGetNullableSingleValue(this JsonObject node, string propertyName, out float? value)
    {
        value = null;

        if (node is null || !node.TryGetPropertyValue(propertyName, out var propertyNode))
        {
            return false;
        }

        if (propertyNode is null)
        {
            return true;
        }

        if (propertyNode is JsonValue singleValue && singleValue.TryGetValue(out float parsedFloat))
        {
            value = parsedFloat;

            return true;
        }

        if (propertyNode is JsonValue doubleValue && doubleValue.TryGetValue(out double parsedDouble))
        {
            value = (float)parsedDouble;

            return true;
        }

        return propertyNode is JsonValue stringValue &&
            stringValue.TryGetValue(out string text) &&
            float.TryParse(text, out parsedFloat) &&
            (value = parsedFloat) is not null;
    }

    /// <summary>
    /// Tries to get an enum property value from a JSON object.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="node">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The parsed enum value.</param>
    public static bool TryGetEnumValue<TEnum>(this JsonObject node, string propertyName, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;

        if (node is null || !node.TryGetPropertyValue(propertyName, out var propertyNode) || propertyNode is null)
        {
            return false;
        }

        if (propertyNode is JsonValue intValue && intValue.TryGetValue(out int number))
        {
            value = (TEnum)Enum.ToObject(typeof(TEnum), number);

            return true;
        }

        return propertyNode is JsonValue stringValue &&
            stringValue.TryGetValue(out string text) &&
            Enum.TryParse(text, true, out value);
    }

    /// <summary>
    /// Tries to get an enum property value from a primary or fallback JSON object.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="node">The primary JSON object.</param>
    /// <param name="fallbackNode">The fallback JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The parsed enum value.</param>
    public static bool TryGetEnumValue<TEnum>(this JsonObject node, JsonObject fallbackNode, string propertyName, out TEnum value)
        where TEnum : struct, Enum
    {
        if (node.TryGetEnumValue(propertyName, out value))
        {
            return true;
        }

        return fallbackNode.TryGetEnumValue(propertyName, out value);
    }

    /// <summary>
    /// Tries to get a nullable enum property value from a JSON object.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="node">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The parsed enum value.</param>
    public static bool TryGetNullableEnumValue<TEnum>(this JsonObject node, string propertyName, out TEnum? value)
        where TEnum : struct, Enum
    {
        value = null;

        if (node is null || !node.TryGetPropertyValue(propertyName, out var propertyNode))
        {
            return false;
        }

        if (propertyNode is null)
        {
            return true;
        }

        if (node.TryGetEnumValue(propertyName, out TEnum parsedValue))
        {
            value = parsedValue;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to get a string array property value from a JSON object.
    /// </summary>
    /// <param name="node">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="values">The parsed string array.</param>
    public static bool TryGetStringArrayValue(this JsonObject node, string propertyName, out string[] values)
    {
        values = null;

        if (node is null || !node.TryGetPropertyValue(propertyName, out var propertyNode))
        {
            return false;
        }

        if (propertyNode is null)
        {
            values = [];

            return true;
        }

        values = propertyNode.Deserialize<string[]>() ?? [];

        return true;
    }

    /// <summary>
    /// Tries to get a string array property value from a primary or fallback JSON object.
    /// </summary>
    /// <param name="node">The primary JSON object.</param>
    /// <param name="fallbackNode">The fallback JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="values">The parsed string array.</param>
    public static bool TryGetStringArrayValue(this JsonObject node, JsonObject fallbackNode, string propertyName, out string[] values)
    {
        if (node.TryGetStringArrayValue(propertyName, out values))
        {
            return true;
        }

        return fallbackNode.TryGetStringArrayValue(propertyName, out values);
    }

    /// <summary>
    /// Tries to get a trimmed string list property value from a JSON object.
    /// </summary>
    /// <param name="node">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="values">The parsed trimmed string list.</param>
    public static bool TryGetTrimmedStringListValue(this JsonObject node, string propertyName, out List<string> values)
    {
        values = null;

        if (!node.TryGetStringArrayValue(propertyName, out var array))
        {
            return false;
        }

        values =
        [
            .. array
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim()),
        ];

        return true;
    }

    /// <summary>
    /// Tries to get a dictionary property value from a JSON object.
    /// </summary>
    /// <param name="node">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="values">The parsed dictionary.</param>
    public static bool TryGetDictionaryValue(this JsonObject node, string propertyName, out Dictionary<string, string> values)
    {
        values = null;

        if (node is null || !node.TryGetPropertyValue(propertyName, out var propertyNode))
        {
            return false;
        }

        if (propertyNode is null)
        {
            values = [];

            return true;
        }

        if (propertyNode is JsonValue jsonValue && jsonValue.TryGetValue(out string jsonText))
        {
            values = string.IsNullOrWhiteSpace(jsonText)
                ? []
                : JsonSerializer.Deserialize<Dictionary<string, string>>(jsonText) ?? [];

            return true;
        }

        values = propertyNode.Deserialize<Dictionary<string, string>>() ?? [];

        return true;
    }

    /// <summary>
    /// Tries to get a dictionary property value from a primary or fallback JSON object.
    /// </summary>
    /// <param name="node">The primary JSON object.</param>
    /// <param name="fallbackNode">The fallback JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="values">The parsed dictionary.</param>
    public static bool TryGetDictionaryValue(this JsonObject node, JsonObject fallbackNode, string propertyName, out Dictionary<string, string> values)
    {
        if (node.TryGetDictionaryValue(propertyName, out values))
        {
            return true;
        }

        return fallbackNode.TryGetDictionaryValue(propertyName, out values);
    }

    /// <summary>
    /// Tries to get a URI property value from a JSON object.
    /// </summary>
    /// <param name="node">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The parsed URI.</param>
    public static bool TryGetUriValue(this JsonObject node, string propertyName, out Uri value)
    {
        value = null;

        if (!node.TryGetTrimmedStringValue(propertyName, out var text))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out value);
    }

    /// <summary>
    /// Tries to get a URI property value from a primary or fallback JSON object.
    /// </summary>
    /// <param name="node">The primary JSON object.</param>
    /// <param name="fallbackNode">The fallback JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The parsed URI.</param>
    public static bool TryGetUriValue(this JsonObject node, JsonObject fallbackNode, string propertyName, out Uri value)
    {
        value = null;

        if (!node.TryGetTrimmedStringValue(fallbackNode, propertyName, out var text))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out value);
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
