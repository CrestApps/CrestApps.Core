using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// One picture placed on a page, as the file stores it.
/// </summary>
internal sealed class PdfImageEntry
{
    /// <summary>
    /// Gets the one-based page the picture is on.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Gets the picture's one-based position among the pictures of its page.
    /// </summary>
    public int IndexOnPage { get; init; }

    /// <summary>
    /// Gets or sets the picture's one-based position among every picture listed.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets where the picture is drawn, in user space.
    /// </summary>
    public PdfBox Box { get; init; }

    /// <summary>
    /// Gets the width in pixels.
    /// </summary>
    public int PixelWidth { get; init; }

    /// <summary>
    /// Gets the height in pixels.
    /// </summary>
    public int PixelHeight { get; init; }

    /// <summary>
    /// Gets the bits per colour component.
    /// </summary>
    public int BitsPerComponent { get; init; }

    /// <summary>
    /// Gets the colour space's name, when it could be read.
    /// </summary>
    public string ColorSpace { get; init; }

    /// <summary>
    /// Gets how the picture is stored: <c>jpeg</c>, <c>png</c> (raw pixels, exported as PNG), <c>jpeg2000</c>,
    /// <c>jbig2</c> or <c>ccitt</c>.
    /// </summary>
    public string Format { get; init; }

    /// <summary>
    /// Gets the stored size in bytes.
    /// </summary>
    public int ByteLength { get; init; }

    /// <summary>
    /// Gets a value indicating whether the picture is written inline in the page's content.
    /// </summary>
    public bool IsInline { get; init; }

    /// <summary>
    /// Gets a value indicating whether the picture is a one-bit stencil mask.
    /// </summary>
    public bool IsMask { get; init; }

    /// <summary>
    /// Gets a fingerprint of the stored bytes, the same for every placement of one picture.
    /// </summary>
    public string Hash { get; init; }

    /// <summary>
    /// Gets or sets the index of the first listed picture this one repeats, or <see langword="null"/>.
    /// </summary>
    public int? DuplicateOf { get; set; }

    /// <summary>
    /// Gets the picture itself, readable while its document is open.
    /// </summary>
    public IPdfImage Image { get; init; }
}
