namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// The four sides of a paragraph's, a row's or a cell's border, and how far the paragraph's text keeps
/// from them.
/// </summary>
internal sealed class PdfLayoutBorders
{
    /// <summary>
    /// Gets or sets the top side.
    /// </summary>
    public PdfLayoutBorder Top { get; set; } = new();

    /// <summary>
    /// Gets or sets the bottom side.
    /// </summary>
    public PdfLayoutBorder Bottom { get; set; } = new();

    /// <summary>
    /// Gets or sets the left side.
    /// </summary>
    public PdfLayoutBorder Left { get; set; } = new();

    /// <summary>
    /// Gets or sets the right side.
    /// </summary>
    public PdfLayoutBorder Right { get; set; } = new();

    /// <summary>
    /// Gets or sets the distance between the text and the top border, in points.
    /// </summary>
    public double? DistanceFromTop { get; set; }

    /// <summary>
    /// Gets or sets the distance between the text and the bottom border, in points.
    /// </summary>
    public double? DistanceFromBottom { get; set; }

    /// <summary>
    /// Gets or sets the distance between the text and the left border, in points.
    /// </summary>
    public double? DistanceFromLeft { get; set; }

    /// <summary>
    /// Gets or sets every side's width at once, which makes them visible.
    /// </summary>
    public double? Width
    {
        get => Top.Width;
        set
        {
            Top.Width = value;
            Bottom.Width = value;
            Left.Width = value;
            Right.Width = value;
        }
    }

    /// <summary>
    /// Gets or sets every side's colour at once.
    /// </summary>
    public PdfColor? Color
    {
        get => Top.Color;
        set
        {
            Top.Color = value;
            Bottom.Color = value;
            Left.Color = value;
            Right.Color = value;
        }
    }

    /// <summary>
    /// Gets or sets whether every side is drawn.
    /// </summary>
    public bool? Visible
    {
        get => Top.Visible;
        set
        {
            Top.Visible = value;
            Bottom.Visible = value;
            Left.Visible = value;
            Right.Visible = value;
        }
    }

    /// <summary>
    /// Returns these borders with the values they leave unset taken from others.
    /// </summary>
    /// <param name="fallback">The borders the unset values come from.</param>
    /// <returns>The combined borders.</returns>
    public PdfLayoutBorders Over(PdfLayoutBorders fallback)
    {
        if (fallback is null)
        {
            return this;
        }

        return new PdfLayoutBorders
        {
            Top = Top.Over(fallback.Top),
            Bottom = Bottom.Over(fallback.Bottom),
            Left = Left.Over(fallback.Left),
            Right = Right.Over(fallback.Right),
            DistanceFromTop = DistanceFromTop ?? fallback.DistanceFromTop,
            DistanceFromBottom = DistanceFromBottom ?? fallback.DistanceFromBottom,
            DistanceFromLeft = DistanceFromLeft ?? fallback.DistanceFromLeft,
        };
    }
}
