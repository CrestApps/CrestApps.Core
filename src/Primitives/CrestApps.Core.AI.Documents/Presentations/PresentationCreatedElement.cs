namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// An element an edit created, so the tool can tell the model how to address it next.
/// </summary>
public sealed class PresentationCreatedElement
{
    /// <summary>
    /// Gets or sets the number of the slide the element is on, after every edit in the batch.
    /// </summary>
    public int SlideNumber { get; set; }

    /// <summary>
    /// Gets or sets the identifier of that slide.
    /// </summary>
    public uint SlideId { get; set; }

    /// <summary>
    /// Gets or sets the element's identifier on its slide.
    /// </summary>
    public uint ElementId { get; set; }

    /// <summary>
    /// Gets or sets the element's name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the kind of element, such as <c>table</c> or <c>chart</c>.
    /// </summary>
    public string Kind { get; set; }
}
