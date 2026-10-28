using CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Images;

namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// One thing placed on a laid-out line: a word, a space, a picture or the dots a tab leaves.
/// </summary>
internal sealed class PdfLaidItem
{
    /// <summary>
    /// Gets or sets where the item starts, in points from the line's left edge.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the width, in points.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the text, or <see langword="null"/> for a picture.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item is a space: drawn as nothing, and widened when a line is justified.
    /// </summary>
    public bool IsSpace { get; set; }

    /// <summary>
    /// Gets or sets the font.
    /// </summary>
    public PdfFont Font { get; set; }

    /// <summary>
    /// Gets or sets the font size, in points.
    /// </summary>
    public double Size { get; set; }

    /// <summary>
    /// Gets or sets the colour.
    /// </summary>
    public PdfColor Color { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is underlined.
    /// </summary>
    public bool Underline { get; set; }

    /// <summary>
    /// Gets or sets where clicking the item goes, or <see langword="null"/>.
    /// </summary>
    public PdfLayoutLink Link { get; set; }

    /// <summary>
    /// Gets or sets the picture, for a picture item.
    /// </summary>
    public PdfImage Image { get; set; }

    /// <summary>
    /// Gets or sets the picture's height, in points.
    /// </summary>
    public double ImageHeight { get; set; }
}
