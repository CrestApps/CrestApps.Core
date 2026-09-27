namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// A section to define: a name and the slide it starts at.
/// </summary>
public sealed class PresentationSectionSpec
{
    /// <summary>
    /// Gets or sets the section's name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the number of the section's first slide. A section runs until the next one starts.
    /// </summary>
    public int FirstSlide { get; set; }
}
