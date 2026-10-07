namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Applies styling to existing content: every slide, some slides, or one element.
/// </summary>
/// <remarks>
/// Formatting the whole deck also writes the text styles onto the slide masters, so slides added afterwards
/// inherit them from the file itself and not only from the style the workspace remembers.
/// </remarks>
public sealed class FormatEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the slides to format. Every slide is formatted when the list is empty and no element is
    /// named.
    /// </summary>
    public IList<int> Slides { get; set; } = [];

    /// <summary>
    /// Gets or sets a single element to format, on the single slide in <see cref="Slides"/>.
    /// </summary>
    public string Element { get; set; }

    /// <summary>
    /// Gets or sets the formatting to apply.
    /// </summary>
    public PresentationFormatting Formatting { get; set; } = new();
}
