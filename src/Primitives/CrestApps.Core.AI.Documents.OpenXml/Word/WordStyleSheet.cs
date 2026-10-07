using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.OpenXml.Word;

/// <summary>
/// Writes the styles a document is built with, and finds them again in a document that already has its own.
/// </summary>
/// <remarks>
/// Headings carry real heading styles with outline levels rather than bold, larger runs, because that is what
/// a table of contents, the navigation pane, an outline and an accessibility checker read. A document a user
/// uploaded keeps its own styles: a style is only added when the document has nothing that matches it, and a
/// match is made on the style's built-in name, which stays English when a localized Word renames its id.
/// </remarks>
internal static class WordStyleSheet
{
    /// <summary>
    /// The body text style.
    /// </summary>
    public const string Normal = "Normal";

    /// <summary>
    /// The document title style.
    /// </summary>
    public const string Title = "Title";

    /// <summary>
    /// The subtitle style.
    /// </summary>
    public const string Subtitle = "Subtitle";

    /// <summary>
    /// The quotation style.
    /// </summary>
    public const string Quote = "Quote";

    /// <summary>
    /// The code block style.
    /// </summary>
    public const string CodeBlock = "CodeBlock";

    /// <summary>
    /// The style list items are set in.
    /// </summary>
    public const string ListParagraph = "ListParagraph";

    /// <summary>
    /// The caption style tables and figures are labelled with.
    /// </summary>
    public const string Caption = "Caption";

    /// <summary>
    /// The heading of a table of contents.
    /// </summary>
    public const string TocHeading = "TOCHeading";

    /// <summary>
    /// The page header style.
    /// </summary>
    public const string Header = "Header";

    /// <summary>
    /// The page footer style.
    /// </summary>
    public const string Footer = "Footer";

    /// <summary>
    /// The paragraph style with no space before or after.
    /// </summary>
    public const string NoSpacing = "NoSpacing";

    /// <summary>
    /// The footnote text style.
    /// </summary>
    public const string FootnoteText = "FootnoteText";

    /// <summary>
    /// The hyperlink character style.
    /// </summary>
    public const string Hyperlink = "Hyperlink";

    /// <summary>
    /// The inline code character style.
    /// </summary>
    public const string InlineCode = "InlineCode";

    /// <summary>
    /// The footnote reference character style.
    /// </summary>
    public const string FootnoteReference = "FootnoteReference";

    /// <summary>
    /// The plain gridded table style.
    /// </summary>
    public const string TableGrid = "TableGrid";

    /// <summary>
    /// The data table style: a shaded header row, banded rows and light borders.
    /// </summary>
    public const string DataTable = "DataTable";

    private static readonly double[] _headingSizes = [18, 14, 12, 11, 11, 11];
    private static readonly double[] _headingSpaceBefore = [18, 14, 12, 10, 8, 8];

    // The name and kind of every style this sheet defines, by id. They do not depend on the design.
    private static readonly Lazy<Dictionary<string, (string Name, StyleValues? Type)>> _definitions = new(() =>
        CreateAll(new WordDesign()).ToDictionary(
            style => style.StyleId.Value,
            style => (style.StyleName?.Val?.Value, style.Type?.Value),
            StringComparer.Ordinal));

    /// <summary>
    /// Returns the style id of a heading level.
    /// </summary>
    /// <param name="level">The level, from 1.</param>
    /// <returns>The style id.</returns>
    public static string Heading(int level)
    {
        return "Heading" + Math.Clamp(level, 1, 9).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns the style id of a table of contents level.
    /// </summary>
    /// <param name="level">The level, from 1.</param>
    /// <returns>The style id.</returns>
    public static string Toc(int level)
    {
        return "TOC" + Math.Clamp(level, 1, 9).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Builds the complete style sheet of a new document.
    /// </summary>
    /// <param name="design">The design.</param>
    /// <returns>The style sheet.</returns>
    public static Styles Create(WordDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);

        var styles = new Styles();

        styles.Append(CreateDocDefaults(design));

        foreach (var style in CreateAll(design))
        {
            styles.Append(style);
        }

        return styles;
    }

    /// <summary>
    /// Restyles a document: its defaults and every style this sheet defines are rewritten from the design, and
    /// every other style is left as it is.
    /// </summary>
    /// <param name="mainPart">The main document part.</param>
    /// <param name="design">The design.</param>
    public static void Apply(MainDocumentPart mainPart, WordDesign design)
    {
        ArgumentNullException.ThrowIfNull(mainPart);
        ArgumentNullException.ThrowIfNull(design);

        var styles = GetOrCreateStyles(mainPart);

        styles.DocDefaults?.Remove();
        styles.PrependChild(CreateDocDefaults(design));

        foreach (var style in CreateAll(design))
        {
            var existing = FindStyle(styles, style.StyleId?.Value, style.StyleName?.Val?.Value, style.Type?.Value);

            if (existing is null)
            {
                styles.Append(style);

                continue;
            }

            // The document's own id is kept, so every paragraph that already uses the style follows the change.
            style.StyleId = existing.StyleId;
            existing.InsertAfterSelf(style);
            existing.Remove();
        }
    }

    /// <summary>
    /// Returns the id a document uses for one of this sheet's styles, adding the style when the document has
    /// nothing that matches it.
    /// </summary>
    /// <param name="mainPart">The main document part.</param>
    /// <param name="styleId">One of this sheet's style ids.</param>
    /// <param name="design">The design an added style is written with.</param>
    /// <returns>The id to reference.</returns>
    public static string Ensure(MainDocumentPart mainPart, string styleId, WordDesign design)
    {
        ArgumentNullException.ThrowIfNull(mainPart);

        var styles = GetOrCreateStyles(mainPart);

        // The style is looked for by its known name and kind first; the whole sheet is only built when the
        // style has to be added, because this runs for every code span and link a document is written with.
        if (styleId is null || !_definitions.Value.TryGetValue(styleId, out var known))
        {
            return FindStyle(styles, styleId, null, null)?.StyleId?.Value ?? styleId;
        }

        var existing = FindStyle(styles, styleId, known.Name, known.Type);

        if (existing is not null)
        {
            return existing.StyleId?.Value ?? styleId;
        }

        var definition = CreateAll(design ?? new WordDesign()).First(style => string.Equals(style.StyleId?.Value, styleId, StringComparison.Ordinal));

        // A style refers to the styles it is based on and followed by, and those have to exist too.
        if (definition.BasedOn?.Val?.Value is { } basedOn && !string.Equals(basedOn, styleId, StringComparison.Ordinal))
        {
            definition.BasedOn.Val = Ensure(mainPart, basedOn, design);
        }

        styles.Append(definition);

        return styleId;
    }

    /// <summary>
    /// Finds the id of a style by the id, the name or the built-in name a model might use for it.
    /// </summary>
    /// <param name="mainPart">The main document part.</param>
    /// <param name="nameOrId">The style id or name, such as <c>Heading 2</c>, <c>heading2</c> or <c>Quote</c>.</param>
    /// <param name="type">The kind of style, or <see langword="null"/> for any.</param>
    /// <returns>The style id, or <see langword="null"/> when the document has no such style.</returns>
    public static string Find(MainDocumentPart mainPart, string nameOrId, StyleValues? type)
    {
        ArgumentNullException.ThrowIfNull(mainPart);

        if (string.IsNullOrWhiteSpace(nameOrId))
        {
            return null;
        }

        var styles = mainPart.StyleDefinitionsPart?.Styles;

        if (styles is null)
        {
            return null;
        }

        var wanted = nameOrId.Trim();
        var style = FindStyle(styles, wanted, wanted, type);

        return style?.StyleId?.Value;
    }

    /// <summary>
    /// Returns the style sheet of a document, creating the part when the document has none.
    /// </summary>
    /// <param name="mainPart">The main document part.</param>
    /// <returns>The style sheet.</returns>
    public static Styles GetOrCreateStyles(MainDocumentPart mainPart)
    {
        ArgumentNullException.ThrowIfNull(mainPart);

        var part = mainPart.StyleDefinitionsPart ?? mainPart.AddNewPart<StyleDefinitionsPart>();

        part.Styles ??= new Styles();

        return part.Styles;
    }

    /// <summary>
    /// Compares two style names the way Word does: ignoring case, spaces and hyphens.
    /// </summary>
    /// <param name="left">The first name.</param>
    /// <param name="right">The second name.</param>
    /// <returns><see langword="true"/> when the names refer to the same style.</returns>
    public static bool NamesMatch(string left, string right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(Compact(left), Compact(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string Compact(string name)
    {
        return name.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);
    }

    private static Style FindStyle(Styles styles, string styleId, string styleName, StyleValues? type)
    {
        var candidates = styles.Elements<Style>().Where(style => type is null || style.Type?.Value == type).ToList();

        return candidates.FirstOrDefault(style => string.Equals(style.StyleId?.Value, styleId, StringComparison.Ordinal))
            ?? candidates.FirstOrDefault(style => styleName is not null && NamesMatch(style.StyleName?.Val?.Value, styleName))
            ?? candidates.FirstOrDefault(style => styleId is not null && NamesMatch(style.StyleId?.Value, styleId))
            ?? candidates.FirstOrDefault(style => styleId is not null && NamesMatch(style.StyleName?.Val?.Value, styleId));
    }

    private static DocDefaults CreateDocDefaults(WordDesign design)
    {
        return new DocDefaults(
            new RunPropertiesDefault(new RunPropertiesBaseStyle
            {
                RunFonts = Fonts(design.BodyFont),
                Color = new Color { Val = design.TextColor },
                FontSize = new FontSize { Val = WordUnits.ToHalfPoints(design.BodySize) },
                FontSizeComplexScript = new FontSizeComplexScript { Val = WordUnits.ToHalfPoints(design.BodySize) },
                Languages = new Languages { Val = "en-US" },
            }),
            new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle
            {
                SpacingBetweenLines = new SpacingBetweenLines
                {
                    After = Twips(design.ParagraphSpacing),
                    Line = LineValue(design.LineSpacing),
                    LineRule = LineSpacingRuleValues.Auto,
                },
            }));
    }

    private static List<Style> CreateAll(WordDesign design)
    {
        var styles = new List<Style>
        {
            ParagraphStyle(Normal, "Normal", basedOn: null, next: null, priority: 0, primary: true, isDefault: true),
            CreateTitle(design),
            CreateSubtitle(design),
        };

        for (var level = 1; level <= 6; level++)
        {
            styles.Add(CreateHeading(design, level));
        }

        styles.Add(CreateQuote(design));
        styles.Add(CreateCodeBlock(design));
        styles.Add(CreateListParagraph());
        styles.Add(CreateCaption(design));
        styles.Add(CreateTocHeading(design));

        for (var level = 1; level <= 6; level++)
        {
            styles.Add(CreateToc(level));
        }

        styles.Add(CreateHeaderFooter(Header, "header"));
        styles.Add(CreateHeaderFooter(Footer, "footer"));
        styles.Add(CreateNoSpacing());
        styles.Add(CreateFootnoteText());
        styles.Add(CreateCharacterStyle(Hyperlink, "Hyperlink", new StyleRunProperties
        {
            Color = new Color { Val = design.LinkColor },
            Underline = new Underline { Val = UnderlineValues.Single },
        }));
        styles.Add(CreateCharacterStyle(InlineCode, "Inline Code", new StyleRunProperties
        {
            RunFonts = Fonts(design.CodeFont),
            FontSize = new FontSize { Val = WordUnits.ToHalfPoints(Math.Max(8, design.BodySize - 1.5)) },
            Shading = new Shading { Val = ShadingPatternValues.Clear, Fill = "F2F2F2", Color = "auto" },
        }));
        styles.Add(CreateCharacterStyle(FootnoteReference, "footnote reference", new StyleRunProperties
        {
            VerticalTextAlignment = new VerticalTextAlignment { Val = VerticalPositionValues.Superscript },
        }));
        styles.Add(CreateTableGrid(design));
        styles.Add(CreateDataTable(design));

        return styles;
    }

    private static Style ParagraphStyle(string id, string name, string basedOn, string next, int priority, bool primary, bool isDefault = false)
    {
        var style = new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = id,
            StyleName = new StyleName { Val = name },
            UIPriority = new UIPriority { Val = priority },
        };

        if (isDefault)
        {
            style.Default = true;
        }

        if (basedOn is not null)
        {
            style.BasedOn = new BasedOn { Val = basedOn };
        }

        if (next is not null)
        {
            style.NextParagraphStyle = new NextParagraphStyle { Val = next };
        }

        if (primary)
        {
            style.PrimaryStyle = new PrimaryStyle();
        }

        return style;
    }

    private static Style CreateTitle(WordDesign design)
    {
        var style = ParagraphStyle(Title, "Title", Normal, Normal, 10, primary: true);

        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            SpacingBetweenLines = new SpacingBetweenLines { After = Twips(6), Line = "240", LineRule = LineSpacingRuleValues.Auto },
            ContextualSpacing = new ContextualSpacing(),
        };

        style.StyleRunProperties = new StyleRunProperties
        {
            RunFonts = Fonts(design.HeadingFont),
            Color = new Color { Val = design.HeadingColor },
            Kern = new Kern { Val = 28U },
            FontSize = new FontSize { Val = WordUnits.ToHalfPoints(28) },
            FontSizeComplexScript = new FontSizeComplexScript { Val = WordUnits.ToHalfPoints(28) },
        };

        return style;
    }

    private static Style CreateSubtitle(WordDesign design)
    {
        var style = ParagraphStyle(Subtitle, "Subtitle", Normal, Normal, 11, primary: true);

        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            SpacingBetweenLines = new SpacingBetweenLines { After = Twips(12) },
        };

        style.StyleRunProperties = new StyleRunProperties
        {
            RunFonts = Fonts(design.BodyFont),
            Color = new Color { Val = "595959" },
            Spacing = new Spacing { Val = 10 },
            FontSize = new FontSize { Val = WordUnits.ToHalfPoints(14) },
            FontSizeComplexScript = new FontSizeComplexScript { Val = WordUnits.ToHalfPoints(14) },
        };

        return style;
    }

    private static Style CreateHeading(WordDesign design, int level)
    {
        var size = _headingSizes[level - 1];
        var style = ParagraphStyle(Heading(level), "heading " + level.ToString(CultureInfo.InvariantCulture), Normal, Normal, 9, primary: true);

        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            KeepNext = new KeepNext(),
            KeepLines = new KeepLines(),
            SpacingBetweenLines = new SpacingBetweenLines
            {
                Before = Twips(_headingSpaceBefore[level - 1]),
                After = Twips(level <= 2 ? 6 : 4),
            },
            OutlineLevel = new OutlineLevel { Val = level - 1 },
        };

        var runProperties = new StyleRunProperties
        {
            RunFonts = Fonts(design.HeadingFont),
            Color = new Color { Val = design.HeadingColor },
            FontSize = new FontSize { Val = WordUnits.ToHalfPoints(size) },
            FontSizeComplexScript = new FontSizeComplexScript { Val = WordUnits.ToHalfPoints(size) },
        };

        if (level >= 2)
        {
            runProperties.Bold = new Bold();
            runProperties.BoldComplexScript = new BoldComplexScript();
        }

        if (level >= 5)
        {
            runProperties.Italic = new Italic();
        }

        style.StyleRunProperties = runProperties;

        return style;
    }

    private static Style CreateQuote(WordDesign design)
    {
        var style = ParagraphStyle(Quote, "Quote", Normal, Normal, 29, primary: true);

        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            ParagraphBorders = new ParagraphBorders(new LeftBorder
            {
                Val = BorderValues.Single,
                Size = 18U,
                Space = 8U,
                Color = design.AccentColor,
            }),
            SpacingBetweenLines = new SpacingBetweenLines { Before = Twips(6), After = Twips(10) },
            Indentation = new Indentation { Left = "567", Right = "567" },
        };

        style.StyleRunProperties = new StyleRunProperties
        {
            Italic = new Italic(),
            Color = new Color { Val = "404040" },
        };

        return style;
    }

    private static Style CreateCodeBlock(WordDesign design)
    {
        var style = ParagraphStyle(CodeBlock, "Code Block", Normal, Normal, 30, primary: false);

        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            Shading = new Shading { Val = ShadingPatternValues.Clear, Fill = "F4F4F4", Color = "auto" },
            SpacingBetweenLines = new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto },
            Indentation = new Indentation { Left = "170", Right = "170" },
            ContextualSpacing = new ContextualSpacing(),
        };

        style.StyleRunProperties = new StyleRunProperties
        {
            RunFonts = Fonts(design.CodeFont),
            Color = new Color { Val = "1E1E1E" },
            FontSize = new FontSize { Val = WordUnits.ToHalfPoints(Math.Max(8, design.BodySize - 1.5)) },
            FontSizeComplexScript = new FontSizeComplexScript { Val = WordUnits.ToHalfPoints(Math.Max(8, design.BodySize - 1.5)) },
        };

        return style;
    }

    private static Style CreateListParagraph()
    {
        var style = ParagraphStyle(ListParagraph, "List Paragraph", Normal, null, 34, primary: true);

        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            SpacingBetweenLines = new SpacingBetweenLines { After = Twips(3) },
            Indentation = new Indentation { Left = "720" },
            ContextualSpacing = new ContextualSpacing(),
        };

        return style;
    }

    private static Style CreateCaption(WordDesign design)
    {
        var style = ParagraphStyle(Caption, "caption", Normal, Normal, 35, primary: true);

        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            SpacingBetweenLines = new SpacingBetweenLines { After = Twips(10), Line = "240", LineRule = LineSpacingRuleValues.Auto },
        };

        style.StyleRunProperties = new StyleRunProperties
        {
            Italic = new Italic(),
            Color = new Color { Val = design.AccentColor },
            FontSize = new FontSize { Val = WordUnits.ToHalfPoints(Math.Max(8, design.BodySize - 2)) },
            FontSizeComplexScript = new FontSizeComplexScript { Val = WordUnits.ToHalfPoints(Math.Max(8, design.BodySize - 2)) },
        };

        return style;
    }

    private static Style CreateTocHeading(WordDesign design)
    {
        var style = ParagraphStyle(TocHeading, "TOC Heading", Heading(1), Normal, 39, primary: true);

        // The heading over a table of contents is not itself an entry in it.
        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            OutlineLevel = new OutlineLevel { Val = 9 },
        };

        style.StyleRunProperties = new StyleRunProperties
        {
            Color = new Color { Val = design.HeadingColor },
        };

        return style;
    }

    private static Style CreateToc(int level)
    {
        var style = ParagraphStyle(Toc(level), "toc " + level.ToString(CultureInfo.InvariantCulture), Normal, Normal, 39, primary: false);

        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            SpacingBetweenLines = new SpacingBetweenLines { After = Twips(level == 1 ? 5 : 3) },
            Indentation = new Indentation { Left = WordUnits.Invariant(220L * (level - 1)) },
        };

        if (level == 1)
        {
            style.StyleRunProperties = new StyleRunProperties { Bold = new Bold() };
        }

        return style;
    }

    private static Style CreateHeaderFooter(string id, string name)
    {
        var style = ParagraphStyle(id, name, Normal, null, 99, primary: false);

        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            SpacingBetweenLines = new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto },
        };

        style.StyleRunProperties = new StyleRunProperties
        {
            Color = new Color { Val = "595959" },
            FontSize = new FontSize { Val = "18" },
            FontSizeComplexScript = new FontSizeComplexScript { Val = "18" },
        };

        return style;
    }

    private static Style CreateNoSpacing()
    {
        var style = ParagraphStyle(NoSpacing, "No Spacing", null, null, 1, primary: true);

        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            SpacingBetweenLines = new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto },
        };

        return style;
    }

    private static Style CreateFootnoteText()
    {
        var style = ParagraphStyle(FootnoteText, "footnote text", Normal, null, 99, primary: false);

        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            SpacingBetweenLines = new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto },
        };

        style.StyleRunProperties = new StyleRunProperties
        {
            FontSize = new FontSize { Val = "18" },
            FontSizeComplexScript = new FontSizeComplexScript { Val = "18" },
        };

        return style;
    }

    private static Style CreateCharacterStyle(string id, string name, StyleRunProperties runProperties)
    {
        return new Style
        {
            Type = StyleValues.Character,
            StyleId = id,
            StyleName = new StyleName { Val = name },
            UIPriority = new UIPriority { Val = 99 },
            StyleRunProperties = runProperties,
        };
    }

    private static Style CreateTableGrid(WordDesign design)
    {
        return new Style
        {
            Type = StyleValues.Table,
            StyleId = TableGrid,
            StyleName = new StyleName { Val = "Table Grid" },
            UIPriority = new UIPriority { Val = 39 },
            StyleParagraphProperties = new StyleParagraphProperties
            {
                SpacingBetweenLines = new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto },
            },
            StyleTableProperties = new StyleTableProperties
            {
                TableIndentation = new TableIndentation { Width = 0, Type = TableWidthUnitValues.Dxa },
                TableBorders = Borders(design.TableBorderColor),
                TableCellMarginDefault = CellMargins(),
            },
        };
    }

    private static Style CreateDataTable(WordDesign design)
    {
        var style = new Style
        {
            Type = StyleValues.Table,
            StyleId = DataTable,
            StyleName = new StyleName { Val = "Data Table" },
            BasedOn = new BasedOn { Val = TableGrid },
            UIPriority = new UIPriority { Val = 40 },
            StyleParagraphProperties = new StyleParagraphProperties
            {
                SpacingBetweenLines = new SpacingBetweenLines { Before = "40", After = "40", Line = "240", LineRule = LineSpacingRuleValues.Auto },
            },
            StyleTableProperties = new StyleTableProperties
            {
                TableStyleRowBandSize = new TableStyleRowBandSize { Val = 1 },
                TableStyleColumnBandSize = new TableStyleColumnBandSize { Val = 1 },
                TableIndentation = new TableIndentation { Width = 0, Type = TableWidthUnitValues.Dxa },
                TableBorders = Borders(design.TableBorderColor),
                TableCellMarginDefault = CellMargins(),
            },
        };

        style.Append(new TableStyleProperties(
            new RunPropertiesBaseStyle
            {
                Bold = new Bold(),
                BoldComplexScript = new BoldComplexScript(),
                Color = new Color { Val = design.TableHeaderTextColor },
            },
            new TableStyleConditionalFormattingTableCellProperties(new Shading
            {
                Val = ShadingPatternValues.Clear,
                Fill = design.TableHeaderFill,
                Color = "auto",
            }))
        {
            Type = TableStyleOverrideValues.FirstRow,
        });

        if (!string.IsNullOrEmpty(design.TableBandFill))
        {
            style.Append(new TableStyleProperties(
                new TableStyleConditionalFormattingTableCellProperties(new Shading
                {
                    Val = ShadingPatternValues.Clear,
                    Fill = design.TableBandFill,
                    Color = "auto",
                }))
            {
                Type = TableStyleOverrideValues.Band1Horizontal,
            });
        }

        return style;
    }

    private static TableBorders Borders(string color)
    {
        return new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = color },
            new LeftBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = color },
            new BottomBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = color },
            new RightBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = color },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = color },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = color });
    }

    private static TableCellMarginDefault CellMargins()
    {
        return new TableCellMarginDefault(
            new TopMargin { Width = "0", Type = TableWidthUnitValues.Dxa },
            new TableCellLeftMargin { Width = 108, Type = TableWidthValues.Dxa },
            new BottomMargin { Width = "0", Type = TableWidthUnitValues.Dxa },
            new TableCellRightMargin { Width = 108, Type = TableWidthValues.Dxa });
    }

    /// <summary>
    /// Builds the font element that sets one typeface for every script.
    /// </summary>
    /// <param name="font">The typeface.</param>
    /// <returns>The font element.</returns>
    public static RunFonts Fonts(string font)
    {
        return new RunFonts
        {
            Ascii = font,
            HighAnsi = font,
            EastAsia = font,
            ComplexScript = font,
        };
    }

    /// <summary>
    /// Formats a length in points as twips text.
    /// </summary>
    /// <param name="points">The length in points.</param>
    /// <returns>The length in twips, as text.</returns>
    public static string Twips(double points)
    {
        return WordUnits.ToTwips(points).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a line spacing multiple as the value an automatic line rule stores, in 240ths of a line.
    /// </summary>
    /// <param name="multiple">The spacing as a multiple of single spacing.</param>
    /// <returns>The value, as text.</returns>
    public static string LineValue(double multiple)
    {
        return ((int)Math.Round(Math.Clamp(multiple, 0.5, 5) * 240)).ToString(CultureInfo.InvariantCulture);
    }
}
