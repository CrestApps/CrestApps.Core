namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// A deck as its slides show it: every slide, element, colour and font resolved through the layout, master
/// and theme it inherits from.
/// </summary>
/// <remarks>
/// This is what the preview draws, what the outline and content tools describe, and what the quality checks
/// inspect. All of them read the same resolved model of the same package the export writes, which is what
/// keeps the picture in the chat from drifting away from the file the reader downloads.
/// </remarks>
public sealed class PresentationModel
{
    /// <summary>
    /// Gets or sets the slide width in EMUs.
    /// </summary>
    public long SlideWidth { get; set; } = PresentationUnits.WideSlideWidth;

    /// <summary>
    /// Gets or sets the slide height in EMUs.
    /// </summary>
    public long SlideHeight { get; set; } = PresentationUnits.WideSlideHeight;

    /// <summary>
    /// Gets or sets the deck title recorded in its document properties.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the theme of the first slide master.
    /// </summary>
    public PresentationTheme Theme { get; set; } = new();

    /// <summary>
    /// Gets or sets the slide masters and their layouts.
    /// </summary>
    public IList<PresentationMaster> Masters { get; set; } = [];

    /// <summary>
    /// Gets or sets the sections, when the deck has any.
    /// </summary>
    public IList<PresentationSection> Sections { get; set; } = [];

    /// <summary>
    /// Gets or sets the slides, in order.
    /// </summary>
    public IList<PresentationSlide> Slides { get; set; } = [];

    /// <summary>
    /// Gets a description of the slide size, such as <c>16:9 (960 x 540 pt)</c>.
    /// </summary>
    public string SlideSizeDescription
    {
        get
        {
            var ratio = SlideHeight == 0 ? 0 : SlideWidth / (double)SlideHeight;
            var name = ratio switch
            {
                > 1.76 and < 1.79 => "16:9 ",
                > 1.59 and < 1.61 => "16:10 ",
                > 1.32 and < 1.34 => "4:3 ",
                > 1.40 and < 1.42 => "A4 ",
                _ => string.Empty,
            };

            return $"{name}({PresentationUnits.FormatPoints(SlideWidth)} x {PresentationUnits.FormatPoints(SlideHeight)} pt)";
        }
    }

    /// <summary>
    /// Finds a slide by its number.
    /// </summary>
    /// <param name="number">The slide number, counting from 1.</param>
    /// <returns>The slide, or <see langword="null"/> when the deck has no such slide.</returns>
    public PresentationSlide FindSlide(int number)
    {
        return number >= 1 && number <= Slides.Count ? Slides[number - 1] : null;
    }
}
