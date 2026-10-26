namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A named paragraph style, based on another or on none.
/// </summary>
internal sealed class PdfLayoutStyle
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutStyle"/> class.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="baseStyle">The name of the style it is based on, or <see langword="null"/>.</param>
    public PdfLayoutStyle(string name, string baseStyle)
    {
        Name = name;
        BaseStyle = baseStyle;
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the name of the style it is based on, or <see langword="null"/>.
    /// </summary>
    public string BaseStyle { get; }

    /// <summary>
    /// Gets the format the style gives a paragraph.
    /// </summary>
    public PdfLayoutParagraphFormat ParagraphFormat { get; } = new();

    /// <summary>
    /// Gets the style's font.
    /// </summary>
    public PdfLayoutFont Font => ParagraphFormat.Font;
}
