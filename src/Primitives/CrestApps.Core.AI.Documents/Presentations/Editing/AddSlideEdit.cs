namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Adds a slide built on one of the deck's layouts and fills its placeholders.
/// </summary>
public sealed class AddSlideEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets where the new slide goes, counting from 1. The slide is added at the end when this is
    /// not set.
    /// </summary>
    public int? Position { get; set; }

    /// <summary>
    /// Gets or sets the layout to build the slide on, by name (<c>Title and Content</c>) or by kind
    /// (<c>title</c>, <c>title_and_content</c>, <c>section_header</c>, <c>two_content</c>,
    /// <c>comparison</c>, <c>title_only</c>, <c>blank</c>, <c>content_with_caption</c>,
    /// <c>picture_with_caption</c>). A slide with no layout is given <c>title_and_content</c>, or
    /// <c>title</c> when it is the first slide of the deck.
    /// </summary>
    public string Layout { get; set; }

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the subtitle, for a title slide.
    /// </summary>
    public string Subtitle { get; set; }

    /// <summary>
    /// Gets or sets the body text, or the left column of a two-column layout.
    /// </summary>
    public IList<PresentationParagraphSpec> Body { get; set; }

    /// <summary>
    /// Gets or sets the right column of a two-column or comparison layout.
    /// </summary>
    public IList<PresentationParagraphSpec> SecondBody { get; set; }

    /// <summary>
    /// Gets or sets the heading above the left column of a comparison layout.
    /// </summary>
    public string FirstHeading { get; set; }

    /// <summary>
    /// Gets or sets the heading above the right column of a comparison layout.
    /// </summary>
    public string SecondHeading { get; set; }

    /// <summary>
    /// Gets or sets the speaker notes.
    /// </summary>
    public string Notes { get; set; }

    /// <summary>
    /// Gets or sets further elements to place on the slide.
    /// </summary>
    public IList<PresentationElementSpec> Elements { get; set; } = [];

    /// <summary>
    /// Gets or sets the slide's own background, when it should differ from the layout's.
    /// </summary>
    public PresentationBackgroundSpec Background { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the slide is hidden during a slide show.
    /// </summary>
    public bool Hidden { get; set; }
}
