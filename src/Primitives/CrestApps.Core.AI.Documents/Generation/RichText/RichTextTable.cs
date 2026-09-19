namespace CrestApps.Core.AI.Documents.Generation.RichText;

/// <summary>
/// A parsed table.
/// </summary>
public sealed class RichTextTable
{
    /// <summary>
    /// Gets the header row.
    /// </summary>
    public RichTextRow Header { get; init; } = new();

    /// <summary>
    /// Gets the data rows.
    /// </summary>
    public IList<RichTextRow> Rows { get; init; } = [];

    /// <summary>
    /// Gets the widest row in the table, which is how many columns a renderer must lay out.
    /// </summary>
    public int ColumnCount
    {
        get
        {
            var columns = Header.Cells.Count;

            foreach (var row in Rows)
            {
                columns = Math.Max(columns, row.Cells.Count);
            }

            return columns;
        }
    }
}
