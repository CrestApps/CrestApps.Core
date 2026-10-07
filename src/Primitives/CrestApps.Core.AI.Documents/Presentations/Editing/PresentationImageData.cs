namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// A picture to place in a deck, already loaded so the engine never reaches outside the call for it.
/// </summary>
public sealed class PresentationImageData
{
    /// <summary>
    /// Gets or sets the encoded picture.
    /// </summary>
    public byte[] Data { get; set; }

    /// <summary>
    /// Gets or sets the media type, for example <c>image/png</c>.
    /// </summary>
    public string ContentType { get; set; }

    /// <summary>
    /// Gets or sets the name the picture came under, used for its alternative text when none is given.
    /// </summary>
    public string FileName { get; set; }

    /// <summary>
    /// Gets or sets the natural width in pixels, when known.
    /// </summary>
    public int? PixelWidth { get; set; }

    /// <summary>
    /// Gets or sets the natural height in pixels, when known.
    /// </summary>
    public int? PixelHeight { get; set; }
}
