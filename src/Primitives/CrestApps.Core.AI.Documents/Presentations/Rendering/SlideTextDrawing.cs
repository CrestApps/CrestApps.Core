namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// A block of laid-out text on a drawn slide.
/// </summary>
public sealed class SlideTextDrawing : SlideDrawingItem
{
    /// <summary>
    /// Gets or sets the lines, already broken and positioned.
    /// </summary>
    public IList<SlideTextLine> Lines { get; set; } = [];
}
