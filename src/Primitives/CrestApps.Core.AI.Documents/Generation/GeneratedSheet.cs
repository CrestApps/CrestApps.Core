using CrestApps.Core.AI.Documents.Generation.Spreadsheets;

namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// One worksheet of a generated file. A workbook made from a multi-sheet upload, or from several
/// loaded tables, carries one of these per tab, each with its own presentation.
/// </summary>
public sealed class GeneratedSheet
{
    /// <summary>
    /// Gets or sets the worksheet name shown on the tab.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the header row.
    /// </summary>
    public IReadOnlyList<string> Header { get; set; } = [];

    /// <summary>
    /// Gets or sets the data rows.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; set; } = [];

    /// <summary>
    /// Gets or sets the presentation applied to this worksheet.
    /// </summary>
    public SpreadsheetFormatting Formatting { get; set; }

    /// <summary>
    /// Gets a value indicating whether the sheet carries a header that writers can render.
    /// </summary>
    public bool HasTable => Header is { Count: > 0 };
}
