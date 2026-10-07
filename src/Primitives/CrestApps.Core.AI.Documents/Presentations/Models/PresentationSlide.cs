namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// One slide of a deck, as the reader sees it.
/// </summary>
public sealed class PresentationSlide
{
    /// <summary>
    /// Gets or sets the slide's position in the deck, counting from 1.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Gets or sets the slide's identifier, which stays the same when slides are added, removed or moved.
    /// </summary>
    public uint SlideId { get; set; }

    /// <summary>
    /// Gets or sets the name of the layout the slide is built on.
    /// </summary>
    public string LayoutName { get; set; }

    /// <summary>
    /// Gets or sets the type of the layout the slide is built on, such as <c>title</c> or <c>obj</c>.
    /// </summary>
    public string LayoutType { get; set; }

    /// <summary>
    /// Gets or sets the name of the slide master the layout belongs to.
    /// </summary>
    public string MasterName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the slide is hidden during a slide show.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Gets or sets the name of the section the slide belongs to, when the deck has sections.
    /// </summary>
    public string SectionName { get; set; }

    /// <summary>
    /// Gets or sets the slide's background, resolved from the slide, its layout and its master.
    /// </summary>
    public PresentationFill Background { get; set; } = PresentationFill.Solid("FFFFFF");

    /// <summary>
    /// Gets or sets the elements that belong to the slide, in drawing order from back to front.
    /// </summary>
    public IList<PresentationElement> Elements { get; set; } = [];

    /// <summary>
    /// Gets or sets the decorative elements the slide shows from its layout and master, such as a logo or a
    /// coloured band, drawn behind the slide's own elements.
    /// </summary>
    public IList<PresentationElement> InheritedElements { get; set; } = [];

    /// <summary>
    /// Gets or sets the speaker notes.
    /// </summary>
    public string Notes { get; set; }

    /// <summary>
    /// Gets or sets the transition into the slide, such as <c>fade</c> or <c>push</c>, when it has one.
    /// </summary>
    public string Transition { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether any element on the slide is animated.
    /// </summary>
    public bool HasAnimations { get; set; }

    /// <summary>
    /// Gets the text of the slide's title placeholder, or <see langword="null"/> when it has none.
    /// </summary>
    public string Title
    {
        get
        {
            foreach (var element in Elements)
            {
                if (element.IsTitle && element.Text?.HasText == true)
                {
                    return element.Text.PlainText.Replace('\n', ' ').Trim();
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Walks every element on the slide, including those inside groups.
    /// </summary>
    /// <returns>The elements in drawing order.</returns>
    public IEnumerable<PresentationElement> AllElements()
    {
        return Elements.SelectMany(element => element.DescendantsAndSelf());
    }
}
