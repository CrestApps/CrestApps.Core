namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// How much of a deck to read.
/// </summary>
public sealed class PresentationReadOptions
{
    /// <summary>
    /// Gets the options that read structure and text only, without loading any picture.
    /// </summary>
    public static PresentationReadOptions TextOnly => new() { IncludeImageData = false };

    /// <summary>
    /// Gets or sets a value indicating whether pictures are loaded, so they can be drawn.
    /// </summary>
    public bool IncludeImageData { get; set; } = true;

    /// <summary>
    /// Gets or sets the largest picture, in bytes, that is loaded. Larger pictures are described but not
    /// loaded.
    /// </summary>
    public long MaxImageBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the numbers of the slides to read in full. Every slide is read when this is empty; the
    /// others are still listed, with their titles, but without their elements.
    /// </summary>
    public ISet<int> Slides { get; set; } = new HashSet<int>();
}
