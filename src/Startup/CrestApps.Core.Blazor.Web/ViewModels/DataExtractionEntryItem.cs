using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class DataExtractionEntryItem
{
    private static readonly char[] _valueSeparators = [',', '\n', '\r'];

    public string Name { get; set; }

    public string Description { get; set; }

    public bool AllowMultipleValues { get; set; }

    public bool IsUpdatable { get; set; }

    public DataExtractionFieldType FieldType { get; set; }

    /// <summary>
    /// Gets or sets the allowed values as a comma or newline separated list. Only meaningful for an
    /// <see cref="DataExtractionFieldType.Enum"/> or <see cref="DataExtractionFieldType.List"/> field.
    /// </summary>
    public string AllowedValuesText { get; set; }

    public bool UsesAllowedValues => FieldType is DataExtractionFieldType.Enum or DataExtractionFieldType.List;

    public static DataExtractionEntryItem FromEntry(DataExtractionEntry entry)
    {
        return new DataExtractionEntryItem
        {
            Name = entry.Name,
            Description = entry.Description,
            AllowMultipleValues = entry.AllowMultipleValues,
            IsUpdatable = entry.IsUpdatable,
            FieldType = entry.FieldType,
            AllowedValuesText = string.Join(Environment.NewLine, entry.AllowedValues ?? []),
        };
    }

    public DataExtractionEntry ToEntry()
    {
        return new DataExtractionEntry
        {
            Name = Name,
            Description = Description,
            AllowMultipleValues = AllowMultipleValues,
            IsUpdatable = IsUpdatable,
            FieldType = FieldType,
            AllowedValues = UsesAllowedValues
                ? ParseAllowedValues(AllowedValuesText)
                : [],
        };
    }

    private static List<string> ParseAllowedValues(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text.Split(_valueSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
