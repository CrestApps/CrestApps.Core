namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Where a table or chart block reads its data from in the conversation's tabular workspace — the uploaded
/// spreadsheets and CSV files, as the tabular data agent has loaded them.
/// </summary>
/// <remarks>
/// Only ever present on a block as it arrives from a tool call. The data is read once, when the block is
/// added, and the block keeps the rows it was given; so the document does not change under the reader when
/// the workspace is edited later, and rendering never needs the workspace.
/// </remarks>
internal sealed class PdfTabularSourceDefinition
{
    /// <summary>
    /// Gets or sets a read-only SQL query (SQLite) whose result is the data.
    /// </summary>
    public string Sql { get; set; }

    /// <summary>
    /// Gets or sets the name of a loaded table whose rows are the data, used when <see cref="Sql"/> is not set.
    /// </summary>
    public string TableName { get; set; }

    /// <summary>
    /// Gets or sets the most rows read.
    /// </summary>
    public int? MaxRows { get; set; }

    /// <summary>
    /// Gets or sets the column a chart takes its category labels from. Defaults to the first column.
    /// </summary>
    public string LabelColumn { get; set; }

    /// <summary>
    /// Gets or sets the columns a chart plots as series. Defaults to every numeric column but the labels.
    /// </summary>
    public List<string> ValueColumns { get; set; }
}
