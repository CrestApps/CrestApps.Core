namespace CrestApps.Core.AI.Documents.Generation.RichText;

/// <summary>
/// One row of a parsed table.
/// </summary>
public sealed class RichTextRow
{
    /// <summary>
    /// Gets the cells in the row.
    /// </summary>
    public IList<RichTextCell> Cells { get; init; } = [];
}
