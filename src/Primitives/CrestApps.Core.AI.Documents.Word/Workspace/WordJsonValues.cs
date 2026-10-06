using System.Globalization;
using System.Text.Json;

namespace CrestApps.Core.AI.Documents.Word.Workspace;

/// <summary>
/// Reads values out of the JSON a model writes, accepting the shapes models use interchangeably: numbers and
/// booleans written as strings, a single value where a list was asked for, and property names in any case.
/// </summary>
internal static class WordJsonValues
{
    /// <summary>
    /// Reads a property of an object, ignoring the case of its name and accepting camelCase for snake_case.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The snake_case property name.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when the property is present and not null.</returns>
    public static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        value = default;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var compact = name.Replace("_", string.Empty, StringComparison.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name.Replace("_", string.Empty, StringComparison.Ordinal), compact, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;

                return value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
            }
        }

        return false;
    }

    /// <summary>
    /// Reads a string property.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing or blank.</returns>
    public static string GetString(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value))
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// Reads a string property keeping its spacing, for text whose whitespace matters such as code.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing.</returns>
    public static string GetRawString(JsonElement element, string name)
    {
        return TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : GetString(element, name);
    }

    /// <summary>
    /// Reads a number property.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static double? GetDouble(JsonElement element, string name)
    {
        return TryGet(element, name, out var value) ? ReadDouble(value) : null;
    }

    /// <summary>
    /// Reads a whole-number property.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static int? GetInt(JsonElement element, string name)
    {
        var value = GetDouble(element, name);

        return value.HasValue && value.Value is >= int.MinValue and <= int.MaxValue ? (int)Math.Round(value.Value) : null;
    }

    /// <summary>
    /// Reads a boolean property.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static bool? GetBoolean(JsonElement element, string name)
    {
        return TryGet(element, name, out var value) ? ReadBoolean(value) : null;
    }

    /// <summary>
    /// Reads a list-of-strings property, accepting a single string.
    /// </summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property name.</param>
    /// <param name="splitCommas">Whether a single string is split at commas.</param>
    /// <returns>The values, or an empty list.</returns>
    public static List<string> GetStrings(JsonElement element, string name, bool splitCommas = false)
    {
        return TryGet(element, name, out var value) ? ReadStrings(value, splitCommas) : [];
    }

    /// <summary>
    /// Reads a value as a number.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The number, or <see langword="null"/>.</returns>
    public static double? ReadDouble(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString()?.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>
    /// Reads a value as a boolean.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The boolean, or <see langword="null"/>.</returns>
    public static bool? ReadBoolean(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.TryGetDouble(out var number) ? number != 0 : null,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed)
                ? parsed
                : value.GetString()?.Trim().ToLowerInvariant() switch
                {
                    "yes" or "1" or "on" => true,
                    "no" or "0" or "off" => false,
                    _ => null,
                },
            _ => null,
        };
    }

    /// <summary>
    /// Reads a value as a list of strings, accepting a single string.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="splitCommas">Whether a single string is split at commas.</param>
    /// <returns>The values.</returns>
    public static List<string> ReadStrings(JsonElement value, bool splitCommas)
    {
        var values = new List<string>();

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();

                if (!string.IsNullOrWhiteSpace(text))
                {
                    values.Add(text.Trim());
                }
            }

            return values;
        }

        var single = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;

        if (string.IsNullOrWhiteSpace(single))
        {
            return values;
        }

        if (splitCommas)
        {
            values.AddRange(single.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        else
        {
            values.Add(single.Trim());
        }

        return values;
    }

    /// <summary>
    /// Reads a scalar as the CLR value a table cell is presented from: a number stays a number, so a column
    /// format applies to it.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The value.</returns>
    public static object ReadScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "Yes",
            JsonValueKind.False => "No",
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => value.GetRawText(),
        };
    }
}
