using System.Globalization;
using CrestApps.Core.AI.Documents.Generation.RichText;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Fields;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Turns a <see cref="PdfDocumentDefinition"/> into a MigraDoc document: styles from the theme, a cover, a
/// table of contents, the body sections with their running heads and feet, and every block of content.
/// </summary>
/// <remarks>
/// The builder only lays out; it never reads a file or calls out. Everything it needs — the pictures in
/// particular — is resolved before it runs, so the same definition always produces the same document.
/// </remarks>
internal sealed partial class PdfMigraDocBuilder
{
    private const string TitleStyle = "DocTitle";
    private const string CaptionStyle = "Caption";
    private const string QuoteStyle = "Quote";
    private const string CodeStyle = "Code";
    private const string TocTitleStyle = "TocTitle";
    private const string TocEntryStylePrefix = "TocEntry";

    private static readonly PdfColor _white = new(255, 255, 255);
    private static readonly PdfColor _black = new(0, 0, 0);
    private static readonly PdfColor _negativeRed = new(0xC0, 0x00, 0x00);

    private readonly PdfDocumentDefinition _definition;
    private readonly IReadOnlyDictionary<string, PdfImageData> _images;
    private readonly PdfCompositionOptions _options;
    private readonly string _dateText;
    private readonly bool _forceDefaultFont;
    private readonly List<string> _warnings;
    private readonly List<TocEntry> _tocEntries = [];
    private readonly Dictionary<string, List<string>> _headingBookmarks = new(StringComparer.Ordinal);

    private PdfResolvedTheme _theme;
    private PageGeometry _page;
    private int _missingImageCount;

    // Whether a section before this one already carries the running heads, so this one does not start them.
    private bool _runningHeadsStarted;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfMigraDocBuilder"/> class.
    /// </summary>
    /// <param name="definition">The document to build.</param>
    /// <param name="images">The pictures the document refers to, already resolved, keyed by source.</param>
    /// <param name="options">The host defaults.</param>
    /// <param name="dateText">The text the <c>{date}</c> token prints.</param>
    /// <param name="forceDefaultFont">Whether every font is replaced by the default, used when rendering with the requested fonts failed.</param>
    /// <param name="warnings">Receives notes about anything that could not be built as described.</param>
    public PdfMigraDocBuilder(
        PdfDocumentDefinition definition,
        IReadOnlyDictionary<string, PdfImageData> images,
        PdfCompositionOptions options,
        string dateText,
        bool forceDefaultFont,
        List<string> warnings)
    {
        _definition = definition;
        _images = images ?? new Dictionary<string, PdfImageData>();
        _options = options ?? new PdfCompositionOptions();
        _dateText = dateText;
        _forceDefaultFont = forceDefaultFont;
        _warnings = warnings;
    }

    /// <summary>
    /// Builds the document.
    /// </summary>
    /// <returns>The MigraDoc document.</returns>
    public Document Build()
    {
        _theme = PdfResolvedTheme.Resolve(_definition.Theme, _options.DefaultFontFamily, _warnings);

        if (_forceDefaultFont)
        {
            // Only the fonts are dropped; the colours and table style still apply.
            _theme = _theme.WithFonts(PdfFontFamilies.Default);
        }

        var document = new Document();

        document.Info.Title = _definition.Title ?? string.Empty;
        document.Info.Author = _definition.Author ?? string.Empty;
        document.Info.Subject = _definition.Subject ?? string.Empty;
        document.Info.Keywords = _definition.Keywords ?? string.Empty;

        DefineStyles(document);
        CollectHeadings();

        var coverEnabled = IsCoverEnabled();
        var tocEnabled = _definition.TableOfContents?.Enabled == true;

        // The cover carries no header, footer or number, so with one the document's first page is already
        // behind the running heads, and "skip the first page" has nothing left to skip.
        _runningHeadsStarted = coverEnabled;

        if (coverEnabled)
        {
            AddCoverSection(document);
        }

        if (tocEnabled)
        {
            AddTableOfContentsSection(document);
        }

        var sections = _definition.Sections is { Count: > 0 }
            ? _definition.Sections
            : [new PdfSectionDefinition { Id = "s1" }];

        for (var index = 0; index < sections.Count; index++)
        {
            AddBodySection(document, sections[index], index == 0);
        }

        if (_missingImageCount > 0)
        {
            _warnings.Add($"{_missingImageCount} image(s) could not be placed and were left out.");
        }

        return document;
    }

    private bool IsCoverEnabled()
    {
        // A cover asked for is always drawn: dropping it because no title was given silently loses a page the
        // user asked for. The cover writes what stands in for the title, and says so.
        var cover = _definition.CoverPage;

        return cover is not null && (cover.Enabled ?? true);
    }

    private void DefineStyles(Document document)
    {
        var normal = document.Styles[StyleNames.Normal];
        normal.Font.Name = _theme.FontFamily;
        normal.Font.Size = _theme.BaseFontSize;
        normal.Font.Color = _theme.Text.ToMigraDoc();
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(Math.Round(_theme.BaseFontSize * 0.6, 1));
        normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Multiple;
        normal.ParagraphFormat.LineSpacing = _theme.LineSpacing;

        double[] sizes = [1.9, 1.45, 1.2, 1.05];
        double[] spaceBefore = [18, 14, 11, 9];

        for (var level = 1; level <= 4; level++)
        {
            var style = document.Styles["Heading" + level.ToString(CultureInfo.InvariantCulture)];
            style.Font.Name = _theme.HeadingFontFamily;
            style.Font.Size = Math.Round(_theme.BaseFontSize * sizes[level - 1], 1);
            style.Font.Bold = true;
            style.Font.Color = _theme.Heading.ToMigraDoc();
            style.ParagraphFormat.SpaceBefore = Unit.FromPoint(spaceBefore[level - 1]);
            style.ParagraphFormat.SpaceAfter = Unit.FromPoint(level == 1 ? 8 : 5);
            style.ParagraphFormat.KeepWithNext = true;
            style.ParagraphFormat.LineSpacingRule = LineSpacingRule.Single;
            style.ParagraphFormat.OutlineLevel = (OutlineLevel)level;

            if (level == 1)
            {
                // A hairline under the top level heading is what makes a long report scannable; it is the
                // one ornament every level-one section carries.
                style.ParagraphFormat.Borders.Bottom.Width = 0.75;
                style.ParagraphFormat.Borders.Bottom.Color = _theme.Heading.Blend(_white, 0.65).ToMigraDoc();
                style.ParagraphFormat.Borders.DistanceFromBottom = Unit.FromPoint(3);
            }
        }

        var title = document.Styles.AddStyle(TitleStyle, StyleNames.Normal);
        title.Font.Name = _theme.HeadingFontFamily;
        title.Font.Size = Math.Round(_theme.BaseFontSize * 2.3, 1);
        title.Font.Bold = true;
        title.Font.Color = _theme.Heading.ToMigraDoc();
        title.ParagraphFormat.SpaceAfter = Unit.FromPoint(14);
        title.ParagraphFormat.LineSpacingRule = LineSpacingRule.Single;
        title.ParagraphFormat.OutlineLevel = OutlineLevel.Level1;

        var caption = document.Styles.AddStyle(CaptionStyle, StyleNames.Normal);
        caption.Font.Size = Math.Max(6, _theme.BaseFontSize - 1.5);
        caption.Font.Italic = true;
        caption.Font.Color = _theme.Muted.ToMigraDoc();
        caption.ParagraphFormat.SpaceBefore = Unit.FromPoint(3);
        caption.ParagraphFormat.SpaceAfter = Unit.FromPoint(10);

        var quote = document.Styles.AddStyle(QuoteStyle, StyleNames.Normal);
        quote.Font.Italic = true;
        quote.Font.Color = _theme.Text.Blend(_white, 0.25).ToMigraDoc();
        quote.ParagraphFormat.LeftIndent = Unit.FromCentimeter(0.8);
        quote.ParagraphFormat.SpaceBefore = Unit.FromPoint(4);
        quote.ParagraphFormat.Borders.Left.Width = 2.5;
        quote.ParagraphFormat.Borders.Left.Color = _theme.Accent.Blend(_white, 0.3).ToMigraDoc();
        quote.ParagraphFormat.Borders.DistanceFromLeft = Unit.FromCentimeter(0.3);

        var code = document.Styles.AddStyle(CodeStyle, StyleNames.Normal);
        code.Font.Name = _forceDefaultFont ? PdfFontFamilies.Default : PdfFontFamilies.Monospace;
        code.Font.Size = Math.Max(6, _theme.BaseFontSize - 1.5);
        code.ParagraphFormat.Shading.Color = new PdfColor(0xF4, 0xF5, 0xF7).ToMigraDoc();
        code.ParagraphFormat.LeftIndent = Unit.FromCentimeter(0.2);
        code.ParagraphFormat.SpaceBefore = Unit.FromPoint(4);
        code.ParagraphFormat.SpaceAfter = Unit.FromPoint(8);
        code.ParagraphFormat.LineSpacingRule = LineSpacingRule.Single;

        var tocTitle = document.Styles.AddStyle(TocTitleStyle, StyleNames.Normal);
        tocTitle.Font.Name = _theme.HeadingFontFamily;
        tocTitle.Font.Size = Math.Round(_theme.BaseFontSize * 1.7, 1);
        tocTitle.Font.Bold = true;
        tocTitle.Font.Color = _theme.Heading.ToMigraDoc();
        tocTitle.ParagraphFormat.SpaceAfter = Unit.FromPoint(14);

        for (var level = 1; level <= 4; level++)
        {
            var entry = document.Styles.AddStyle(TocEntryStylePrefix + level.ToString(CultureInfo.InvariantCulture), StyleNames.Normal);
            entry.Font.Bold = level == 1;
            entry.ParagraphFormat.LeftIndent = Unit.FromPoint((level - 1) * 14);
            entry.ParagraphFormat.SpaceBefore = Unit.FromPoint(level == 1 ? 5 : 1);
            entry.ParagraphFormat.SpaceAfter = Unit.FromPoint(1);
        }

        var header = document.Styles[StyleNames.Header];
        header.Font.Size = Math.Max(6, _theme.BaseFontSize - 2);
        header.Font.Color = _theme.Muted.ToMigraDoc();

        var footer = document.Styles[StyleNames.Footer];
        footer.Font.Size = Math.Max(6, _theme.BaseFontSize - 2);
        footer.Font.Color = _theme.Muted.ToMigraDoc();
    }

    /// <summary>
    /// Finds every heading the table of contents lists and gives each a bookmark, before anything is laid
    /// out, so the contents page can link to headings that come after it.
    /// </summary>
    private void CollectHeadings()
    {
        var depth = Math.Clamp(_definition.TableOfContents?.Depth ?? 2, 1, 4);

        foreach (var section in _definition.Sections ?? [])
        {
            foreach (var block in section.Blocks ?? [])
            {
                var type = NormalizeType(block);

                if (type == PdfBlockTypes.Heading && !string.IsNullOrWhiteSpace(block.Text))
                {
                    var level = Math.Clamp(block.Level ?? 1, 1, 4);
                    var bookmark = "h_" + SafeId(block.Id);

                    _headingBookmarks[block.Id ?? string.Empty] = [bookmark];

                    if (level <= depth)
                    {
                        _tocEntries.Add(new TocEntry(RichTextParser.ToPlainText(RichTextParser.Parse(block.Text)), level, bookmark));
                    }
                }
                else if (type == PdfBlockTypes.Markdown && !string.IsNullOrWhiteSpace(block.Text))
                {
                    var bookmarks = new List<string>();
                    var counter = 0;

                    foreach (var rich in RichTextParser.Parse(block.Text))
                    {
                        if (rich.Kind != RichTextBlockKind.Heading)
                        {
                            continue;
                        }

                        var bookmark = "h_" + SafeId(block.Id) + "_" + (++counter).ToString(CultureInfo.InvariantCulture);
                        bookmarks.Add(bookmark);

                        var level = Math.Clamp(rich.Level, 1, 4);

                        if (level <= depth)
                        {
                            _tocEntries.Add(new TocEntry(string.Concat(rich.Spans.Select(span => span.Text)), level, bookmark));
                        }
                    }

                    _headingBookmarks[block.Id ?? string.Empty] = bookmarks;
                }
            }
        }
    }

    private void AddBodySection(Document document, PdfSectionDefinition definition, bool isFirst)
    {
        var section = document.AddSection();

        _page = ApplyPageSetup(section.PageSetup, definition.PageSetup);

        // Pages are numbered as they stand in the file, the cover and contents included, so the number on
        // a page is the one a viewer and every PDF tool call it by; only an explicit start_at changes that.
        if (isFirst && _definition.PageNumbers?.StartAt is > 0 and var startAt)
        {
            section.PageSetup.StartingNumber = startAt;
        }

        AddRunningHeads(section, !_runningHeadsStarted);
        _runningHeadsStarted = true;

        var blocks = definition.Blocks ?? [];

        if (blocks.Count == 0)
        {
            section.AddParagraph();

            return;
        }

        foreach (var block in blocks)
        {
            AddBlock(section, block);
        }
    }

    /// <summary>
    /// Applies a page setup to a section, filling every gap from the document and then from the host.
    /// </summary>
    /// <param name="target">The section's page setup.</param>
    /// <param name="section">The section's own page setup, or <see langword="null"/>.</param>
    /// <returns>The resolved page geometry.</returns>
    private PageGeometry ApplyPageSetup(PageSetup target, PdfPageSetupDefinition section)
    {
        var geometry = ResolvePageGeometry(section);

        // The dimensions are written out as they are to be printed, landscape included, and the orientation
        // is left as portrait. MigraDoc swaps the dimensions of a landscape section itself, so setting both
        // would turn a landscape page back into a portrait one.
        target.Orientation = Orientation.Portrait;
        target.PageWidth = Unit.FromPoint(geometry.Width);
        target.PageHeight = Unit.FromPoint(geometry.Height);
        target.TopMargin = Unit.FromPoint(geometry.Top);
        target.BottomMargin = Unit.FromPoint(geometry.Bottom);
        target.LeftMargin = Unit.FromPoint(geometry.Left);
        target.RightMargin = Unit.FromPoint(geometry.Right);
        target.HeaderDistance = Unit.FromPoint(Math.Max(18, geometry.Top * 0.45));
        target.FooterDistance = Unit.FromPoint(Math.Max(18, geometry.Bottom * 0.45));

        return geometry;
    }

    private PageGeometry ResolvePageGeometry(PdfPageSetupDefinition section)
    {
        var document = _definition.PageSetup ?? new PdfPageSetupDefinition();
        section ??= new PdfPageSetupDefinition();

        double width;
        double height;

        var customWidth = section.WidthMm ?? (section.Size is null ? document.WidthMm : null);
        var customHeight = section.HeightMm ?? (section.Size is null ? document.HeightMm : null);

        if (customWidth is > 20 && customHeight is > 20)
        {
            width = PdfPageSizes.FromMillimetres(Math.Min(customWidth.Value, 5000));
            height = PdfPageSizes.FromMillimetres(Math.Min(customHeight.Value, 5000));
        }
        else
        {
            var name = section.Size ?? document.Size ?? _options.DefaultPageSize;

            if (!PdfPageSizes.TryGet(name, out width, out height))
            {
                _warnings.Add($"\"{name}\" is not a paper size this renderer knows; {_options.DefaultPageSize} was used. Known sizes: {string.Join(", ", PdfPageSizes.Names)}.");

                if (!PdfPageSizes.TryGet(_options.DefaultPageSize, out width, out height))
                {
                    PdfPageSizes.TryGet("A4", out width, out height);
                }
            }
        }

        var orientation = (section.Orientation ?? document.Orientation)?.Trim().ToLowerInvariant();

        if ((orientation == "landscape" && width < height) || (orientation == "portrait" && width > height))
        {
            (width, height) = (height, width);
        }

        var fallbackMargin = PdfPageSizes.FromMillimetres(Math.Clamp(_options.DefaultMarginMm, 0, 80));

        double Margin(double? sectionValue, double? documentValue)
        {
            var value = sectionValue ?? documentValue;

            return value is >= 0
                ? PdfPageSizes.FromMillimetres(Math.Min(value.Value, 100))
                : fallbackMargin;
        }

        var geometry = new PageGeometry(
            width,
            height,
            Margin(section.MarginLeftMm, document.MarginLeftMm),
            Margin(section.MarginRightMm, document.MarginRightMm),
            Margin(section.MarginTopMm, document.MarginTopMm),
            Margin(section.MarginBottomMm, document.MarginBottomMm));

        if (geometry.UsableWidth < 72 || geometry.UsableHeight < 72)
        {
            _warnings.Add("The margins left less than an inch of printable space, so the default margins were used.");

            geometry = geometry with
            {
                Left = fallbackMargin,
                Right = fallbackMargin,
                Top = fallbackMargin,
                Bottom = fallbackMargin,
            };
        }

        return geometry;
    }

    private void AddBlock(Section section, PdfBlockDefinition block)
    {
        if (block is null)
        {
            return;
        }

        switch (NormalizeType(block))
        {
            case PdfBlockTypes.Heading:
                AddHeading(section, block);

                break;

            case PdfBlockTypes.Paragraph:
                AddParagraph(section, block);

                break;

            case PdfBlockTypes.Markdown:
                AddMarkdown(section, block);

                break;

            case PdfBlockTypes.List:
                AddList(section, block);

                break;

            case PdfBlockTypes.Table:
                if (block.Table is null)
                {
                    _warnings.Add($"Table block {block.Id} has no table.");
                }
                else
                {
                    AddTable(section, block.Table, block);
                }

                break;

            case PdfBlockTypes.Image:
                AddImage(section, block.Image, block);

                break;

            case PdfBlockTypes.Chart:
                if (block.Chart is null)
                {
                    _warnings.Add($"Chart block {block.Id} has no chart.");
                }
                else
                {
                    AddChart(section, block.Chart);
                }

                break;

            case PdfBlockTypes.PageBreak:
                section.AddPageBreak();

                break;

            case PdfBlockTypes.Spacer:
                AddSpacer(section, block.Height ?? 12);

                break;

            case PdfBlockTypes.Rule:
                AddRule(section, block);

                break;

            case PdfBlockTypes.Quote:
                AddQuote(section, block.Text, block);

                break;

            case PdfBlockTypes.Code:
                AddCode(section, block.Text);

                break;

            case PdfBlockTypes.Callout:
                AddCallout(section, block);

                break;

            case PdfBlockTypes.KeyValue:
                AddKeyValues(section, block);

                break;

            case PdfBlockTypes.SignatureLines:
                AddSignatureLines(section, block);

                break;

            default:
                if (!string.IsNullOrWhiteSpace(block.Text))
                {
                    _warnings.Add($"Block {block.Id} has the unknown type \"{block.Type}\" and was drawn as a paragraph. Types: {string.Join(", ", PdfBlockTypes.All)}.");
                    AddParagraph(section, block);
                }
                else
                {
                    _warnings.Add($"Block {block.Id} has the unknown type \"{block.Type}\" and was left out.");
                }

                break;
        }
    }

    /// <summary>
    /// Reads a block's type, accepting the synonyms a model reaches for.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>The canonical type.</returns>
    public static string NormalizeType(PdfBlockDefinition block)
    {
        var type = block?.Type?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

        return type switch
        {
            null or "" => string.IsNullOrWhiteSpace(block?.Text) ? string.Empty : PdfBlockTypes.Paragraph,
            "h1" or "h2" or "h3" or "h4" or "title" or "header" or "section_heading" => PdfBlockTypes.Heading,
            "text" or "p" or "body" or "para" => PdfBlockTypes.Paragraph,
            "md" or "rich_text" or "html" => PdfBlockTypes.Markdown,
            "bullets" or "bullet_list" or "ul" or "ol" or "numbered_list" or "bulleted_list" => PdfBlockTypes.List,
            "img" or "picture" or "photo" or "logo" or "figure" => PdfBlockTypes.Image,
            "graph" => PdfBlockTypes.Chart,
            "break" or "pagebreak" or "new_page" => PdfBlockTypes.PageBreak,
            "space" or "gap" => PdfBlockTypes.Spacer,
            "hr" or "divider" or "horizontal_rule" or "line" => PdfBlockTypes.Rule,
            "blockquote" => PdfBlockTypes.Quote,
            "pre" or "code_block" => PdfBlockTypes.Code,
            "note" or "box" or "alert" or "admonition" => PdfBlockTypes.Callout,
            "kv" or "key_values" or "keyvalue" or "facts" or "details" => PdfBlockTypes.KeyValue,
            "signature" or "signatures" or "signature_line" => PdfBlockTypes.SignatureLines,
            _ => type,
        };
    }

    private void AddHeading(Section section, PdfBlockDefinition block)
    {
        var level = block.Level ?? LevelFromType(block.Type);
        var paragraph = section.AddParagraph();

        paragraph.Style = "Heading" + Math.Clamp(level, 1, 4).ToString(CultureInfo.InvariantCulture);
        ApplyBlockFormat(paragraph.Format, block);

        if (_headingBookmarks.TryGetValue(block.Id ?? string.Empty, out var bookmarks) && bookmarks.Count > 0)
        {
            paragraph.AddBookmark(bookmarks[0]);
        }

        AddInline(paragraph, block.Text, block.Bold, block.Italic);
    }

    private static int LevelFromType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "h2" => 2,
            "h3" => 3,
            "h4" => 4,
            _ => 1,
        };
    }

    private void AddParagraph(Section section, PdfBlockDefinition block)
    {
        var paragraph = section.AddParagraph();

        ApplyBlockFormat(paragraph.Format, block);
        AddInline(paragraph, block.Text, block.Bold, block.Italic);
    }

    private void AddMarkdown(Section section, PdfBlockDefinition block)
    {
        if (string.IsNullOrWhiteSpace(block.Text))
        {
            return;
        }

        _headingBookmarks.TryGetValue(block.Id ?? string.Empty, out var bookmarks);

        var headingIndex = 0;

        foreach (var rich in RichTextParser.Parse(block.Text))
        {
            switch (rich.Kind)
            {
                case RichTextBlockKind.Heading:
                    {
                        var paragraph = section.AddParagraph();
                        paragraph.Style = "Heading" + Math.Clamp(rich.Level, 1, 4).ToString(CultureInfo.InvariantCulture);

                        if (bookmarks is not null && headingIndex < bookmarks.Count)
                        {
                            paragraph.AddBookmark(bookmarks[headingIndex]);
                        }

                        headingIndex++;
                        AddSpans(paragraph, rich.Spans);

                        break;
                    }

                case RichTextBlockKind.BulletItem:
                    AddListItem(section, rich.Spans, "•", Math.Max(0, rich.Level - 1), block);

                    break;

                case RichTextBlockKind.NumberedItem:
                    AddListItem(section, rich.Spans, rich.Number.ToString(CultureInfo.InvariantCulture) + ".", Math.Max(0, rich.Level - 1), block);

                    break;

                case RichTextBlockKind.Quote:
                    {
                        var paragraph = section.AddParagraph();
                        paragraph.Style = QuoteStyle;
                        AddSpans(paragraph, rich.Spans);

                        break;
                    }

                case RichTextBlockKind.Code:
                    AddCode(section, rich.Text);

                    break;

                case RichTextBlockKind.HorizontalRule:
                    AddRule(section, null);

                    break;

                case RichTextBlockKind.Table:
                    if (rich.Table is { ColumnCount: > 0 })
                    {
                        AddTable(section, FromRichTable(rich.Table), null);
                    }

                    break;

                default:
                    {
                        var paragraph = section.AddParagraph();
                        ApplyBlockFormat(paragraph.Format, block);
                        AddSpans(paragraph, rich.Spans);

                        break;
                    }
            }
        }
    }

    private static PdfTableDefinition FromRichTable(RichTextTable table)
    {
        var definition = new PdfTableDefinition();

        for (var index = 0; index < table.ColumnCount; index++)
        {
            definition.Columns.Add(new PdfTableColumnDefinition
            {
                Header = index < table.Header.Cells.Count ? PlainText(table.Header.Cells[index].Spans) : string.Empty,
            });
        }

        foreach (var row in table.Rows)
        {
            definition.Rows.Add([.. row.Cells.Select(cell => PlainText(cell.Spans))]);
        }

        return definition;
    }

    private static string PlainText(IEnumerable<RichTextSpan> spans)
    {
        return string.Concat(spans.Select(span => span.Text));
    }

    private void AddList(Section section, PdfBlockDefinition block)
    {
        var items = block.Items ?? [];

        if (items.Count == 0 && !string.IsNullOrWhiteSpace(block.Text))
        {
            items = [.. block.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)];
        }

        var ordered = block.Ordered == true || block.Type?.Trim().ToLowerInvariant() is "ol" or "numbered_list";
        var counters = new int[8];

        foreach (var raw in items)
        {
            if (raw is null)
            {
                continue;
            }

            // Two spaces or a tab of indentation per level, the way a model writes a nested list by hand.
            var indentation = raw.Length - raw.TrimStart(' ', '\t').Length + raw.TakeWhile(character => character == '\t').Count();
            var level = Math.Clamp(indentation / 2, 0, 7);
            var text = raw.Trim();

            if (text.StartsWith("- ", StringComparison.Ordinal) || text.StartsWith("* ", StringComparison.Ordinal) || text.StartsWith("• ", StringComparison.Ordinal))
            {
                text = text[2..];
            }

            counters[level]++;

            for (var deeper = level + 1; deeper < counters.Length; deeper++)
            {
                counters[deeper] = 0;
            }

            var marker = ordered
                ? counters[level].ToString(CultureInfo.InvariantCulture) + "."
                : level % 2 == 0 ? "•" : "–";

            AddListItem(section, RichTextParser.ParseInline(text), marker, level, block);
        }
    }

    private void AddListItem(Section section, IEnumerable<RichTextSpan> spans, string marker, int level, PdfBlockDefinition block)
    {
        var paragraph = section.AddParagraph();

        ApplyBlockFormat(paragraph.Format, block);
        paragraph.Format.LeftIndent = Unit.FromCentimeter(0.75 + (level * 0.6));

        // The marker hangs to the left of the wrapped text, so a long item stays aligned under itself.
        paragraph.Format.FirstLineIndent = Unit.FromCentimeter(-0.45);
        paragraph.Format.SpaceAfter = Unit.FromPoint(2.5);
        paragraph.Format.TabStops.ClearAll();
        paragraph.Format.TabStops.AddTabStop(Unit.FromCentimeter(0.75 + (level * 0.6)));

        var markerText = paragraph.AddFormattedText(marker);
        markerText.Color = _theme.Primary.ToMigraDoc();
        paragraph.AddTab();

        AddSpans(paragraph, spans, block?.Bold == true, block?.Italic == true);
    }

    private static void AddSpacer(Section section, double height)
    {
        var paragraph = section.AddParagraph();

        paragraph.Format.LineSpacingRule = LineSpacingRule.Exactly;
        paragraph.Format.LineSpacing = Unit.FromPoint(1);
        paragraph.Format.SpaceBefore = 0;
        paragraph.Format.SpaceAfter = Unit.FromPoint(Math.Clamp(height, 0, 400));
        paragraph.Format.Font.Size = 1;
    }

    private void AddRule(Section section, PdfBlockDefinition block)
    {
        var paragraph = section.AddParagraph();
        var color = PdfResolvedTheme.ReadColor(block?.Color, _theme.Muted.Blend(_white, 0.5), "color", _warnings);

        paragraph.Format.Borders.Bottom.Width = 0.75;
        paragraph.Format.Borders.Bottom.Color = color.ToMigraDoc();
        paragraph.Format.SpaceBefore = Unit.FromPoint(block?.SpaceBefore ?? 4);
        paragraph.Format.SpaceAfter = Unit.FromPoint(block?.SpaceAfter ?? 10);
        paragraph.Format.LineSpacingRule = LineSpacingRule.Exactly;
        paragraph.Format.LineSpacing = Unit.FromPoint(2);
        paragraph.Format.Font.Size = 1;
    }

    private void AddQuote(Section section, string text, PdfBlockDefinition block)
    {
        var paragraph = section.AddParagraph();

        paragraph.Style = QuoteStyle;
        ApplyBlockFormat(paragraph.Format, block);
        AddInline(paragraph, text, block?.Bold, block?.Italic);
    }

    private static void AddCode(Section section, string text)
    {
        var paragraph = section.AddParagraph();

        paragraph.Style = CodeStyle;

        var lines = (text ?? string.Empty).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                paragraph.AddLineBreak();
            }

            paragraph.AddText(lines[index].TrimEnd('\r'));
        }
    }

    private void AddImage(Section section, PdfImageDefinition image, PdfBlockDefinition block)
    {
        if (image is null || string.IsNullOrWhiteSpace(image.Source))
        {
            _warnings.Add($"Image block {block?.Id} names no source.");

            return;
        }

        if (!TryGetImage(image.Source, out var data, out var pixelWidth, out var pixelHeight))
        {
            _missingImageCount++;
            _warnings.Add($"The image \"{image.Source}\" could not be found or is not a JPEG, PNG, GIF or BMP.");

            return;
        }

        var paragraph = section.AddParagraph();
        paragraph.Format.Alignment = ReadAlignment(block?.Align, ParagraphAlignment.Center);
        paragraph.Format.SpaceBefore = Unit.FromPoint(block?.SpaceBefore ?? 4);
        paragraph.Format.SpaceAfter = Unit.FromPoint(string.IsNullOrWhiteSpace(image.Caption) ? block?.SpaceAfter ?? 8 : 2);
        paragraph.Format.KeepWithNext = !string.IsNullOrWhiteSpace(image.Caption);

        var picture = paragraph.AddImage(ToImageName(data));
        var (width, height) = FitImage(image, pixelWidth, pixelHeight, _page.UsableWidth, _page.UsableHeight * 0.85);

        picture.Width = Unit.FromPoint(width);
        picture.Height = Unit.FromPoint(height);
        picture.LockAspectRatio = false;

        AddCaption(section, image.Caption);
    }

    /// <summary>
    /// Sizes a picture: as asked, or at its natural size capped to the space available, keeping its shape
    /// whenever only one dimension was given.
    /// </summary>
    private static (double Width, double Height) FitImage(
        PdfImageDefinition image,
        int pixelWidth,
        int pixelHeight,
        double maxWidth,
        double maxHeight)
    {
        var aspect = pixelHeight > 0 ? (double)pixelWidth / pixelHeight : 1;

        // Pictures are assumed to be 96 dpi, the resolution screenshots and web images are made at.
        var naturalWidth = pixelWidth * 0.75;

        double width;
        double height;

        if (image.Width is > 0 && image.Height is > 0)
        {
            width = image.Width.Value;
            height = image.Height.Value;
        }
        else if (image.Width is > 0 || image.WidthPercent is > 0)
        {
            width = image.Width ?? (maxWidth * Math.Clamp(image.WidthPercent.Value, 1, 100) / 100);
            height = width / aspect;
        }
        else if (image.Height is > 0)
        {
            height = image.Height.Value;
            width = height * aspect;
        }
        else
        {
            width = Math.Min(naturalWidth, maxWidth);
            height = width / aspect;
        }

        if (width > maxWidth)
        {
            height *= maxWidth / width;
            width = maxWidth;
        }

        if (height > maxHeight)
        {
            width *= maxHeight / height;
            height = maxHeight;
        }

        return (Math.Max(width, 4), Math.Max(height, 4));
    }

    private bool TryGetImage(string source, out PdfImageData data, out int pixelWidth, out int pixelHeight)
    {
        pixelWidth = 0;
        pixelHeight = 0;

        if (string.IsNullOrWhiteSpace(source) ||
            !_images.TryGetValue(source.Trim(), out data) ||
            data?.Bytes is not { Length: > 0 })
        {
            data = null;

            return false;
        }

        return PdfImageInfo.TryRead(data.Bytes, out _, out pixelWidth, out pixelHeight);
    }

    /// <summary>
    /// Builds the name MigraDoc reads an in-memory picture from.
    /// </summary>
    /// <param name="data">The picture.</param>
    /// <returns>The image name.</returns>
    private static string ToImageName(PdfImageData data)
    {
        return "base64:" + Convert.ToBase64String(data.Bytes);
    }

    private void AddCaption(Section section, string caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return;
        }

        var paragraph = section.AddParagraph();
        paragraph.Style = CaptionStyle;
        paragraph.Format.Alignment = ParagraphAlignment.Center;
        AddInline(paragraph, caption, null, null);
    }

    private void AddCallout(Section section, PdfBlockDefinition block)
    {
        var accent = (block.Variant?.Trim().ToLowerInvariant()) switch
        {
            "success" or "tip" => new PdfColor(0x2E, 0x7D, 0x32),
            "warning" or "caution" => new PdfColor(0xED, 0x7D, 0x31),
            "danger" or "error" or "important" => new PdfColor(0xC0, 0x00, 0x00),
            "note" or "neutral" => _theme.Muted,
            _ => _theme.Accent,
        };

        accent = PdfResolvedTheme.ReadColor(block.Color, accent, "color", _warnings);

        var background = PdfResolvedTheme.ReadColor(block.BackgroundColor, accent.Blend(_white, 0.9), "background_color", _warnings);
        var table = section.AddTable();

        table.Borders.Visible = false;
        table.LeftPadding = Unit.FromPoint(10);
        table.RightPadding = Unit.FromPoint(10);
        table.TopPadding = Unit.FromPoint(6);
        table.BottomPadding = Unit.FromPoint(6);
        table.KeepTogether = true;
        table.AddColumn(Unit.FromPoint(_page.UsableWidth));

        var row = table.AddRow();
        var cell = row.Cells[0];

        cell.Shading.Color = background.ToMigraDoc();
        cell.Borders.Left.Visible = true;
        cell.Borders.Left.Width = 3;
        cell.Borders.Left.Color = accent.ToMigraDoc();

        if (!string.IsNullOrWhiteSpace(block.Title))
        {
            var title = cell.AddParagraph();
            title.Format.Font.Bold = true;
            title.Format.Font.Color = accent.ToMigraDoc();
            title.Format.SpaceAfter = Unit.FromPoint(2);
            AddInline(title, block.Title, null, null);
        }

        foreach (var line in (block.Text ?? string.Empty).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var paragraph = cell.AddParagraph();
            paragraph.Format.SpaceAfter = Unit.FromPoint(2);
            AddInline(paragraph, line.Trim(), block.Bold, block.Italic);
        }

        AddSpacer(section, block.SpaceAfter ?? 8);
    }

    private void AddKeyValues(Section section, PdfBlockDefinition block)
    {
        var pairs = new List<(string Label, string Value)>();

        foreach (var pair in block.Pairs ?? [])
        {
            if (pair is { Count: > 0 })
            {
                pairs.Add((pair[0], pair.Count > 1 ? pair[1] : string.Empty));
            }
        }

        foreach (var item in block.Items ?? [])
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var separator = item.IndexOf(':', StringComparison.Ordinal);

            pairs.Add(separator > 0
                ? (item[..separator].Trim(), item[(separator + 1)..].Trim())
                : (item.Trim(), string.Empty));
        }

        if (pairs.Count == 0)
        {
            return;
        }

        var table = section.AddTable();

        table.Borders.Visible = false;
        table.LeftPadding = 0;
        table.RightPadding = Unit.FromPoint(6);
        table.TopPadding = Unit.FromPoint(1.5);
        table.BottomPadding = Unit.FromPoint(1.5);

        // A short fact list reads as one thing; split across a page break it reads as two.
        table.KeepTogether = pairs.Count <= 15;
        table.AddColumn(Unit.FromPoint(_page.UsableWidth * 0.32));
        table.AddColumn(Unit.FromPoint(_page.UsableWidth * 0.68));

        foreach (var (label, value) in pairs)
        {
            var row = table.AddRow();
            var labelParagraph = row.Cells[0].AddParagraph();
            labelParagraph.Format.Font.Bold = true;
            labelParagraph.Format.Font.Color = _theme.Muted.ToMigraDoc();
            AddInline(labelParagraph, label, null, null);

            var valueParagraph = row.Cells[1].AddParagraph();
            AddInline(valueParagraph, value, block.Bold, block.Italic);
        }

        AddSpacer(section, block.SpaceAfter ?? 8);
    }

    private void AddSignatureLines(Section section, PdfBlockDefinition block)
    {
        var labels = (block.Items ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).ToList();

        if (labels.Count == 0)
        {
            labels = ["Signature", "Date"];
        }

        var perRow = Math.Min(labels.Count, 3);
        var table = section.AddTable();
        var columnWidth = _page.UsableWidth / perRow;

        table.Borders.Visible = false;
        table.LeftPadding = 0;
        table.RightPadding = Unit.FromPoint(18);
        table.KeepTogether = true;

        for (var index = 0; index < perRow; index++)
        {
            table.AddColumn(Unit.FromPoint(columnWidth));
        }

        for (var start = 0; start < labels.Count; start += perRow)
        {
            var row = table.AddRow();

            for (var index = 0; index < perRow && start + index < labels.Count; index++)
            {
                var line = row.Cells[index].AddParagraph();
                line.Format.SpaceBefore = Unit.FromPoint(34);
                line.Format.Borders.Top.Width = 0.75;
                line.Format.Borders.Top.Color = _theme.Text.ToMigraDoc();
                line.Format.Font.Size = Math.Max(6, _theme.BaseFontSize - 1.5);
                line.Format.Font.Color = _theme.Muted.ToMigraDoc();
                line.AddText(labels[start + index]);
            }
        }

        AddSpacer(section, block.SpaceAfter ?? 10);
    }

    /// <summary>
    /// Applies a block's own formatting over its style.
    /// </summary>
    private void ApplyBlockFormat(ParagraphFormat format, PdfBlockDefinition block)
    {
        if (block is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(block.Align))
        {
            format.Alignment = ReadAlignment(block.Align, format.Alignment);
        }

        if (block.FontSize is > 0)
        {
            format.Font.Size = Math.Clamp(block.FontSize.Value, 4, 96);
        }

        if (!string.IsNullOrWhiteSpace(block.FontFamily) && !_forceDefaultFont)
        {
            format.Font.Name = PdfFontFamilies.Resolve(block.FontFamily, _theme.FontFamily, _warnings);
        }

        if (!string.IsNullOrWhiteSpace(block.Color))
        {
            format.Font.Color = PdfResolvedTheme.ReadColor(block.Color, _theme.Text, "color", _warnings).ToMigraDoc();
        }

        if (!string.IsNullOrWhiteSpace(block.BackgroundColor))
        {
            format.Shading.Color = PdfResolvedTheme.ReadColor(block.BackgroundColor, _white, "background_color", _warnings).ToMigraDoc();
        }

        if (block.SpaceBefore is >= 0)
        {
            format.SpaceBefore = Unit.FromPoint(Math.Min(block.SpaceBefore.Value, 400));
        }

        if (block.SpaceAfter is >= 0)
        {
            format.SpaceAfter = Unit.FromPoint(Math.Min(block.SpaceAfter.Value, 400));
        }

        if (block.KeepWithNext == true)
        {
            format.KeepWithNext = true;
        }
    }

    private static ParagraphAlignment ReadAlignment(string value, ParagraphAlignment fallback)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "left" or "start" => ParagraphAlignment.Left,
            "center" or "centre" or "middle" => ParagraphAlignment.Center,
            "right" or "end" => ParagraphAlignment.Right,
            "justify" or "justified" => ParagraphAlignment.Justify,
            _ => fallback,
        };
    }

    private void AddInline(Paragraph paragraph, string text, bool? bold, bool? italic)
    {
        if (string.IsNullOrEmpty(text))
        {
            paragraph.AddText(string.Empty);

            return;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                paragraph.AddLineBreak();
            }

            AddSpans(paragraph, RichTextParser.ParseInline(lines[index]), bold == true, italic == true);
        }
    }

    private void AddSpans(Paragraph paragraph, IEnumerable<RichTextSpan> spans, bool forceBold = false, bool forceItalic = false)
    {
        var wroteAnything = false;

        foreach (var span in spans)
        {
            if (string.IsNullOrEmpty(span.Text))
            {
                continue;
            }

            FormattedText formatted;

            if (IsWebLink(span.Link))
            {
                var hyperlink = paragraph.AddHyperlink(span.Link, HyperlinkType.Web);
                formatted = hyperlink.AddFormattedText(span.Text);
                formatted.Font.Color = _theme.Accent.ToMigraDoc();
                formatted.Font.Underline = Underline.Single;
            }
            else
            {
                formatted = paragraph.AddFormattedText(span.Text);
            }

            if (span.Bold || forceBold)
            {
                formatted.Bold = true;
            }

            if (span.Italic || forceItalic)
            {
                formatted.Italic = true;
            }

            if (span.Strikethrough)
            {
                // MigraDoc has no strike-through, so the run is marked the way a reader still reads as
                // removed rather than silently losing the distinction.
                formatted.Font.Color = _theme.Muted.ToMigraDoc();
                formatted.Italic = true;
            }

            if (span.Code)
            {
                formatted.Font.Name = _forceDefaultFont ? PdfFontFamilies.Default : PdfFontFamilies.Monospace;
                formatted.Font.Size = Math.Max(6, _theme.BaseFontSize - 1);
            }

            wroteAnything = true;
        }

        if (!wroteAnything)
        {
            // An empty paragraph still has to occupy its line, or the surrounding spacing collapses.
            paragraph.AddText(string.Empty);
        }
    }

    private static bool IsWebLink(string link)
    {
        return !string.IsNullOrWhiteSpace(link) &&
            Uri.TryCreate(link, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto);
    }

    private static string SafeId(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "x";
        }

        var builder = new System.Text.StringBuilder(id.Length);

        foreach (var character in id)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Adds the running heads and feet to a body section.
    /// </summary>
    private void AddRunningHeads(Section section, bool isFirst)
    {
        var numbers = _definition.PageNumbers;

        // A header or footer that already writes {page} is the page number; adding page_numbers to it as
        // well would print "Page 1 of 3 Page 1 of 3".
        var numbersEnabled = numbers?.Enabled == true && !ShowsPageNumber(_definition.Header) && !ShowsPageNumber(_definition.Footer);
        var numbersPosition = (numbers?.Position ?? "footer-center").Trim().ToLowerInvariant();
        var numbersTemplate = string.IsNullOrWhiteSpace(numbers?.Template) ? "Page {page} of {pages}" : numbers.Template;
        var numbersInHeader = numbersPosition.StartsWith("header", StringComparison.Ordinal) || numbersPosition.StartsWith("top", StringComparison.Ordinal);
        var numbersSlot = numbersPosition.EndsWith("left", StringComparison.Ordinal)
            ? "left"
            : numbersPosition.EndsWith("right", StringComparison.Ordinal) ? "right" : "center";

        var header = _definition.Header;
        var footer = _definition.Footer;
        var logoInHeader = _theme.Logo is not null && _theme.LogoPosition.StartsWith("header", StringComparison.Ordinal);
        var logoInFooter = _theme.Logo is not null && _theme.LogoPosition.StartsWith("footer", StringComparison.Ordinal);

        var differentFirst = isFirst &&
            ((numbersEnabled && numbers.SkipFirstPage == true) ||
             header?.ShowOnFirstPage == false ||
             footer?.ShowOnFirstPage == false);

        section.PageSetup.DifferentFirstPageHeaderFooter = differentFirst;

        FillRunningHead(section.Headers.Primary, header, isHeader: true, numbersEnabled && numbersInHeader, numbersSlot, numbersTemplate, logoInHeader);
        FillRunningHead(section.Footers.Primary, footer, isHeader: false, numbersEnabled && !numbersInHeader, numbersSlot, numbersTemplate, logoInFooter);

        if (differentFirst)
        {
            var skipNumbers = numbers?.SkipFirstPage == true;

            if (header?.ShowOnFirstPage != false)
            {
                FillRunningHead(section.Headers.FirstPage, header, isHeader: true, numbersEnabled && numbersInHeader && !skipNumbers, numbersSlot, numbersTemplate, logoInHeader);
            }

            if (footer?.ShowOnFirstPage != false)
            {
                FillRunningHead(section.Footers.FirstPage, footer, isHeader: false, numbersEnabled && !numbersInHeader && !skipNumbers, numbersSlot, numbersTemplate, logoInFooter);
            }
        }
    }

    private void FillRunningHead(
        HeaderFooter target,
        PdfHeaderFooterDefinition slots,
        bool isHeader,
        bool includeNumbers,
        string numbersSlot,
        string numbersTemplate,
        bool includeLogo)
    {
        var left = slots?.Left;
        var center = slots?.Center;
        var right = slots?.Right;

        if (includeNumbers)
        {
            switch (numbersSlot)
            {
                case "left":
                    left = Join(left, numbersTemplate);

                    break;
                case "right":
                    right = Join(right, numbersTemplate);

                    break;
                default:
                    center = Join(center, numbersTemplate);

                    break;
            }
        }

        var hasText = !string.IsNullOrWhiteSpace(left) || !string.IsNullOrWhiteSpace(center) || !string.IsNullOrWhiteSpace(right);

        if (!hasText && !includeLogo)
        {
            return;
        }

        var paragraph = target.AddParagraph();

        if (slots?.FontSize is > 0)
        {
            paragraph.Format.Font.Size = Math.Clamp(slots.FontSize.Value, 5, 24);
        }

        if (!string.IsNullOrWhiteSpace(slots?.Color))
        {
            paragraph.Format.Font.Color = PdfResolvedTheme.ReadColor(slots.Color, _theme.Muted, "color", _warnings).ToMigraDoc();
        }

        paragraph.Format.TabStops.ClearAll();
        paragraph.Format.TabStops.AddTabStop(Unit.FromPoint(_page.UsableWidth / 2), TabAlignment.Center);
        paragraph.Format.TabStops.AddTabStop(Unit.FromPoint(_page.UsableWidth), TabAlignment.Right);

        if (slots?.Separator == true)
        {
            var border = isHeader ? paragraph.Format.Borders.Bottom : paragraph.Format.Borders.Top;
            border.Width = 0.5;
            border.Color = _theme.Muted.Blend(_white, 0.55).ToMigraDoc();

            if (isHeader)
            {
                paragraph.Format.Borders.DistanceFromBottom = Unit.FromPoint(3);
            }
            else
            {
                paragraph.Format.Borders.DistanceFromTop = Unit.FromPoint(3);
            }
        }

        var logoLeft = includeLogo && !_theme.LogoPosition.EndsWith("right", StringComparison.Ordinal);
        var logoRight = includeLogo && !logoLeft;

        if (logoLeft)
        {
            AddLogo(paragraph);
            paragraph.AddSpace(2);
        }

        AppendTokens(paragraph, left);

        if (!string.IsNullOrWhiteSpace(center) || !string.IsNullOrWhiteSpace(right) || logoRight)
        {
            paragraph.AddTab();
            AppendTokens(paragraph, center);
        }

        if (!string.IsNullOrWhiteSpace(right) || logoRight)
        {
            paragraph.AddTab();
            AppendTokens(paragraph, right);

            if (logoRight)
            {
                paragraph.AddSpace(2);
                AddLogo(paragraph);
            }
        }
    }

    private void AddLogo(Paragraph paragraph)
    {
        if (!TryGetImage(_theme.Logo, out var data, out var pixelWidth, out var pixelHeight))
        {
            _missingImageCount++;

            return;
        }

        var image = paragraph.AddImage(ToImageName(data));
        var height = _theme.LogoHeight;

        image.Height = Unit.FromPoint(height);
        image.Width = Unit.FromPoint(pixelHeight > 0 ? height * pixelWidth / pixelHeight : height);
        image.LockAspectRatio = false;
    }

    private static string Join(string existing, string addition)
    {
        return string.IsNullOrWhiteSpace(existing)
            ? addition
            : existing + "   " + addition;
    }

    /// <summary>
    /// Writes running head text, turning its tokens into the fields the renderer fills in page by page.
    /// </summary>
    private void AppendTokens(Paragraph paragraph, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var format = PageNumberFormat();
        var position = 0;

        while (position < text.Length)
        {
            var open = text.IndexOf('{', position);

            if (open < 0)
            {
                paragraph.AddText(text[position..]);

                break;
            }

            var close = text.IndexOf('}', open + 1);

            if (close < 0)
            {
                paragraph.AddText(text[position..]);

                break;
            }

            if (open > position)
            {
                paragraph.AddText(text[position..open]);
            }

            var token = text[(open + 1)..close].Trim().ToLowerInvariant();

            switch (token)
            {
                case "page":
                    {
                        var field = paragraph.AddPageField();

                        if (!string.IsNullOrEmpty(format))
                        {
                            field.Format = format;
                        }

                        break;
                    }

                case "pages":
                case "total":
                    {
                        // "of 3" counts every page of the file, as the page numbers do.
                        var field = paragraph.AddNumPagesField();

                        if (!string.IsNullOrEmpty(format))
                        {
                            field.Format = format;
                        }

                        break;
                    }

                case "title":
                    paragraph.AddText(_definition.Title ?? string.Empty);

                    break;

                case "author":
                    paragraph.AddText(_definition.Author ?? string.Empty);

                    break;

                case "date":
                    paragraph.AddText(_dateText ?? string.Empty);

                    break;

                default:
                    paragraph.AddText(text[open..(close + 1)]);

                    break;
            }

            position = close + 1;
        }
    }

    /// <summary>
    /// Returns whether a header or footer writes the page number itself.
    /// </summary>
    /// <param name="slots">The header or footer.</param>
    /// <returns><see langword="true"/> when any of its slots holds a <c>{page}</c> token.</returns>
    internal static bool ShowsPageNumber(PdfHeaderFooterDefinition slots)
    {
        return slots is not null && new[] { slots.Left, slots.Center, slots.Right }.Any(text =>
            text is not null && text.Replace(" ", string.Empty, StringComparison.Ordinal).Contains("{page}", StringComparison.OrdinalIgnoreCase));
    }

    private string PageNumberFormat()
    {
        return _definition.PageNumbers?.Format?.Trim() switch
        {
            "i" or "roman" => "roman",
            "I" or "ROMAN" => "ROMAN",
            "a" or "alphabetic" => "alphabetic",
            "A" or "ALPHABETIC" => "ALPHABETIC",
            _ => null,
        };
    }

    /// <summary>
    /// A heading listed in the table of contents.
    /// </summary>
    /// <param name="Text">The heading text.</param>
    /// <param name="Level">The heading level.</param>
    /// <param name="Bookmark">The bookmark the entry links to.</param>
    private readonly record struct TocEntry(string Text, int Level, string Bookmark);

    /// <summary>
    /// The size and margins of the page a section is printed on, in points.
    /// </summary>
    private readonly record struct PageGeometry(double Width, double Height, double Left, double Right, double Top, double Bottom)
    {
        public double UsableWidth => Width - Left - Right;

        public double UsableHeight => Height - Top - Bottom;
    }
}
