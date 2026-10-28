namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// One side of a border. Giving it a width makes it visible.
/// </summary>
internal sealed class PdfLayoutBorder
{
    private double? _width;

    /// <summary>
    /// Gets or sets the width, in points.
    /// </summary>
    public double? Width
    {
        get => _width;
        set
        {
            _width = value;
            Visible ??= value > 0;
        }
    }

    /// <summary>
    /// Gets or sets the colour.
    /// </summary>
    public PdfColor? Color { get; set; }

    /// <summary>
    /// Gets or sets whether the border is drawn.
    /// </summary>
    public bool? Visible { get; set; }

    /// <summary>
    /// Returns this border with the values it leaves unset taken from another.
    /// </summary>
    /// <param name="fallback">The border the unset values come from.</param>
    /// <returns>The combined border.</returns>
    public PdfLayoutBorder Over(PdfLayoutBorder fallback)
    {
        if (fallback is null)
        {
            return this;
        }

        return new PdfLayoutBorder
        {
            _width = _width ?? fallback._width,
            Color = Color ?? fallback.Color,
            Visible = Visible ?? fallback.Visible,
        };
    }

    /// <summary>
    /// Gets a value indicating whether the border is drawn.
    /// </summary>
    /// <returns><see langword="true"/> when it is visible with a width.</returns>
    public bool IsDrawn()
    {
        return Visible == true && (_width ?? 0.5) > 0;
    }
}
