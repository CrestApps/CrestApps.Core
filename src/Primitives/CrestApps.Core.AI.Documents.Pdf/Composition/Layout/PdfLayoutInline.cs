namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// Something a paragraph's lines hold: text, a break, a tab, a picture, a field or a bookmark.
/// </summary>
internal abstract class PdfLayoutInline
{
    /// <summary>
    /// Gets the font this piece sets over the paragraph's.
    /// </summary>
    public PdfLayoutFont Font { get; } = new();

    /// <summary>
    /// Gets or sets where clicking the piece goes, or <see langword="null"/> when it is not a link.
    /// </summary>
    public PdfLayoutLink Link { get; set; }

    /// <summary>
    /// Gets or sets whether the piece is bold.
    /// </summary>
    public bool? Bold
    {
        get => Font.Bold;
        set => Font.Bold = value;
    }

    /// <summary>
    /// Gets or sets whether the piece is italic.
    /// </summary>
    public bool? Italic
    {
        get => Font.Italic;
        set => Font.Italic = value;
    }

    /// <summary>
    /// Gets or sets the piece's colour.
    /// </summary>
    public PdfColor? Color
    {
        get => Font.Color;
        set => Font.Color = value;
    }
}
