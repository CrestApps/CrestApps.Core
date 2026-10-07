using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.OpenXml.Word;

/// <summary>
/// The character formatting a run of text can be given. Every property is optional: what is not set is left
/// as the paragraph's style has it.
/// </summary>
internal sealed class WordRunFormat
{
    /// <summary>
    /// Gets or sets the typeface.
    /// </summary>
    public string Font { get; set; }

    /// <summary>
    /// Gets or sets the size in points.
    /// </summary>
    public double? Size { get; set; }

    /// <summary>
    /// Gets or sets whether the text is bold.
    /// </summary>
    public bool? Bold { get; set; }

    /// <summary>
    /// Gets or sets whether the text is italic.
    /// </summary>
    public bool? Italic { get; set; }

    /// <summary>
    /// Gets or sets the underline: <c>single</c>, <c>double</c>, <c>dotted</c>, <c>dash</c>, <c>wave</c>, <c>thick</c> or <c>none</c>.
    /// </summary>
    public string Underline { get; set; }

    /// <summary>
    /// Gets or sets whether the text is struck through.
    /// </summary>
    public bool? Strikethrough { get; set; }

    /// <summary>
    /// Gets or sets the text color, as six hexadecimal digits.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets the highlight: one of Word's highlight colors, such as <c>yellow</c> or <c>green</c>.
    /// </summary>
    public string Highlight { get; set; }

    /// <summary>
    /// Gets or sets the background shading of the text, as six hexadecimal digits.
    /// </summary>
    public string Shading { get; set; }

    /// <summary>
    /// Gets or sets whether the text is raised as a superscript.
    /// </summary>
    public bool? Superscript { get; set; }

    /// <summary>
    /// Gets or sets whether the text is lowered as a subscript.
    /// </summary>
    public bool? Subscript { get; set; }

    /// <summary>
    /// Gets or sets whether the text is shown in capitals.
    /// </summary>
    public bool? AllCaps { get; set; }

    /// <summary>
    /// Gets or sets whether the text is shown in small capitals.
    /// </summary>
    public bool? SmallCaps { get; set; }

    /// <summary>
    /// Gets or sets the extra space between characters, in points.
    /// </summary>
    public double? CharacterSpacing { get; set; }

    /// <summary>
    /// Gets or sets the character style applied to the text.
    /// </summary>
    public string StyleId { get; set; }

    /// <summary>
    /// Gets or sets the language of the text, such as <c>en-US</c> or <c>fr-FR</c>.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Gets a value indicating whether nothing is set.
    /// </summary>
    public bool IsEmpty =>
        Font is null && Size is null && Bold is null && Italic is null && Underline is null &&
        Strikethrough is null && Color is null && Highlight is null && Shading is null && Superscript is null &&
        Subscript is null && AllCaps is null && SmallCaps is null && CharacterSpacing is null && StyleId is null &&
        Language is null;

    /// <summary>
    /// Writes the format onto run properties, changing only what is set. The typed setters place each
    /// element where the schema expects it.
    /// </summary>
    /// <param name="properties">The run properties.</param>
    public void ApplyTo(RunProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (StyleId is not null)
        {
            properties.RunStyle = string.IsNullOrEmpty(StyleId) ? null : new RunStyle { Val = StyleId };
        }

        if (!string.IsNullOrWhiteSpace(Font))
        {
            properties.RunFonts = WordStyleSheet.Fonts(Font.Trim());
        }

        if (Bold is not null)
        {
            properties.Bold = Bold.Value ? new Bold() : new Bold { Val = false };
            properties.BoldComplexScript = Bold.Value ? new BoldComplexScript() : new BoldComplexScript { Val = false };
        }

        if (Italic is not null)
        {
            properties.Italic = Italic.Value ? new Italic() : new Italic { Val = false };
            properties.ItalicComplexScript = Italic.Value ? new ItalicComplexScript() : new ItalicComplexScript { Val = false };
        }

        if (AllCaps is not null)
        {
            properties.Caps = AllCaps.Value ? new Caps() : null;
        }

        if (SmallCaps is not null)
        {
            properties.SmallCaps = SmallCaps.Value ? new SmallCaps() : null;
        }

        if (Strikethrough is not null)
        {
            properties.Strike = Strikethrough.Value ? new Strike() : null;
        }

        if (Color is not null)
        {
            properties.Color = WordColor.TryParse(Color, out var color) ? new Color { Val = color } : null;
        }

        if (CharacterSpacing is not null)
        {
            properties.Spacing = new Spacing { Val = WordUnits.ToTwips(CharacterSpacing.Value) };
        }

        if (Size is > 0)
        {
            properties.FontSize = new FontSize { Val = WordUnits.ToHalfPoints(Size.Value) };
            properties.FontSizeComplexScript = new FontSizeComplexScript { Val = WordUnits.ToHalfPoints(Size.Value) };
        }

        if (Highlight is not null)
        {
            properties.Highlight = TryReadHighlight(Highlight, out var highlight) ? new Highlight { Val = highlight } : null;
        }

        if (Underline is not null)
        {
            properties.Underline = ReadUnderline(Underline) is { } underline ? new Underline { Val = underline } : null;
        }

        if (Shading is not null)
        {
            properties.Shading = WordColor.TryParse(Shading, out var fill)
                ? new Shading { Val = ShadingPatternValues.Clear, Fill = fill, Color = "auto" }
                : null;
        }

        if (Superscript == true || Subscript == true)
        {
            properties.VerticalTextAlignment = new VerticalTextAlignment
            {
                Val = Superscript == true ? VerticalPositionValues.Superscript : VerticalPositionValues.Subscript,
            };
        }
        else if (Superscript == false || Subscript == false)
        {
            properties.VerticalTextAlignment = null;
        }

        if (!string.IsNullOrWhiteSpace(Language))
        {
            properties.Languages = new Languages { Val = Language.Trim() };
        }
    }

    /// <summary>
    /// Reads one of Word's highlight colors by name.
    /// </summary>
    /// <param name="value">The name, such as <c>yellow</c>, <c>light gray</c> or <c>none</c>.</param>
    /// <param name="highlight">The highlight color.</param>
    /// <returns><see langword="true"/> when the name is a highlight color.</returns>
    public static bool TryReadHighlight(string value, out HighlightColorValues highlight)
    {
        highlight = HighlightColorValues.Yellow;

        switch ((value ?? string.Empty).Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant())
        {
            case "yellow":
                highlight = HighlightColorValues.Yellow;

                return true;

            case "green":
            case "brightgreen":
                highlight = HighlightColorValues.Green;

                return true;

            case "cyan":
            case "turquoise":
                highlight = HighlightColorValues.Cyan;

                return true;

            case "magenta":
            case "pink":
                highlight = HighlightColorValues.Magenta;

                return true;

            case "blue":
                highlight = HighlightColorValues.Blue;

                return true;

            case "red":
                highlight = HighlightColorValues.Red;

                return true;

            case "darkblue":
                highlight = HighlightColorValues.DarkBlue;

                return true;

            case "darkcyan":
            case "teal":
                highlight = HighlightColorValues.DarkCyan;

                return true;

            case "darkgreen":
                highlight = HighlightColorValues.DarkGreen;

                return true;

            case "darkmagenta":
            case "violet":
                highlight = HighlightColorValues.DarkMagenta;

                return true;

            case "darkred":
                highlight = HighlightColorValues.DarkRed;

                return true;

            case "darkyellow":
                highlight = HighlightColorValues.DarkYellow;

                return true;

            case "darkgray":
            case "darkgrey":
                highlight = HighlightColorValues.DarkGray;

                return true;

            case "lightgray":
            case "lightgrey":
            case "gray":
            case "grey":
                highlight = HighlightColorValues.LightGray;

                return true;

            case "black":
                highlight = HighlightColorValues.Black;

                return true;

            case "white":
                highlight = HighlightColorValues.White;

                return true;

            default:
                return false;
        }
    }

    private static UnderlineValues? ReadUnderline(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "none" or "false" => null,
            "double" => UnderlineValues.Double,
            "dotted" => UnderlineValues.Dotted,
            "dash" or "dashed" => UnderlineValues.Dash,
            "wave" or "wavy" => UnderlineValues.Wave,
            "thick" => UnderlineValues.Thick,
            "words" => UnderlineValues.Words,
            _ => UnderlineValues.Single,
        };
    }
}
