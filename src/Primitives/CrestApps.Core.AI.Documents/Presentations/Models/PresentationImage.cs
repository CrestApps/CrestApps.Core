namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// A picture held by the deck, as a picture element, a picture fill or a background.
/// </summary>
public sealed class PresentationImage
{
    /// <summary>
    /// Gets or sets the media type, for example <c>image/png</c>.
    /// </summary>
    public string ContentType { get; set; }

    /// <summary>
    /// Gets or sets the encoded bytes, or <see langword="null"/> when they were not loaded because the
    /// caller did not ask for them or they were larger than the limit it set.
    /// </summary>
    public byte[] Data { get; set; }

    /// <summary>
    /// Gets or sets the size of the encoded picture in bytes, known even when <see cref="Data"/> was not
    /// loaded.
    /// </summary>
    public long ByteLength { get; set; }

    /// <summary>
    /// Gets or sets the natural width of the picture in pixels, when its header could be read.
    /// </summary>
    public int? PixelWidth { get; set; }

    /// <summary>
    /// Gets or sets the natural height of the picture in pixels, when its header could be read.
    /// </summary>
    public int? PixelHeight { get; set; }

    /// <summary>
    /// Gets or sets the fraction cut off the left edge, from 0 to 1.
    /// </summary>
    public double CropLeft { get; set; }

    /// <summary>
    /// Gets or sets the fraction cut off the top edge, from 0 to 1.
    /// </summary>
    public double CropTop { get; set; }

    /// <summary>
    /// Gets or sets the fraction cut off the right edge, from 0 to 1.
    /// </summary>
    public double CropRight { get; set; }

    /// <summary>
    /// Gets or sets the fraction cut off the bottom edge, from 0 to 1.
    /// </summary>
    public double CropBottom { get; set; }

    /// <summary>
    /// Gets or sets the address of a picture the deck links to rather than holds.
    /// </summary>
    public string LinkedUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the relationship that should hold the picture is missing.
    /// </summary>
    public bool IsMissing { get; set; }

    /// <summary>
    /// Gets or sets the opacity the picture is drawn with, from 0 (transparent) to 1 (opaque).
    /// </summary>
    public double Alpha { get; set; } = 1;

    /// <summary>
    /// Gets a value indicating whether a browser can draw the picture as it is stored.
    /// </summary>
    public bool IsBrowserDisplayable => ContentType is "image/png" or "image/jpeg" or "image/gif" or "image/bmp" or "image/webp" or "image/svg+xml";
}
