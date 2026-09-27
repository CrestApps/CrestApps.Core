namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Places a logo on the slide masters, so every slide shows it, or on chosen slides.
/// </summary>
public sealed class AddLogoEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the logo picture.
    /// </summary>
    public PresentationImageData Logo { get; set; }

    /// <summary>
    /// Gets or sets the corner the logo sits in: <c>top_left</c>, <c>top_right</c>, <c>bottom_left</c> or
    /// <c>bottom_right</c>.
    /// </summary>
    public string Position { get; set; } = "top_right";

    /// <summary>
    /// Gets or sets the logo width. The height follows from the picture's proportions.
    /// </summary>
    public PresentationLength? Width { get; set; }

    /// <summary>
    /// Gets or sets the slides to place the logo on. The logo goes on the slide masters when the list is
    /// empty, replacing a logo placed there before.
    /// </summary>
    public IList<int> Slides { get; set; } = [];
}
