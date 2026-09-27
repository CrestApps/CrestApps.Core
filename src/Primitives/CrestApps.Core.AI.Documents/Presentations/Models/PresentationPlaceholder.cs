namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// A placeholder a layout offers the slides built on it.
/// </summary>
public sealed class PresentationPlaceholder
{
    /// <summary>
    /// Gets or sets the placeholder type, such as <c>title</c>, <c>body</c> or <c>pic</c>.
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the placeholder index that ties slide content to it.
    /// </summary>
    public uint? Index { get; set; }

    /// <summary>
    /// Gets or sets the placeholder's name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets where the placeholder sits.
    /// </summary>
    public PresentationBounds Bounds { get; set; }
}
