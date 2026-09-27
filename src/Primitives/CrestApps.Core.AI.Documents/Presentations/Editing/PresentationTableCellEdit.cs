namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// A change to one cell of a slide table.
/// </summary>
public sealed class PresentationTableCellEdit
{
    /// <summary>
    /// Gets or sets the row, counting from 1 at the top (the header row, when there is one, is row 1).
    /// </summary>
    public int Row { get; set; }

    /// <summary>
    /// Gets or sets the column, counting from 1 at the left.
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Gets or sets the new text, or <see langword="null"/> to keep the text and change only its style.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets how the cell's text looks.
    /// </summary>
    public PresentationTextStyle TextStyle { get; set; }

    /// <summary>
    /// Gets or sets the cell's background colour, or <c>none</c>.
    /// </summary>
    public string Fill { get; set; }

    /// <summary>
    /// Gets or sets how many columns the cell spans, to merge it with its neighbours to the right.
    /// </summary>
    public int? ColumnSpan { get; set; }

    /// <summary>
    /// Gets or sets how many rows the cell spans, to merge it with the cells below.
    /// </summary>
    public int? RowSpan { get; set; }
}
