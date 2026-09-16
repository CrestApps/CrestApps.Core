namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// A named range defined for the workbook, so a region can be referenced by name from a formula or
/// selected from the name box.
/// </summary>
public sealed class SpreadsheetNamedRange
{
    /// <summary>
    /// Gets or sets the name. It must start with a letter or underscore and contain no spaces; an
    /// unusable name is skipped rather than written, because the workbook would not open.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the range in A1 notation (<c>A1:D20</c>). The worksheet is supplied by the writer,
    /// so the range does not need to name it.
    /// </summary>
    public string Range { get; set; }
}
