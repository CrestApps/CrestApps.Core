namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// A table to put on a slide.
/// </summary>
public sealed class PresentationTableSpec
{
    /// <summary>
    /// Gets or sets the cell text, row by row. When <see cref="HeaderRow"/> is set the first row is the
    /// header.
    /// </summary>
    public IList<IList<string>> Rows { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the first row is a header.
    /// </summary>
    public bool HeaderRow { get; set; } = true;

    /// <summary>
    /// Gets or sets the width of each column. Columns without one share what is left equally.
    /// </summary>
    public IList<PresentationLength?> ColumnWidths { get; set; } = [];

    /// <summary>
    /// Gets or sets the alignment of each column: <c>left</c>, <c>center</c> or <c>right</c>. A column
    /// without one is right-aligned when it holds numbers and left-aligned otherwise.
    /// </summary>
    public IList<string> ColumnAlignments { get; set; } = [];

    /// <summary>
    /// Gets or sets how the table looks, laid over the deck's recorded table style.
    /// </summary>
    public PresentationTableStyle Style { get; set; }
}
