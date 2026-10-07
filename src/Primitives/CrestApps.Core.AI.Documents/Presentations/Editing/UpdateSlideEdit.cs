namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Changes a slide's title, body, notes or visibility. Properties left unset are not touched.
/// </summary>
public sealed class UpdateSlideEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the number of the slide to change.
    /// </summary>
    public int Slide { get; set; }

    /// <summary>
    /// Gets or sets the new title. An empty string clears it.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the new subtitle. An empty string clears it.
    /// </summary>
    public string Subtitle { get; set; }

    /// <summary>
    /// Gets or sets the new body text, or the left column of a two-column layout.
    /// </summary>
    public IList<PresentationParagraphSpec> Body { get; set; }

    /// <summary>
    /// Gets or sets the new right column of a two-column or comparison layout.
    /// </summary>
    public IList<PresentationParagraphSpec> SecondBody { get; set; }

    /// <summary>
    /// Gets or sets the new speaker notes. An empty string clears them.
    /// </summary>
    public string Notes { get; set; }

    /// <summary>
    /// Gets or sets whether the slide is hidden during a slide show.
    /// </summary>
    public bool? Hidden { get; set; }

    /// <summary>
    /// Gets or sets the transition into the slide: <c>none</c>, <c>fade</c>, <c>push</c>, <c>wipe</c>,
    /// <c>split</c>, <c>cover</c>, <c>cut</c> or <c>zoom</c>.
    /// </summary>
    public string Transition { get; set; }
}
