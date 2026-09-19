namespace CrestApps.Core.AI.Documents.Generation.RichText;

/// <summary>
/// One block of a parsed document.
/// </summary>
public sealed class RichTextBlock
{
    /// <summary>
    /// Gets the block kind.
    /// </summary>
    public RichTextBlockKind Kind { get; init; }

    /// <summary>
    /// Gets the heading depth, 1 through 6, for a heading block.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets the position of a numbered list item.
    /// </summary>
    public int Number { get; init; }

    /// <summary>
    /// Gets the formatted runs that make up the block.
    /// </summary>
    public IList<RichTextSpan> Spans { get; init; } = [];

    /// <summary>
    /// Gets the verbatim content of a code block.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    /// Gets the table content of a table block.
    /// </summary>
    public RichTextTable Table { get; init; }
}
