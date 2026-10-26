namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// The font text is set in. A value left unset is taken from the style, then from the paragraph around it.
/// </summary>
internal sealed class PdfLayoutFont
{
    /// <summary>
    /// Gets or sets the family, as <c>Arial</c>.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the size, in points.
    /// </summary>
    public double? Size { get; set; }

    /// <summary>
    /// Gets or sets whether the text is bold.
    /// </summary>
    public bool? Bold { get; set; }

    /// <summary>
    /// Gets or sets whether the text is italic.
    /// </summary>
    public bool? Italic { get; set; }

    /// <summary>
    /// Gets or sets whether the text is underlined.
    /// </summary>
    public bool? Underline { get; set; }

    /// <summary>
    /// Gets or sets the colour.
    /// </summary>
    public PdfColor? Color { get; set; }

    /// <summary>
    /// Returns this font with the values it leaves unset taken from another.
    /// </summary>
    /// <param name="fallback">The font the unset values come from.</param>
    /// <returns>The combined font.</returns>
    public PdfLayoutFont Over(PdfLayoutFont fallback)
    {
        if (fallback is null)
        {
            return this;
        }

        return new PdfLayoutFont
        {
            Name = Name ?? fallback.Name,
            Size = Size ?? fallback.Size,
            Bold = Bold ?? fallback.Bold,
            Italic = Italic ?? fallback.Italic,
            Underline = Underline ?? fallback.Underline,
            Color = Color ?? fallback.Color,
        };
    }
}
