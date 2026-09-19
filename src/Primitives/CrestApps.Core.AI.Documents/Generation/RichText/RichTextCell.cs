namespace CrestApps.Core.AI.Documents.Generation.RichText;

/// <summary>
/// One cell of a parsed table.
/// </summary>
public sealed class RichTextCell
{
    /// <summary>
    /// Gets the formatted runs that make up the cell.
    /// </summary>
    public IList<RichTextSpan> Spans { get; init; } = [];
}
