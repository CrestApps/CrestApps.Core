namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Describes a table loaded into a tabular workspace, including its source document and schema.
/// </summary>
public sealed class TabularTableInfo
{
    /// <summary>
    /// Gets or sets the SQL table name.
    /// </summary>
    public string TableName { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the source document that produced this table.
    /// </summary>
    public string SourceDocumentId { get; set; }

    /// <summary>
    /// Gets or sets the original file name of the source document.
    /// </summary>
    public string SourceFileName { get; set; }

    /// <summary>
    /// Gets or sets the source worksheet name for multi-sheet workbooks. This is <see langword="null"/>
    /// for single-sheet sources such as CSV or TSV files that have no worksheet concept.
    /// </summary>
    public string WorksheetName { get; set; }

    /// <summary>
    /// Gets or sets the number of rows currently in the table.
    /// </summary>
    public long RowCount { get; set; }

    /// <summary>
    /// Gets or sets the columns of the table.
    /// </summary>
    public IReadOnlyList<TabularColumnInfo> Columns { get; set; } = [];
}
