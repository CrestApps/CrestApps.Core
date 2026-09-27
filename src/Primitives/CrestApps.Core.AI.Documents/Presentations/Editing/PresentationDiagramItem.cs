namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// One step, stage, node or quadrant of a diagram.
/// </summary>
public sealed class PresentationDiagramItem
{
    /// <summary>
    /// Gets or sets the item's label.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets a line of detail shown under the label.
    /// </summary>
    public string Detail { get; set; }

    /// <summary>
    /// Gets or sets, for a hierarchy, the label of the item above this one.
    /// </summary>
    public string Parent { get; set; }

    /// <summary>
    /// Gets or sets a colour for this item instead of the palette's.
    /// </summary>
    public string Color { get; set; }
}
