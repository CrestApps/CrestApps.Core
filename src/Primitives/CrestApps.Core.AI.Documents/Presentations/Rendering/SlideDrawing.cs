namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// A slide drawn as a list of shapes, text and pictures, in points, ready to be painted by any surface: the
/// SVG preview or a PDF page.
/// </summary>
public sealed class SlideDrawing
{
    /// <summary>
    /// Gets or sets the slide's width in points.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the slide's height in points.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Gets or sets the slide's number.
    /// </summary>
    public int SlideNumber { get; set; }

    /// <summary>
    /// Gets or sets the slide's title, for the picture's accessible name.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the items, back to front.
    /// </summary>
    public IList<SlideDrawingItem> Items { get; set; } = [];

    /// <summary>
    /// Gets or sets short descriptions of content that could only be drawn as a labelled placeholder, such
    /// as a SmartArt graphic, so the caller can say what the picture leaves out.
    /// </summary>
    public IList<string> Placeholders { get; set; } = [];

    /// <summary>
    /// Gets or sets the number of picture bytes left out to keep the drawing within its size limit.
    /// </summary>
    public long OmittedImageBytes { get; set; }
}
