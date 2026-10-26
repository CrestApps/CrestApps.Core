using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Generation.RichText;
using CrestApps.Core.AI.Documents.Pdf.Composition.Layout;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Images;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Turns a <see cref="PdfDocumentDefinition"/> into a layout document: styles from the theme, a cover, a
/// table of contents, the body sections with their running heads and feet, and every block of content.
/// </summary>
/// <remarks>
/// The builder only lays out; it never reads a file or calls out. Everything it needs — the pictures in
/// particular — is resolved before it runs, so the same definition always produces the same document.
/// </remarks>
internal sealed partial class PdfLayoutBuilder
{
    private const string TitleStyle = "DocTitle";
    private const string CaptionStyle = "Caption";
    private const string QuoteStyle = "Quote";
    private const string CodeStyle = "Code";
    private const string TocTitleStyle = "TocTitle";
    private const string TocEntryStylePrefix = "TocEntry";

    // Points in a centimetre.
    private const double Centimetre = 72 / 2.54;

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
    private readonly Dictionary<string, PdfImage> _loadedImages = new(StringComparer.Ordinal);

    private PdfResolvedTheme _theme;
    private PageGeometry _page;
    private int _missingImageCount;

    // Whether a section before this one already carries the running heads, so this one does not start them.
    private bool _runningHeadsStarted;

    // Whether only the body is numbered: start_at 1 behind a cover or contents page, "number the report from
    // one and do not count the front matter". Then "of {pages}" counts the body as well.
    private bool _numberBodyOnly;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutBuilder"/> class.
    /// </summary>
    /// <param name="definition">The document to build.</param>
    /// <param name="images">The pictures the document refers to, already resolved, keyed by source.</param>
    /// <param name="options">The host defaults.</param>
    /// <param name="dateText">The text the <c>{date}</c> token prints.</param>
    /// <param name="forceDefaultFont">Whether every font is replaced by the default, used when rendering with the requested fonts failed.</param>
    /// <param name="warnings">Receives notes about anything that could not be built as described.</param>
    public PdfLayoutBuilder(
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
    /// <returns>The layout document.</returns>
    public PdfLayoutDocument Build()
    {
        _theme = PdfResolvedTheme.Resolve(_definition.Theme, _options.DefaultFontFamily, _warnings);

        if (_forceDefaultFont)
        {
            // Only the fonts are dropped; the colours and table style still apply.
            _theme = _theme.WithFonts(PdfFontFamilies.Default);
        }

        var document = new PdfLayoutDocument
        {
            Title = _definition.Title ?? string.Empty,
            Author = _definition.Author ?? string.Empty,
            Subject = _definition.Subject ?? string.Empty,
            Keywords = _definition.Keywords ?? string.Empty,
        };

        DefineStyles(document);
        CollectHeadings();

        var coverEnabled = IsCoverEnabled();
        var tocEnabled = _definition.TableOfContents?.Enabled == true;

        _numberBodyOnly = _definition.PageNumbers?.StartAt == 1 &&
            (coverEnabled || tocEnabled) &&
            (_definition.Sections?.Count ?? 1) <= 1;

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

    private static double Cm(double centimetres)
    {
        return centimetres * Centimetre;
    }

    private bool IsCoverEnabled()
    {
        // A cover asked for is always drawn: dropping it because no title was given silently loses a page the
        // user asked for. The cover writes what stands in for the title, and says so.
        var cover = _definition.CoverPage;

        return cover is not null && (cover.Enabled ?? true);
    }

    private void DefineStyles(PdfLayoutDocument document)
    {
        var normal = document.Styles[PdfLayoutStyles.Normal];
        normal.Font.Name = _theme.FontFamily;
        normal.Font.Size = _theme.BaseFontSize;
        normal.Font.Color = _theme.Text;
        normal.ParagraphFormat.SpaceAfter = Math.Round(_theme.BaseFontSize * 0.6, 1);
        normal.ParagraphFormat.LineSpacingRule = PdfLayoutLineSpacing.Multiple;
        normal.ParagraphFormat.LineSpacing = _theme.LineSpacing;

        double[] sizes = [1.9, 1.45, 1.2, 1.05];
        double[] spaceBefore = [18, 14, 11, 9];

        for (var level = 1; level <= 4; level++)
        {
            var style = document.Styles[PdfLayoutStyles.Heading(level)];
            style.Font.Name = _theme.HeadingFontFamily;
            style.Font.Size = Math.Round(_theme.BaseFontSize * sizes[level - 1], 1);
            style.Font.Bold = true;
            style.Font.Color = _theme.Heading;
            style.ParagraphFormat.SpaceBefore = spaceBefore[level - 1];
            style.ParagraphFormat.SpaceAfter = level == 1 ? 8 : 5;
            style.ParagraphFormat.KeepWithNext = true;
            style.ParagraphFormat.LineSpacingRule = PdfLayoutLineSpacing.Single;
            style.ParagraphFormat.OutlineLevel = level;

            if (level == 1)
            {
                // A hairline under the top level heading is what makes a long report scannable; it is the
                // one ornament every level-one section carries.
                style.ParagraphFormat.Borders.Bottom.Width = 0.75;
                style.ParagraphFormat.Borders.Bottom.Color = _theme.Heading.Blend(_white, 0.65);
                style.ParagraphFormat.Borders.DistanceFromBottom = 3;
            }
        }

        var title = document.Styles.AddStyle(TitleStyle, PdfLayoutStyles.Normal);
        title.Font.Name = _theme.HeadingFontFamily;
        title.Font.Size = Math.Round(_theme.BaseFontSize * 2.3, 1);
        title.Font.Bold = true;
        title.Font.Color = _theme.Heading;
        title.ParagraphFormat.SpaceAfter = 14;
        title.ParagraphFormat.LineSpacingRule = PdfLayoutLineSpacing.Single;
        title.ParagraphFormat.OutlineLevel = 1;

        var caption = document.Styles.AddStyle(CaptionStyle, PdfLayoutStyles.Normal);
        caption.Font.Size = Math.Max(6, _theme.BaseFontSize - 1.5);
        caption.Font.Italic = true;
        caption.Font.Color = _theme.Muted;
        caption.ParagraphFormat.SpaceBefore = 3;
        caption.ParagraphFormat.SpaceAfter = 10;

        var quote = document.Styles.AddStyle(QuoteStyle, PdfLayoutStyles.Normal);
        quote.Font.Italic = true;
        quote.Font.Color = _theme.Text.Blend(_white, 0.25);
        quote.ParagraphFormat.LeftIndent = Cm(0.8);
        quote.ParagraphFormat.SpaceBefore = 4;
        quote.ParagraphFormat.Borders.Left.Width = 2.5;
        quote.ParagraphFormat.Borders.Left.Color = _theme.Accent.Blend(_white, 0.3);
        quote.ParagraphFormat.Borders.DistanceFromLeft = Cm(0.3);

        var code = document.Styles.AddStyle(CodeStyle, PdfLayoutStyles.Normal);
        code.Font.Name = _forceDefaultFont ? PdfFontFamilies.Default : PdfFontFamilies.Monospace;
        code.Font.Size = Math.Max(6, _theme.BaseFontSize - 1.5);
        code.ParagraphFormat.Shading = new PdfColor(0xF4, 0xF5, 0xF7);
        code.ParagraphFormat.LeftIndent = Cm(0.2);
        code.ParagraphFormat.SpaceBefore = 4;
        code.ParagraphFormat.SpaceAfter = 8;
        code.ParagraphFormat.LineSpacingRule = PdfLayoutLineSpacing.Single;

        var tocTitle = document.Styles.AddStyle(TocTitleStyle, PdfLayoutStyles.Normal);
        tocTitle.Font.Name = _theme.HeadingFontFamily;
        tocTitle.Font.Size = Math.Round(_theme.BaseFontSize * 1.7, 1);
        tocTitle.Font.Bold = true;
        tocTitle.Font.Color = _theme.Heading;
        tocTitle.ParagraphFormat.SpaceAfter = 14;

        for (var level = 1; level <= 4; level++)
        {
            var entry = document.Styles.AddStyle(TocEntryStylePrefix + level.ToString(CultureInfo.InvariantCulture), PdfLayoutStyles.Normal);
            entry.Font.Bold = level == 1;
            entry.ParagraphFormat.LeftIndent = (level - 1) * 14;
            entry.ParagraphFormat.SpaceBefore = level == 1 ? 5 : 1;
            entry.ParagraphFormat.SpaceAfter = 1;
        }

        var header = document.Styles[PdfLayoutStyles.Header];
        header.Font.Size = Math.Max(6, _theme.BaseFontSize - 2);
        header.Font.Color = _theme.Muted;

        var footer = document.Styles[PdfLayoutStyles.Footer];
        footer.Font.Size = Math.Max(6, _theme.BaseFontSize - 2);
        footer.Font.Color = _theme.Muted;
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

    private void AddBodySection(PdfLayoutDocument document, PdfSectionDefinition definition, bool isFirst)
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
    private PageGeometry ApplyPageSetup(PdfLayoutPageSetup target, PdfPageSetupDefinition section)
    {
        var geometry = ResolvePageGeometry(section);

        // The dimensions are written out as they are to be printed, landscape included.
        target.PageWidth = geometry.Width;
        target.PageHeight = geometry.Height;
        target.TopMargin = geometry.Top;
        target.BottomMargin = geometry.Bottom;
        target.LeftMargin = geometry.Left;
        target.RightMargin = geometry.Right;
        target.HeaderDistance = Math.Max(18, geometry.Top * 0.45);
        target.FooterDistance = Math.Max(18, geometry.Bottom * 0.45);

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

    private void AddBlock(PdfLayoutSection section, PdfBlockDefinition block)
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

    private void AddHeading(PdfLayoutSection section, PdfBlockDefinition block)
    {
        var level = block.Level ?? LevelFromType(block.Type);
        var paragraph = section.AddParagraph();

        paragraph.Style = PdfLayoutStyles.Heading(Math.Clamp(level, 1, 4));
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

    private void AddParagraph(PdfLayoutSection section, PdfBlockDefinition block)
    {
        var paragraph = section.AddParagraph();

        ApplyBlockFormat(paragraph.Format, block);
        AddInline(paragraph, block.Text, block.Bold, block.Italic);
    }

    private void AddMarkdown(PdfLayoutSection section, PdfBlockDefinition block)
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
                        paragraph.Style = PdfLayoutStyles.Heading(Math.Clamp(rich.Level, 1, 4));

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

    private void AddList(PdfLayoutSection section, PdfBlockDefinition block)
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

    private void AddListItem(PdfLayoutSection section, IEnumerable<RichTextSpan> spans, string marker, int level, PdfBlockDefinition block)
    {
        var paragraph = section.AddParagraph();

        ApplyBlockFormat(paragraph.Format, block);
        paragraph.Format.LeftIndent = Cm(0.75 + (level * 0.6));

        // The marker hangs to the left of the wrapped text, so a long item stays aligned under itself.
        paragraph.Format.FirstLineIndent = Cm(-0.45);
        paragraph.Format.SpaceAfter = 2.5;
        paragraph.Format.TabStops = [new PdfLayoutTabStop(Cm(0.75 + (level * 0.6)))];

        paragraph.AddText(marker).Color = _theme.Primary;
        paragraph.AddTab();

        AddSpans(paragraph, spans, block?.Bold == true, block?.Italic == true);
    }

    private static void AddSpacer(PdfLayoutSection section, double height)
    {
        var paragraph = section.AddParagraph();

        paragraph.Format.LineSpacingRule = PdfLayoutLineSpacing.Exactly;
        paragraph.Format.LineSpacing = 1;
        paragraph.Format.SpaceBefore = 0;
        paragraph.Format.SpaceAfter = Math.Clamp(height, 0, 400);
        paragraph.Format.Font.Size = 1;
    }

    private void AddRule(PdfLayoutSection section, PdfBlockDefinition block)
    {
        var paragraph = section.AddParagraph();
        var color = PdfResolvedTheme.ReadColor(block?.Color, _theme.Muted.Blend(_white, 0.5), "color", _warnings);

        paragraph.Format.Borders.Bottom.Width = 0.75;
        paragraph.Format.Borders.Bottom.Color = color;
        paragraph.Format.SpaceBefore = block?.SpaceBefore ?? 4;
        paragraph.Format.SpaceAfter = block?.SpaceAfter ?? 10;
        paragraph.Format.LineSpacingRule = PdfLayoutLineSpacing.Exactly;
        paragraph.Format.LineSpacing = 2;
        paragraph.Format.Font.Size = 1;
    }

    private void AddQuote(PdfLayoutSection section, string text, PdfBlockDefinition block)
    {
        var paragraph = section.AddParagraph();

        paragraph.Style = QuoteStyle;
        ApplyBlockFormat(paragraph.Format, block);
        AddInline(paragraph, text, block?.Bold, block?.Italic);
    }

    private static void AddCode(PdfLayoutSection section, string text)
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

    private void AddImage(PdfLayoutSection section, PdfImageDefinition image, PdfBlockDefinition block)
    {
        if (image is null || string.IsNullOrWhiteSpace(image.Source))
        {
            _warnings.Add($"Image block {block?.Id} names no source.");

            return;
        }

        if (!TryGetImage(image.Source, out var picture))
        {
            _missingImageCount++;
            _warnings.Add($"The image \"{image.Source}\" could not be found or is not a JPEG, PNG, GIF or BMP.");

            return;
        }

        var paragraph = section.AddParagraph();
        paragraph.Format.Alignment = ReadAlignment(block?.Align, PdfLayoutAlignment.Center);
        paragraph.Format.SpaceBefore = block?.SpaceBefore ?? 4;
        paragraph.Format.SpaceAfter = string.IsNullOrWhiteSpace(image.Caption) ? block?.SpaceAfter ?? 8 : 2;
        paragraph.Format.KeepWithNext = !string.IsNullOrWhiteSpace(image.Caption);

        var (width, height) = FitImage(image, picture.Width, picture.Height, _page.UsableWidth, _page.UsableHeight * 0.85);

        paragraph.AddImage(picture, width, height);

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

    /// <summary>
    /// Reads a picture the document names, once however often it is drawn.
    /// </summary>
    private bool TryGetImage(string source, out PdfImage picture)
    {
        picture = null;

        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var key = source.Trim();

        if (_loadedImages.TryGetValue(key, out picture))
        {
            return picture is not null;
        }

        if (_images.TryGetValue(key, out var data) && data?.Bytes is { Length: > 0 } bytes)
        {
            picture = PdfImage.TryLoad(bytes);
        }

        _loadedImages[key] = picture;

        return picture is not null;
    }

    private void AddCaption(PdfLayoutSection section, string caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return;
        }

        var paragraph = section.AddParagraph();
        paragraph.Style = CaptionStyle;
        paragraph.Format.Alignment = PdfLayoutAlignment.Center;
        AddInline(paragraph, caption, null, null);
    }

    private void AddCallout(PdfLayoutSection section, PdfBlockDefinition block)
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
        table.LeftPadding = 10;
        table.RightPadding = 10;
        table.TopPadding = 6;
        table.BottomPadding = 6;
        table.KeepTogether = true;
        table.AddColumn(_page.UsableWidth);

        var row = table.AddRow();
        var cell = row.Cells[0];

        cell.Shading = background;
        cell.Borders.Left.Visible = true;
        cell.Borders.Left.Width = 3;
        cell.Borders.Left.Color = accent;

        if (!string.IsNullOrWhiteSpace(block.Title))
        {
            var title = cell.AddParagraph();
            title.Format.Font.Bold = true;
            title.Format.Font.Color = accent;
            title.Format.SpaceAfter = 2;
            AddInline(title, block.Title, null, null);
        }

        foreach (var line in (block.Text ?? string.Empty).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var paragraph = cell.AddParagraph();
            paragraph.Format.SpaceAfter = 2;
            AddInline(paragraph, line.Trim(), block.Bold, block.Italic);
        }

        AddSpacer(section, block.SpaceAfter ?? 8);
    }

    private void AddKeyValues(PdfLayoutSection section, PdfBlockDefinition block)
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
        table.RightPadding = 6;
        table.TopPadding = 1.5;
        table.BottomPadding = 1.5;

        // A short fact list reads as one thing; split across a page break it reads as two.
        table.KeepTogether = pairs.Count <= 15;
        table.AddColumn(_page.UsableWidth * 0.32);
        table.AddColumn(_page.UsableWidth * 0.68);

        foreach (var (label, value) in pairs)
        {
            var row = table.AddRow();
            var labelParagraph = row.Cells[0].AddParagraph();
            labelParagraph.Format.Font.Bold = true;
            labelParagraph.Format.Font.Color = _theme.Muted;
            AddInline(labelParagraph, label, null, null);

            var valueParagraph = row.Cells[1].AddParagraph();
            AddInline(valueParagraph, value, block.Bold, block.Italic);
        }

        AddSpacer(section, block.SpaceAfter ?? 8);
    }

    private void AddSignatureLines(PdfLayoutSection section, PdfBlockDefinition block)
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
        table.RightPadding = 18;
        table.KeepTogether = true;

        for (var index = 0; index < perRow; index++)
        {
            table.AddColumn(columnWidth);
        }

        for (var start = 0; start < labels.Count; start += perRow)
        {
            var row = table.AddRow();

            for (var index = 0; index < perRow && start + index < labels.Count; index++)
            {
                var line = row.Cells[index].AddParagraph();
                line.Format.SpaceBefore = 34;
                line.Format.Borders.Top.Width = 0.75;
                line.Format.Borders.Top.Color = _theme.Text;
                line.Format.Font.Size = Math.Max(6, _theme.BaseFontSize - 1.5);
                line.Format.Font.Color = _theme.Muted;
                line.AddText(labels[start + index]);
            }
        }

        AddSpacer(section, block.SpaceAfter ?? 10);
    }

    /// <summary>
    /// Applies a block's own formatting over its style.
    /// </summary>
    private void ApplyBlockFormat(PdfLayoutParagraphFormat format, PdfBlockDefinition block)
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
            format.Font.Color = PdfResolvedTheme.ReadColor(block.Color, _theme.Text, "color", _warnings);
        }

        if (!string.IsNullOrWhiteSpace(block.BackgroundColor))
        {
            format.Shading = PdfResolvedTheme.ReadColor(block.BackgroundColor, _white, "background_color", _warnings);
        }

        if (block.SpaceBefore is >= 0)
        {
            format.SpaceBefore = Math.Min(block.SpaceBefore.Value, 400);
        }

        if (block.SpaceAfter is >= 0)
        {
            format.SpaceAfter = Math.Min(block.SpaceAfter.Value, 400);
        }

        if (block.KeepWithNext == true)
        {
            format.KeepWithNext = true;
        }
    }

    private static PdfLayoutAlignment? ReadAlignment(string value, PdfLayoutAlignment? fallback)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "left" or "start" => PdfLayoutAlignment.Left,
            "center" or "centre" or "middle" => PdfLayoutAlignment.Center,
            "right" or "end" => PdfLayoutAlignment.Right,
            "justify" or "justified" => PdfLayoutAlignment.Justify,
            _ => fallback,
        };
    }

    private void AddInline(PdfLayoutParagraph paragraph, string text, bool? bold, bool? italic)
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

    private void AddSpans(PdfLayoutParagraph paragraph, IEnumerable<RichTextSpan> spans, bool forceBold = false, bool forceItalic = false)
    {
        var wroteAnything = false;

        foreach (var span in spans)
        {
            if (string.IsNullOrEmpty(span.Text))
            {
                continue;
            }

            PdfLayoutText formatted;

            if (IsWebLink(span.Link))
            {
                formatted = paragraph.AddHyperlink(span.Link, isWeb: true).AddText(span.Text);
                formatted.Font.Color = _theme.Accent;
                formatted.Font.Underline = true;
            }
            else
            {
                formatted = paragraph.AddText(span.Text);
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
                // Strike-through is not drawn, so the run is marked the way a reader still reads as removed
                // rather than silently losing the distinction.
                formatted.Font.Color = _theme.Muted;
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

        var builder = new StringBuilder(id.Length);

        foreach (var character in id)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Adds the running heads and feet to a body section.
    /// </summary>
    private void AddRunningHeads(PdfLayoutSection section, bool isFirst)
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
        PdfLayoutHeaderFooter target,
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
            paragraph.Format.Font.Color = PdfResolvedTheme.ReadColor(slots.Color, _theme.Muted, "color", _warnings);
        }

        paragraph.Format.TabStops =
        [
            new PdfLayoutTabStop(_page.UsableWidth / 2, PdfLayoutAlignment.Center),
            new PdfLayoutTabStop(_page.UsableWidth, PdfLayoutAlignment.Right),
        ];

        if (slots?.Separator == true)
        {
            var border = isHeader ? paragraph.Format.Borders.Bottom : paragraph.Format.Borders.Top;
            border.Width = 0.5;
            border.Color = _theme.Muted.Blend(_white, 0.55);

            if (isHeader)
            {
                paragraph.Format.Borders.DistanceFromBottom = 3;
            }
            else
            {
                paragraph.Format.Borders.DistanceFromTop = 3;
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

    private void AddLogo(PdfLayoutParagraph paragraph)
    {
        if (!TryGetImage(_theme.Logo, out var picture))
        {
            _missingImageCount++;

            return;
        }

        var height = _theme.LogoHeight;
        var width = picture.Height > 0
            ? height * picture.Width / picture.Height
            : height;

        paragraph.AddImage(picture, width, height);
    }

    private static string Join(string existing, string addition)
    {
        return string.IsNullOrWhiteSpace(existing)
            ? addition
            : existing + "   " + addition;
    }

    /// <summary>
    /// Writes running head text, turning its tokens into the fields filled in page by page.
    /// </summary>
    private void AppendTokens(PdfLayoutParagraph paragraph, string text)
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
                    paragraph.AddPageField().Format = format;

                    break;

                case "pages":
                case "total":
                    {
                        // "of 3" counts the pages the numbers run through: the whole file, or only the body
                        // when the body is numbered from one. Counting the file while numbering only the body
                        // printed "Page 4 of 3".
                        var field = _numberBodyOnly
                            ? paragraph.AddSectionPagesField()
                            : paragraph.AddNumPagesField();

                        field.Format = format;

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
