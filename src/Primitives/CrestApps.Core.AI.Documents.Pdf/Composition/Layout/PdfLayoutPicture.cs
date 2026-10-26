using CrestApps.Core.AI.Documents.Pdf.Graphics.Images;

namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A picture set in a line of text, its bottom on the baseline.
/// </summary>
internal sealed class PdfLayoutPicture : PdfLayoutInline
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutPicture"/> class.
    /// </summary>
    /// <param name="image">The picture.</param>
    /// <param name="width">The width, in points.</param>
    /// <param name="height">The height, in points.</param>
    public PdfLayoutPicture(PdfImage image, double width, double height)
    {
        Image = image;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets the picture.
    /// </summary>
    public PdfImage Image { get; }

    /// <summary>
    /// Gets the width, in points.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the height, in points.
    /// </summary>
    public double Height { get; }
}
