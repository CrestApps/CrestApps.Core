namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// A named section grouping consecutive slides.
/// </summary>
public sealed class PresentationSection
{
    /// <summary>
    /// Gets or sets the section's name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the numbers of the slides in the section, in order.
    /// </summary>
    public IList<int> SlideNumbers { get; set; } = [];
}
