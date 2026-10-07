namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// Limits applied while drawing a slide.
/// </summary>
internal sealed class SlideDrawingOptions
{
    /// <summary>
    /// Gets or sets the most picture bytes one slide's drawing may embed. Pictures past the limit are drawn
    /// as labelled placeholders.
    /// </summary>
    public long MaxImageBytes { get; set; } = 6 * 1024 * 1024;
}
