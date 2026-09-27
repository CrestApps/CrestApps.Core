namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// A slide layout: the arrangement of placeholders a slide can be built on.
/// </summary>
public sealed class PresentationLayout
{
    /// <summary>
    /// Gets or sets the layout's name, such as <c>Title and Content</c>.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the layout type, such as <c>title</c>, <c>obj</c>, <c>secHead</c> or <c>blank</c>, or
    /// <c>cust</c> for a layout the author designed.
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the placeholders the layout offers.
    /// </summary>
    public IList<PresentationPlaceholder> Placeholders { get; set; } = [];

    /// <summary>
    /// Gets or sets the number of slides built on the layout.
    /// </summary>
    public int SlideCount { get; set; }
}
