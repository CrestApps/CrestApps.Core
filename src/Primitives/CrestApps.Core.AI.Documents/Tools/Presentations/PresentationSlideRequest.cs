using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// A slide a tool call asked for, with the elements on it that still need content fetched.
/// </summary>
internal sealed class PresentationSlideRequest
{
    /// <summary>
    /// Gets or sets the slide as far as the arguments describe it.
    /// </summary>
    public AddSlideEdit Edit { get; set; }

    /// <summary>
    /// Gets or sets the elements to place on the slide.
    /// </summary>
    public IList<PresentationElementRequest> Elements { get; set; } = [];

    /// <summary>
    /// Gets or sets an uploaded picture to use as the slide's background.
    /// </summary>
    public string BackgroundImageDocument { get; set; }
}
