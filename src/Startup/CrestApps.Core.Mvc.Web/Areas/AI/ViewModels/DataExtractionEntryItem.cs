using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

public sealed class DataExtractionEntryItem
{
    private static readonly char[] _separators = [',', '\r', '\n', ';'];

    public string Name { get; set; }

    public string Description { get; set; }

    public bool AllowMultipleValues { get; set; }

    public bool IsUpdatable { get; set; }

    public DataExtractionFieldType FieldType { get; set; }

    /// <summary>
    /// Gets or sets the allowed values as one comma or line separated string. Only used by Enum and List fields.
    /// </summary>
    public string AllowedValues { get; set; }

    public static DataExtractionEntryItem FromEntry(DataExtractionEntry entry)
    {
        return new DataExtractionEntryItem
        {
            Name = entry.Name,
            Description = entry.Description,
            AllowMultipleValues = entry.AllowMultipleValues,
            IsUpdatable = entry.IsUpdatable,
            FieldType = entry.FieldType,
            AllowedValues = string.Join(Environment.NewLine, entry.AllowedValues ?? []),
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
            // Allowed values only mean something for these two types, so a switch back to Text drops them.
            AllowedValues = FieldType is DataExtractionFieldType.Enum or DataExtractionFieldType.List
                ? ParseAllowedValues(AllowedValues)
                : [],
        };
    }

    public static List<string> ParseAllowedValues(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(_separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
