namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// One cell of a slide table.
/// </summary>
public sealed class PresentationTableCell
{
    /// <summary>
    /// Gets or sets the text in the cell.
    /// </summary>
    public PresentationTextBody Text { get; set; } = new();

    /// <summary>
    /// Gets or sets the cell's background, resolved from the cell and, where the cell sets none, from the
    /// table style.
    /// </summary>
    public PresentationFill Fill { get; set; } = PresentationFill.None;

    /// <summary>
    /// Gets or sets the number of columns the cell spans.
    /// </summary>
    public int ColumnSpan { get; set; } = 1;

    /// <summary>
    /// Gets or sets the number of rows the cell spans.
    /// </summary>
    public int RowSpan { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether the cell is covered by a merged neighbour and is not drawn
    /// on its own.
    /// </summary>
    public bool IsMerged { get; set; }

    /// <summary>
    /// Gets or sets the left border.
    /// </summary>
    public PresentationLine BorderLeft { get; set; }

    /// <summary>
    /// Gets or sets the right border.
    /// </summary>
    public PresentationLine BorderRight { get; set; }

    /// <summary>
    /// Gets or sets the top border.
    /// </summary>
    public PresentationLine BorderTop { get; set; }

    /// <summary>
    /// Gets or sets the bottom border.
    /// </summary>
    public PresentationLine BorderBottom { get; set; }
}
