using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics.Images;

/// <summary>
/// A decoded picture: eight-bit grey or RGB samples, row by row from the top, with an optional alpha channel.
/// </summary>
internal sealed class PdfRasterImage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfRasterImage"/> class.
    /// </summary>
    /// <param name="width">The width, in pixels.</param>
    /// <param name="height">The height, in pixels.</param>
    /// <param name="components">The samples per pixel: 1 for grey, 3 for RGB.</param>
    /// <param name="samples">The samples.</param>
    /// <param name="alpha">One opacity sample per pixel, or <see langword="null"/> when the picture is opaque.</param>
    public PdfRasterImage(int width, int height, int components, byte[] samples, byte[] alpha)
    {
        Width = width;
        Height = height;
        Components = components;
        Samples = samples;

        // A channel that is opaque everywhere is left out, so the image needs no mask.
        Alpha = alpha is not null && alpha.Any(value => value != 255)
            ? alpha
            : null;
    }

    /// <summary>
    /// Gets the width, in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height, in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the samples per pixel: 1 for grey, 3 for RGB.
    /// </summary>
    public int Components { get; }

    /// <summary>
    /// Gets the samples.
    /// </summary>
    public byte[] Samples { get; }

    /// <summary>
    /// Gets one opacity sample per pixel, or <see langword="null"/> when the picture is opaque.
    /// </summary>
    public byte[] Alpha { get; }

    /// <summary>
    /// Creates the image object for a document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The image object, with its soft mask when the picture has transparency.</returns>
    public PdfDictionary ToXObject(PdfDocument document)
    {
        var colorSpace = Components == 1
            ? "/DeviceGray"
            : "/DeviceRGB";

        var image = Create(document, Samples, colorSpace);

        if (Alpha is not null)
        {
            image.Elements.SetReference("/SMask", Create(document, Alpha, "/DeviceGray"));
        }

        return image;
    }

    private PdfDictionary Create(PdfDocument document, byte[] samples, string colorSpace)
    {
        var image = new PdfDictionary(document);

        image.Elements.SetName("/Type", "/XObject");
        image.Elements.SetName("/Subtype", "/Image");
        image.Elements.SetInteger("/Width", Width);
        image.Elements.SetInteger("/Height", Height);
        image.Elements.SetInteger("/BitsPerComponent", 8);
        image.Elements.SetName("/ColorSpace", colorSpace);
        image.CreateStream([]).SetCompressed(samples);

        return image;
    }
}
