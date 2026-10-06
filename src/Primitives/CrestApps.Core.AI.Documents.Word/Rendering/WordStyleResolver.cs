using System.Globalization;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;

namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <summary>
/// Works out the formatting paragraphs and runs end up with: document defaults, then the table style, the
/// paragraph style and the styles it is based on, the list level, the character style, and the direct
/// formatting — in the order Word applies them.
/// </summary>
internal sealed class WordStyleResolver
{
    private readonly WordStyleIndex _styles;
    private readonly OpenXmlElement _paragraphDefaults;
    private readonly OpenXmlElement _runDefaults;
    private readonly string _minorFont;
    private readonly string _majorFont;
    private readonly Dictionary<string, string> _themeColors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="WordStyleResolver"/> class.
    /// </summary>
    /// <param name="mainPart">The main document part.</param>
    public WordStyleResolver(MainDocumentPart mainPart)
    {
        ArgumentNullException.ThrowIfNull(mainPart);

        _styles = new WordStyleIndex(mainPart);
        _paragraphDefaults = mainPart.StyleDefinitionsPart?.Styles?.DocDefaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle;
        _runDefaults = mainPart.StyleDefinitionsPart?.Styles?.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
        _minorFont = WordDesignReader.ThemeFont(mainPart, minor: true) ?? "Calibri";
        _majorFont = WordDesignReader.ThemeFont(mainPart, minor: false) ?? "Calibri Light";

        var scheme = mainPart.ThemePart?.Theme?.ThemeElements?.ColorScheme;

        if (scheme is not null)
        {
            foreach (var color in scheme.ChildElements)
            {
                var value = color.GetFirstChild<A.RgbColorModelHex>()?.Val?.Value ?? color.GetFirstChild<A.SystemColor>()?.LastColor?.Value;

                if (value is not null)
                {
                    _themeColors[color.LocalName] = value;
                }
            }
        }
    }

    /// <summary>
    /// Gets the style index.
    /// </summary>
    public WordStyleIndex Styles => _styles;

    /// <summary>
    /// Resolves a paragraph's formatting.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="table">The table style the paragraph sits in, or <see langword="null"/>.</param>
    /// <returns>The formatting, and the run formatting the paragraph's text starts from.</returns>
    public (WordResolvedParagraph Paragraph, WordResolvedRun Run) Resolve(Paragraph paragraph, WordTableStyle table = null)
    {
        var resolved = new WordResolvedParagraph();
        var run = new WordResolvedRun { Font = _minorFont };

        ApplyParagraph(resolved, _paragraphDefaults);
        ApplyRun(run, _runDefaults);

        if (table is not null)
        {
            ApplyParagraph(resolved, table.Paragraph);
            ApplyRun(run, table.Run);
        }

        var styleId = _styles.StyleOf(paragraph);

        foreach (var style in _styles.Chain(styleId).Reverse())
        {
            ApplyParagraph(resolved, style.StyleParagraphProperties);
            ApplyRun(run, style.StyleRunProperties);
        }

        resolved.StyleId = styleId;

        var direct = paragraph?.ParagraphProperties;

        if (direct?.NumberingProperties is not null)
        {
            ApplyNumbering(resolved, direct.NumberingProperties);
        }

        if (resolved.NumberId is { } numberId && _styles.LevelOf(numberId, resolved.NumberLevel) is { } level)
        {
            ApplyParagraph(resolved, level.PreviousParagraphProperties);
        }

        ApplyParagraph(resolved, direct);

        return (resolved, run);
    }

    /// <summary>
    /// Resolves a run's formatting on top of its paragraph's.
    /// </summary>
    /// <param name="paragraphRun">The run formatting of the paragraph.</param>
    /// <param name="properties">The run's own properties, or <see langword="null"/>.</param>
    /// <returns>The formatting.</returns>
    public WordResolvedRun ResolveRun(WordResolvedRun paragraphRun, RunProperties properties)
    {
        var run = paragraphRun.Clone();

        if (properties?.RunStyle?.Val?.Value is { } characterStyle)
        {
            foreach (var style in _styles.Chain(characterStyle).Reverse())
            {
                ApplyRun(run, style.StyleRunProperties);
            }
        }

        ApplyRun(run, properties);

        return run;
    }

    /// <summary>
    /// Resolves the formatting of a list marker.
    /// </summary>
    /// <param name="paragraphRun">The run formatting of the paragraph.</param>
    /// <param name="level">The list level.</param>
    /// <returns>The marker's formatting.</returns>
    public WordResolvedRun ResolveMarker(WordResolvedRun paragraphRun, Level level)
    {
        var run = paragraphRun.Clone();

        ApplyRun(run, level?.NumberingSymbolRunProperties);
        run.Underline = false;

        return run;
    }

    /// <summary>
    /// Reads a table's style: its borders, cell margins, base formatting and the formatting of its first row,
    /// banded rows and other conditional parts.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <returns>The style.</returns>
    public WordTableStyle ResolveTable(Table table)
    {
        var result = new WordTableStyle();
        var styleId = table?.GetFirstChild<TableProperties>()?.TableStyle?.Val?.Value;

        foreach (var style in _styles.Chain(styleId).Reverse())
        {
            result.Paragraph = style.StyleParagraphProperties ?? result.Paragraph;
            result.Run = style.StyleRunProperties ?? result.Run;

            if (style.StyleTableProperties is { } tableProperties)
            {
                result.Borders = tableProperties.TableBorders ?? result.Borders;
                result.CellMargins = tableProperties.TableCellMarginDefault ?? result.CellMargins;
            }

            foreach (var conditional in style.Elements<TableStyleProperties>())
            {
                var type = conditional.Type?.InnerText;

                if (string.IsNullOrEmpty(type))
                {
                    continue;
                }

                result.Conditional[type] = new WordTableStyleCondition(
                    conditional.GetFirstChild<RunPropertiesBaseStyle>(),
                    conditional.GetFirstChild<StyleParagraphProperties>(),
                    conditional.GetFirstChild<TableStyleConditionalFormattingTableCellProperties>()?.GetFirstChild<Shading>()?.Fill?.Value);
            }
        }

        return result;
    }

    /// <summary>
    /// Applies the run formatting of a table style's conditional part.
    /// </summary>
    /// <param name="run">The run formatting.</param>
    /// <param name="condition">The conditional part.</param>
    public void ApplyCondition(WordResolvedRun run, WordTableStyleCondition condition)
    {
        if (condition is not null)
        {
            ApplyRun(run, condition.Run);
        }
    }

    /// <summary>
    /// Resolves a color, including a theme color, to six hexadecimal digits.
    /// </summary>
    /// <param name="value">The color value.</param>
    /// <param name="themeColor">The theme color name, or <see langword="null"/>.</param>
    /// <returns>The color, or <see langword="null"/> for automatic.</returns>
    public string ResolveColor(string value, string themeColor)
    {
        if (!string.IsNullOrEmpty(themeColor))
        {
            var key = themeColor switch
            {
                "text1" or "dark1" => "dk1",
                "background1" or "light1" => "lt1",
                "text2" or "dark2" => "dk2",
                "background2" or "light2" => "lt2",
                "hyperlink" => "hlink",
                "followedHyperlink" => "folHlink",
                _ => themeColor,
            };

            if (_themeColors.TryGetValue(key, out var theme))
            {
                return theme;
            }
        }

        return string.IsNullOrEmpty(value) || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase) ? null : value.ToUpperInvariant();
    }

    private void ApplyParagraph(WordResolvedParagraph target, OpenXmlElement properties)
    {
        if (properties is null)
        {
            return;
        }

        foreach (var element in properties.ChildElements)
        {
            switch (element)
            {
                case Justification justification:
                    target.Alignment = justification.Val?.InnerText switch
                    {
                        "center" => "center",
                        "right" or "end" => "right",
                        "both" or "distribute" or "thaiDistribute" or "lowKashida" or "mediumKashida" or "highKashida" => "both",
                        _ => "left",
                    };

                    break;

                case Indentation indentation:
                    if (Twips(indentation.Left?.Value ?? indentation.Start?.Value) is { } left)
                    {
                        target.IndentLeft = left;
                    }

                    if (Twips(indentation.Right?.Value ?? indentation.End?.Value) is { } right)
                    {
                        target.IndentRight = right;
                    }

                    if (Twips(indentation.Hanging?.Value) is { } hanging)
                    {
                        target.FirstLine = -hanging;
                    }
                    else if (Twips(indentation.FirstLine?.Value) is { } firstLine)
                    {
                        target.FirstLine = firstLine;
                    }

                    break;

                case SpacingBetweenLines spacing:
                    if (spacing.BeforeAutoSpacing?.Value == true)
                    {
                        target.SpaceBefore = 14;
                    }
                    else if (Twips(spacing.Before?.Value) is { } before)
                    {
                        target.SpaceBefore = before;
                    }

                    if (spacing.AfterAutoSpacing?.Value == true)
                    {
                        target.SpaceAfter = 14;
                    }
                    else if (Twips(spacing.After?.Value) is { } after)
                    {
                        target.SpaceAfter = after;
                    }

                    if (double.TryParse(spacing.Line?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var line))
                    {
                        var rule = spacing.LineRule?.InnerText ?? "auto";

                        target.LineRule = rule;
                        target.LineValue = rule == "auto" ? line / 240 : line / 20;
                    }

                    break;

                case KeepNext keepNext:
                    target.KeepNext = keepNext.Val?.Value ?? true;

                    break;

                case KeepLines keepLines:
                    target.KeepLines = keepLines.Val?.Value ?? true;

                    break;

                case PageBreakBefore pageBreak:
                    target.PageBreakBefore = pageBreak.Val?.Value ?? true;

                    break;

                case ContextualSpacing contextual:
                    target.ContextualSpacing = contextual.Val?.Value ?? true;

                    break;

                case Shading shading:
                    target.Shading = ResolveColor(shading.Fill?.Value, shading.ThemeFill?.InnerText);

                    break;

                case ParagraphBorders borders:
                    target.Top = ReadBorder(borders.TopBorder) ?? target.Top;
                    target.Bottom = ReadBorder(borders.BottomBorder) ?? target.Bottom;
                    target.Left = ReadBorder(borders.LeftBorder) ?? target.Left;
                    target.Right = ReadBorder(borders.RightBorder) ?? target.Right;

                    break;

                case Tabs tabs:
                    foreach (var stop in tabs.Elements<TabStop>())
                    {
                        var position = Twips(stop.Position?.Value.ToString(CultureInfo.InvariantCulture)) ?? 0;
                        var kind = stop.Val?.InnerText ?? "left";

                        target.Tabs.RemoveAll(existing => Math.Abs(existing.Position - position) < 0.5);

                        if (kind != "clear")
                        {
                            target.Tabs.Add(new WordTabStop(position, kind is "end" ? "right" : kind is "start" ? "left" : kind, stop.Leader?.InnerText ?? "none"));
                        }
                    }

                    target.Tabs.Sort((left, right) => left.Position.CompareTo(right.Position));

                    break;

                case NumberingProperties numbering:
                    ApplyNumbering(target, numbering);

                    break;
            }
        }
    }

    private static void ApplyNumbering(WordResolvedParagraph target, NumberingProperties numbering)
    {
        if (numbering.NumberingId?.Val?.Value is { } numberId)
        {
            target.NumberId = numberId == 0 ? null : numberId;
        }

        if (numbering.NumberingLevelReference?.Val?.Value is { } level)
        {
            target.NumberLevel = level;
        }
    }

    private void ApplyRun(WordResolvedRun target, OpenXmlElement properties)
    {
        if (properties is null)
        {
            return;
        }

        foreach (var element in properties.ChildElements)
        {
            switch (element)
            {
                case RunFonts fonts:
                    var font = fonts.Ascii?.Value ?? fonts.HighAnsi?.Value;

                    if (!string.IsNullOrWhiteSpace(font))
                    {
                        target.Font = font;
                    }
                    else if (fonts.AsciiTheme is not null)
                    {
                        var theme = fonts.AsciiTheme.InnerText;

                        target.Font = theme.StartsWith("major", StringComparison.OrdinalIgnoreCase) ? _majorFont : _minorFont;
                    }

                    break;

                case Bold bold:
                    target.Bold = bold.Val?.Value ?? true;

                    break;

                case Italic italic:
                    target.Italic = italic.Val?.Value ?? true;

                    break;

                case Strike strike:
                    target.Strike = strike.Val?.Value ?? true;

                    break;

                case DoubleStrike doubleStrike:
                    target.Strike = doubleStrike.Val?.Value ?? true;

                    break;

                case Caps caps:
                    target.Caps = caps.Val?.Value ?? true;

                    break;

                case SmallCaps smallCaps:
                    target.SmallCaps = smallCaps.Val?.Value ?? true;

                    break;

                case Vanish vanish:
                    target.Hidden = vanish.Val?.Value ?? true;

                    break;

                case Color color:
                    target.Color = ResolveColor(color.Val?.Value, color.ThemeColor?.InnerText) ?? "000000";

                    break;

                case FontSize size when double.TryParse(size.Val?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var halfPoints) && halfPoints > 0:
                    target.Size = halfPoints / 2;

                    break;

                case Highlight highlight:
                    target.Background = HighlightColor(highlight.Val?.InnerText);

                    break;

                case Shading shading:
                    target.Background = ResolveColor(shading.Fill?.Value, shading.ThemeFill?.InnerText) ?? target.Background;

                    break;

                case Underline underline:
                    target.Underline = underline.Val is null || underline.Val.InnerText != "none";

                    break;

                case VerticalTextAlignment vertical:
                    target.VerticalPosition = vertical.Val?.InnerText switch
                    {
                        "superscript" => 1,
                        "subscript" => -1,
                        _ => 0,
                    };

                    break;
            }
        }
    }

    private WordBorder ReadBorder(BorderType border)
    {
        if (border?.Val is null)
        {
            return null;
        }

        var style = border.Val.InnerText;

        if (style is "nil" or "none")
        {
            return new WordBorder(0, null, 0, "none");
        }

        return new WordBorder(
            Math.Max(0.25, (border.Size?.Value ?? 4U) / 8d),
            ResolveColor(border.Color?.Value, border.ThemeColor?.InnerText) ?? "000000",
            border.Space?.Value ?? 0U,
            style);
    }

    /// <summary>
    /// Reads a border of a table or cell.
    /// </summary>
    /// <param name="border">The border element.</param>
    /// <returns>The border, a border of style <c>none</c> when it is switched off, or <see langword="null"/> when not given.</returns>
    public WordBorder Border(BorderType border)
    {
        return ReadBorder(border);
    }

    private static double? Twips(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var twips) ? twips / 20 : null;
    }

    private static string HighlightColor(string name)
    {
        return name switch
        {
            "yellow" => "FFFF00",
            "green" => "00FF00",
            "cyan" => "00FFFF",
            "magenta" => "FF00FF",
            "blue" => "0000FF",
            "red" => "FF0000",
            "darkBlue" => "000080",
            "darkCyan" => "008080",
            "darkGreen" => "008000",
            "darkMagenta" => "800080",
            "darkRed" => "800000",
            "darkYellow" => "808000",
            "darkGray" => "808080",
            "lightGray" => "C0C0C0",
            "black" => "000000",
            "white" => "FFFFFF",
            _ => null,
        };
    }
}

/// <summary>
/// The parts of a table style that decide how its cells look.
/// </summary>
internal sealed class WordTableStyle
{
    /// <summary>
    /// Gets or sets the paragraph formatting of every cell.
    /// </summary>
    public OpenXmlElement Paragraph { get; set; }

    /// <summary>
    /// Gets or sets the run formatting of every cell.
    /// </summary>
    public OpenXmlElement Run { get; set; }

    /// <summary>
    /// Gets or sets the table borders.
    /// </summary>
    public TableBorders Borders { get; set; }

    /// <summary>
    /// Gets or sets the default cell margins.
    /// </summary>
    public TableCellMarginDefault CellMargins { get; set; }

    /// <summary>
    /// Gets the conditional formatting by part: <c>firstRow</c>, <c>lastRow</c>, <c>firstCol</c>, <c>band1Horz</c>…
    /// </summary>
    public Dictionary<string, WordTableStyleCondition> Conditional { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// The formatting a table style gives one conditional part of a table.
/// </summary>
/// <param name="Run">The run formatting.</param>
/// <param name="Paragraph">The paragraph formatting.</param>
/// <param name="Fill">The cell fill.</param>
internal sealed record WordTableStyleCondition(OpenXmlElement Run, OpenXmlElement Paragraph, string Fill);
