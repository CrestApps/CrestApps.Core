namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// A page of a <see cref="PdfDocument"/>.
/// </summary>
/// <remarks>
/// A page read from a file holds every entry it inherited in the page tree, so its boxes, rotation and
/// resources can be read and changed on the page itself.
/// </remarks>
internal sealed class PdfPage : PdfDictionary
{
    /// <summary>
    /// The width of an A4 page, in points.
    /// </summary>
    public const double A4Width = 595.276;

    /// <summary>
    /// The height of an A4 page, in points.
    /// </summary>
    public const double A4Height = 841.89;

    // A page without a usable media box is shown at Letter size by viewers.
    private static readonly PdfRectangle _letter = new(0, 0, 612, 792);

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfPage"/> class: an empty A4 page.
    /// </summary>
    /// <param name="owner">The document the page belongs to.</param>
    public PdfPage(PdfDocument owner)
        : base(owner)
    {
        Elements.SetName("/Type", "/Page");
        Elements["/MediaBox"] = new PdfRectangle(0, 0, A4Width, A4Height);
        Elements["/Resources"] = new PdfDictionary(owner);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfPage"/> class with the entries of a page read from a file.
    /// </summary>
    /// <param name="owner">The document the page belongs to.</param>
    /// <param name="source">The page dictionary as read.</param>
    internal PdfPage(PdfDocument owner, PdfDictionary source)
        : base(owner)
    {
        foreach (var (key, value) in source.Elements)
        {
            Elements[key] = value;
        }

        if (source.Stream is not null)
        {
            CreateStream(source.Stream.Value);
        }

        Elements.SetName("/Type", "/Page");
    }

    /// <summary>
    /// Gets or sets the media box: the size of the page.
    /// </summary>
    public PdfRectangle MediaBox
    {
        get => PdfRectangle.From(Elements["/MediaBox"]) ?? _letter;
        set => Elements["/MediaBox"] = value;
    }

    /// <summary>
    /// Gets a value indicating whether the page has a crop box of its own.
    /// </summary>
    public bool HasCropBox => PdfRectangle.From(Elements["/CropBox"]) is not null;

    /// <summary>
    /// Gets or sets the crop box: the part of the page a viewer shows. Without one, it is the media box.
    /// </summary>
    public PdfRectangle CropBox
    {
        get => PdfRectangle.From(Elements["/CropBox"]) ?? MediaBox;
        set => Elements["/CropBox"] = value;
    }

    /// <summary>
    /// Gets or sets the width of the media box, in points.
    /// </summary>
    public double Width
    {
        get => MediaBox.Width;
        set => MediaBox = new PdfRectangle(MediaBox.X1, MediaBox.Y1, MediaBox.X1 + value, MediaBox.Y2);
    }

    /// <summary>
    /// Gets or sets the height of the media box, in points.
    /// </summary>
    public double Height
    {
        get => MediaBox.Height;
        set => MediaBox = new PdfRectangle(MediaBox.X1, MediaBox.Y1, MediaBox.X2, MediaBox.Y1 + value);
    }

    /// <summary>
    /// Gets or sets the clockwise rotation a viewer shows the page at: 0, 90, 180 or 270.
    /// </summary>
    public int Rotate
    {
        get => ((Elements.GetInteger("/Rotate") % 360) + 360) % 360 / 90 * 90;
        set => Elements.SetInteger("/Rotate", ((value % 360) + 360) % 360 / 90 * 90);
    }

    /// <summary>
    /// Gets the page's resources, creating them when the page has none.
    /// </summary>
    public PdfDictionary Resources
    {
        get
        {
            if (Elements.GetDictionary("/Resources") is { } resources)
            {
                return resources;
            }

            var created = new PdfDictionary(Owner);

            Elements["/Resources"] = created;

            return created;
        }
    }

    /// <summary>
    /// Gets the page's content streams.
    /// </summary>
    public PdfPageContents Contents => new(this);

    /// <summary>
    /// Gets the page's annotations, or <see langword="null"/> when it has none.
    /// </summary>
    public PdfArray Annotations => Elements.GetArray("/Annots");
}
