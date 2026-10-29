using System.Globalization;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Validates and normalizes an extracted value against its field's <see cref="DataExtractionFieldType"/>. Untyped
/// (<see cref="DataExtractionFieldType.Text"/>) fields keep the value as extracted.
/// </summary>
public static class DataExtractionValueNormalizer
{
    private static readonly string[] _trueValues = ["true", "yes", "y", "yeah", "yep", "1", "on"];
    private static readonly string[] _falseValues = ["false", "no", "n", "nope", "0", "off"];

    /// <summary>
    /// Gets whether the field keeps several values: it allows them explicitly, or it is a
    /// <see cref="DataExtractionFieldType.List"/>.
    /// </summary>
    /// <param name="entry">The field definition.</param>
    public static bool IsMultiValued(DataExtractionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.AllowMultipleValues || entry.FieldType == DataExtractionFieldType.List;
    }

    /// <summary>
    /// Normalizes an extracted value of a multi-valued field into its items. A list field accepts one string holding
    /// several items separated by commas, semicolons or line breaks, since a model does not always return an array;
    /// any other multi-valued field takes the value as one item.
    /// </summary>
    /// <param name="entry">The field definition.</param>
    /// <param name="value">The extracted value.</param>
    /// <returns>The valid, distinct items in the order given; empty when none is valid.</returns>
    public static IReadOnlyList<string> NormalizeItems(DataExtractionEntry entry, string value)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var parts = entry.FieldType == DataExtractionFieldType.List
            ? value.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [value];
        var items = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            if (TryNormalize(entry, part, out var item) && !items.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                items.Add(item);
            }
        }

        return items;
    }

    /// <summary>
    /// Normalizes a value for the field.
    /// </summary>
    /// <param name="entry">The field definition.</param>
    /// <param name="value">The extracted value.</param>
    /// <param name="normalized">The stored form: an invariant number, <c>true</c>/<c>false</c>, a <c>yyyy-MM-dd</c> date, or the allowed value's configured spelling.</param>
    /// <returns><see langword="false"/> when the value is not valid for the field, in which case it must not be stored.</returns>
    public static bool TryNormalize(DataExtractionEntry entry, string value, out string normalized)
    {
        ArgumentNullException.ThrowIfNull(entry);

        normalized = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        switch (entry.FieldType)
        {
            case DataExtractionFieldType.Number:
                return TryNumber(trimmed, out normalized);

            case DataExtractionFieldType.Boolean:
                if (_trueValues.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    normalized = "true";
                }
                else if (_falseValues.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    normalized = "false";
                }

                return normalized is not null;

            case DataExtractionFieldType.Date:
                if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
                {
                    normalized = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }

                return normalized is not null;

            case DataExtractionFieldType.List:
            case DataExtractionFieldType.Enum:
                // An enum with nothing to choose from cannot be validated; keep the value rather than lose it. A list
                // is free text unless it names the items it may hold.
                if (entry.AllowedValues is not { Count: > 0 } allowed)
                {
                    normalized = trimmed;

                    return true;
                }

                normalized = allowed.FirstOrDefault(option => string.Equals(option?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))?.Trim();

                return normalized is not null;

            default:
                normalized = value;

                return true;
        }
    }

    private static bool TryNumber(string value, out string normalized)
    {
        // Spoken and typed numbers arrive with currency symbols, separators and units attached ("$1,200.50", "15%").
        var cleaned = value.Replace(",", string.Empty).Replace("$", string.Empty).Replace("%", string.Empty).Trim();

        if (decimal.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            normalized = number.ToString(CultureInfo.InvariantCulture);

            return true;
        }

        normalized = null;

        return false;
    }
}
