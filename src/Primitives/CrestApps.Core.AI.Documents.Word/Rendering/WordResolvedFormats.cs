namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <summary>
/// The formatting a run of text ends up with once its document defaults, styles and direct formatting are
/// applied in Word's order.
/// </summary>
internal sealed class WordResolvedRun
{
    /// <summary>
    /// Gets or sets the typeface.
    /// </summary>
    public string Font { get; set; } = "Calibri";

    /// <summary>
    /// Gets or sets the size in points.
    /// </summary>
    public double Size { get; set; } = 11;

    /// <summary>
    /// Gets or sets a value indicating whether the text is bold.
    /// </summary>
    public bool Bold { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is italic.
    /// </summary>
    public bool Italic { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is struck through.
    /// </summary>
    public bool Strike { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is shown in capitals.
    /// </summary>
    public bool Caps { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is shown in small capitals.
    /// </summary>
    public bool SmallCaps { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is hidden.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is underlined.
    /// </summary>
    public bool Underline { get; set; }

    /// <summary>
    /// Gets or sets the text color, as six hexadecimal digits.
    /// </summary>
    public string Color { get; set; } = "000000";

    /// <summary>
    /// Gets or sets the background behind the text, as six hexadecimal digits, or <see langword="null"/>.
    /// </summary>
    public string Background { get; set; }

    /// <summary>
    /// Gets or sets the vertical position: 1 for superscript, -1 for subscript, 0 for normal.
    /// </summary>
    public int VerticalPosition { get; set; }

    /// <summary>
    /// Gets the size the text is drawn at, smaller for a superscript or subscript.
    /// </summary>
    public double DrawnSize => VerticalPosition == 0 ? Size : Size * 0.65;

    /// <summary>
    /// Copies the format.
    /// </summary>
    /// <returns>The copy.</returns>
    public WordResolvedRun Clone()
    {
        return (WordResolvedRun)MemberwiseClone();
    }
}

/// <summary>
/// The formatting a paragraph ends up with once its document defaults, styles, numbering and direct formatting
/// are applied in Word's order.
/// </summary>
internal sealed class WordResolvedParagraph
{
    /// <summary>
    /// Gets or sets the alignment: <c>left</c>, <c>center</c>, <c>right</c> or <c>both</c>.
    /// </summary>
    public string Alignment { get; set; } = "left";

    /// <summary>
    /// Gets or sets the left indent in points.
    /// </summary>
    public double IndentLeft { get; set; }

    /// <summary>
    /// Gets or sets the right indent in points.
    /// </summary>
    public double IndentRight { get; set; }

    /// <summary>
    /// Gets or sets the first-line indent in points; negative for a hanging indent.
    /// </summary>
    public double FirstLine { get; set; }

    /// <summary>
    /// Gets or sets the space before in points.
    /// </summary>
    public double SpaceBefore { get; set; }

    /// <summary>
    /// Gets or sets the space after in points.
    /// </summary>
    public double SpaceAfter { get; set; }

    /// <summary>
    /// Gets or sets the line spacing rule: <c>auto</c> (a multiple), <c>exact</c> or <c>atLeast</c>.
    /// </summary>
    public string LineRule { get; set; } = "auto";

    /// <summary>
    /// Gets or sets the line spacing: a multiple for <c>auto</c>, or points for <c>exact</c> and <c>atLeast</c>.
    /// </summary>
    public double LineValue { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether the paragraph stays on the page of the next.
    /// </summary>
    public bool KeepNext { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the paragraph's lines stay together.
    /// </summary>
    public bool KeepLines { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the paragraph starts a new page.
    /// </summary>
    public bool PageBreakBefore { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether spacing between paragraphs of the same style is ignored.
    /// </summary>
    public bool ContextualSpacing { get; set; }

    /// <summary>
    /// Gets or sets the background fill.
    /// </summary>
    public string Shading { get; set; }

    /// <summary>
    /// Gets or sets the top border.
    /// </summary>
    public WordBorder Top { get; set; }

    /// <summary>
    /// Gets or sets the bottom border.
    /// </summary>
    public WordBorder Bottom { get; set; }

    /// <summary>
    /// Gets or sets the left border.
    /// </summary>
    public WordBorder Left { get; set; }

    /// <summary>
    /// Gets or sets the right border.
    /// </summary>
    public WordBorder Right { get; set; }

    /// <summary>
    /// Gets or sets the custom tab stops.
    /// </summary>
    public List<WordTabStop> Tabs { get; set; } = [];

    /// <summary>
    /// Gets or sets the numbering instance, when the paragraph is a list item.
    /// </summary>
    public int? NumberId { get; set; }

    /// <summary>
    /// Gets or sets the list level.
    /// </summary>
    public int NumberLevel { get; set; }

    /// <summary>
    /// Gets or sets the paragraph style id.
    /// </summary>
    public string StyleId { get; set; }

    /// <summary>
    /// Copies the format.
    /// </summary>
    /// <returns>The copy.</returns>
    public WordResolvedParagraph Clone()
    {
        var copy = (WordResolvedParagraph)MemberwiseClone();

        copy.Tabs = [.. Tabs];

        return copy;
    }
}

/// <summary>
/// A border line.
/// </summary>
/// <param name="Width">The width in points.</param>
/// <param name="Color">The color, as six hexadecimal digits.</param>
/// <param name="Space">The space between the border and the content, in points.</param>
/// <param name="Style">The line style, such as <c>single</c>, <c>double</c> or <c>dashed</c>.</param>
internal sealed record WordBorder(double Width, string Color, double Space, string Style);

/// <summary>
/// A tab stop.
/// </summary>
/// <param name="Position">The position from the left indent's origin, in points.</param>
/// <param name="Alignment">How text aligns at it: <c>left</c>, <c>center</c>, <c>right</c> or <c>decimal</c>.</param>
/// <param name="Leader">The leader: <c>dot</c>, <c>hyphen</c>, <c>underscore</c> or <c>none</c>.</param>
internal sealed record WordTabStop(double Position, string Alignment, string Leader);
