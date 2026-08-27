namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Represents a single parsed worksheet within a tabular document. Preserving the worksheet name lets
/// multi-sheet workbooks keep every sheet as an independent table instead of being flattened together.
/// </summary>
public sealed class TabularWorksheet
{
    /// <summary>
    /// Gets or sets the worksheet name as defined in the source workbook. This is <see langword="null"/>
    /// for single-sheet sources such as CSV or TSV files that have no worksheet concept.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the parsed header row for this worksheet.
    /// </summary>
    public List<string> Header { get; set; } = [];

    /// <summary>
    /// Gets or sets the parsed data rows for this worksheet.
    /// </summary>
    public List<List<string>> Rows { get; set; } = [];
}
