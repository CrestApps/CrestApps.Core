namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// Text laid out into lines inside its box.
/// </summary>
internal sealed class TextLayoutResult
{
    /// <summary>
    /// Gets or sets the lines, top to bottom.
    /// </summary>
    public IList<TextLayoutLine> Lines { get; set; } = [];

    /// <summary>
    /// Gets or sets the height the text needs, in points, including the box's top and bottom insets.
    /// </summary>
    public double RequiredHeight { get; set; }

    /// <summary>
    /// Gets or sets the width of the widest line plus the box's side insets, in points.
    /// </summary>
    public double RequiredWidth { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text needs more height than its box has.
    /// </summary>
    public bool Overflows { get; set; }

    /// <summary>
    /// Gets or sets the smallest font size drawn, in points.
    /// </summary>
    public double SmallestFontSize { get; set; }
}
