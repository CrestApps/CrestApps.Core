namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <summary>
/// One laid-out page.
/// </summary>
internal sealed class WordLayoutPage
{
    /// <summary>
    /// Gets or sets the page's position in the document, from 1.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the page number as it is printed, which a section can restart or format as roman numerals.
    /// </summary>
    public string DisplayNumber { get; set; }

    /// <summary>
    /// Gets or sets the one-based section the page is in.
    /// </summary>
    public int Section { get; set; }

    /// <summary>
    /// Gets or sets the page width in points.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the page height in points.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Gets or sets the left edge of the text area.
    /// </summary>
    public double ContentLeft { get; set; }

    /// <summary>
    /// Gets or sets the right edge of the text area.
    /// </summary>
    public double ContentRight { get; set; }

    /// <summary>
    /// Gets or sets the top of the text area.
    /// </summary>
    public double ContentTop { get; set; }

    /// <summary>
    /// Gets or sets the bottom of the text area.
    /// </summary>
    public double ContentBottom { get; set; }

    /// <summary>
    /// Gets or sets the page's background color, or <see langword="null"/> for white.
    /// </summary>
    public string Background { get; set; }

    /// <summary>
    /// Gets the items drawn on the page, in drawing order.
    /// </summary>
    public List<WordDrawItem> Items { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the body put nothing on the page.
    /// </summary>
    public bool HasBodyContent { get; set; }

    /// <summary>
    /// Gets or sets the lowest point body content reached.
    /// </summary>
    public double BodyBottom { get; set; }
}
