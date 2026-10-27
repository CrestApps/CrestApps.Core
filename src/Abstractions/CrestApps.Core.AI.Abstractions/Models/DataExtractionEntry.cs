namespace CrestApps.Core.AI.Models;

/// <summary>
/// Defines a single AI data-extraction field configuration, specifying what information to extract from a chat session.
/// </summary>
public sealed class DataExtractionEntry
{
    /// <summary>
    /// Gets or sets the unique key for this extraction entry.
    /// Must be alphanumeric with underscores only.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a description of what to extract.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets whether to keep extracting values across messages.
    /// </summary>
    public bool AllowMultipleValues { get; set; }

    /// <summary>
    /// Gets or sets whether to allow replacing a single-value field.
    /// </summary>
    public bool IsUpdatable { get; set; }

    /// <summary>
    /// Gets or sets the kind of value this field collects. Defaults to <see cref="DataExtractionFieldType.Text"/>,
    /// which stores values as extracted.
    /// </summary>
    public DataExtractionFieldType FieldType { get; set; }

    /// <summary>
    /// Gets or sets the values an <see cref="DataExtractionFieldType.Enum"/> field may take. Ignored for other types.
    /// </summary>
    public List<string> AllowedValues { get; set; } = [];
}
