namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Adds, changes or removes the footer text, slide number and date shown at the bottom of slides.
/// </summary>
public sealed class SetFooterEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the footer text. An empty string removes the footer; <see langword="null"/> leaves it.
    /// </summary>
    public string FooterText { get; set; }

    /// <summary>
    /// Gets or sets whether slides show their number.
    /// </summary>
    public bool? SlideNumbers { get; set; }

    /// <summary>
    /// Gets or sets whether slides show a date.
    /// </summary>
    public bool? Date { get; set; }

    /// <summary>
    /// Gets or sets a fixed date text. The date updates itself when this is not set.
    /// </summary>
    public string DateText { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether title slides are left without a footer, as PowerPoint's own
    /// "Don't show on title slide" does.
    /// </summary>
    public bool SkipTitleSlides { get; set; } = true;

    /// <summary>
    /// Gets or sets the slides to change. Every slide is changed when the list is empty.
    /// </summary>
    public IList<int> Slides { get; set; } = [];
}
