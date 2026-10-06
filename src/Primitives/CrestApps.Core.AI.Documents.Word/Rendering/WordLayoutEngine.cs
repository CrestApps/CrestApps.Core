using System.Globalization;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <summary>
/// Lays a document out into pages: sections with their page size, margins and columns, paragraphs broken into
/// lines, tables broken across pages with repeating header rows, pictures, charts and shapes, headers and
/// footers with page numbers, and footnotes.
/// </summary>
/// <remarks>
/// The Open XML package says what a document contains but not where anything lands, and the page numbers of a
/// table of contents, a preview and a PDF all need that. This engine is an approximation of Word's own layout —
/// text widths are estimated, text does not flow beside floating pictures — but it is the one layout the
/// preview, the PDF export and the computed page numbers all share, so they agree with each other.
/// </remarks>
internal sealed partial class WordLayoutEngine
{
    private const string PageToken = "\u0001PAGE\u0001";
    private const string PagesToken = "\u0001NUMPAGES\u0001";
    private const string SectionPagesToken = "\u0001SECTIONPAGES\u0001";

    private readonly WordPackage _package;
    private readonly WordLayoutOptions _options;
    private readonly WordStyleResolver _resolver;
    private readonly WordListCounter _lists;
    private readonly WordLayout _layout = new();
    private readonly Dictionary<string, byte[]> _pictures = new(StringComparer.Ordinal);
    private readonly Dictionary<long, int> _footnoteNumbers = [];
    private readonly bool _evenAndOddHeaders;
    private readonly string _background;
    private readonly double _defaultTab;
    private readonly Dictionary<string, OpenXmlPart> _headers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OpenXmlPart> _footers = new(StringComparer.Ordinal);
    private readonly List<PageInfo> _pageInfo = [];

    private SectionGeometry _geometry;
    private int _sectionNumber;
    private int _pageInSection;
    private int _displayCounter;
    private WordLayoutPage _page;
    private double _y;
    private double _columnTop;
    private int _column;
    private long _pictureBytesOnPage;
    private double _footnoteReserve;
    private List<FootnoteBox> _pageFootnotes = [];
    private bool _stopped;
    private Dictionary<string, string> _pageFieldValues;

    private WordLayoutEngine(WordPackage package, WordLayoutOptions options)
    {
        _package = package;
        _options = options;
        _resolver = new WordStyleResolver(package.MainPart);
        _lists = new WordListCounter(package.MainPart);

        var settings = package.MainPart.DocumentSettingsPart?.Settings;

        _evenAndOddHeaders = settings?.GetFirstChild<EvenAndOddHeaders>() is { } evenOdd && (evenOdd.Val?.Value ?? true);
        _defaultTab = settings?.GetFirstChild<DefaultTabStop>()?.Val?.Value is { } tab && tab > 0 ? tab / 20d : 36;

        var background = package.MainPart.Document.DocumentBackground?.Color?.Value;

        _background = string.IsNullOrEmpty(background) || string.Equals(background, "auto", StringComparison.OrdinalIgnoreCase) ? null : background;
    }

    /// <summary>
    /// Lays a document out.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <param name="options">The layout options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The layout.</returns>
    public static WordLayout Layout(WordPackage package, WordLayoutOptions options = null)
    {
        ArgumentNullException.ThrowIfNull(package);

        var engine = new WordLayoutEngine(package, options ?? new WordLayoutOptions());

        engine.Run();

        return engine._layout;
    }

    private void Run()
    {
        var sections = SplitSections();

        for (var index = 0; index < sections.Count && !_stopped; index++)
        {
            BeginSection(sections[index].Properties, index + 1);

            foreach (var block in sections[index].Blocks)
            {
                if (_stopped)
                {
                    break;
                }

                FlowBlock(block);
            }
        }

        FinishPage();
        FinishDocument();
    }

    private List<(SectionProperties Properties, List<OpenXmlElement> Blocks)> SplitSections()
    {
        var sections = new List<(SectionProperties, List<OpenXmlElement>)>();
        var blocks = new List<OpenXmlElement>();

        foreach (var element in _package.Body.ChildElements)
        {
            if (element is SectionProperties)
            {
                continue;
            }

            blocks.Add(element);

            var ending = element switch
            {
                Paragraph paragraph => paragraph.ParagraphProperties?.SectionProperties,
                _ => element.Descendants<SectionProperties>().LastOrDefault(),
            };

            if (ending is not null)
            {
                sections.Add((ending, blocks));
                blocks = [];
            }
        }

        sections.Add((WordSections.EnsureBodySection(_package.Body), blocks));

        return sections;
    }

    private void BeginSection(SectionProperties section, int number)
    {
        _sectionNumber = number;

        foreach (var reference in section.Elements<HeaderReference>())
        {
            if (reference.Id?.Value is { } id && _package.MainPart.GetPartById(id) is HeaderPart part)
            {
                _headers[reference.Type?.InnerText ?? "default"] = part;
            }
        }

        foreach (var reference in section.Elements<FooterReference>())
        {
            if (reference.Id?.Value is { } id && _package.MainPart.GetPartById(id) is FooterPart part)
            {
                _footers[reference.Type?.InnerText ?? "default"] = part;
            }
        }

        var titlePage = section.GetFirstChild<TitlePage>() is { } title && (title.Val?.Value ?? true);

        // A first-page header belongs to the section that declares a different first page.
        if (!titlePage)
        {
            _headers.Remove("first");
            _footers.Remove("first");
        }

        var (width, height) = WordSections.PageSize(section);
        var margins = WordSections.Margins(section);
        var (columns, gap) = WordSections.Columns(section);
        var textWidth = width - margins.Left - margins.Right - margins.Gutter;
        var numbering = section.GetFirstChild<PageNumberType>();

        _geometry = new SectionGeometry
        {
            Width = width,
            Height = height,
            Left = margins.Left + margins.Gutter,
            Right = width - margins.Right,
            MarginTop = margins.Top,
            MarginBottom = margins.Bottom,
            HeaderDistance = margins.Header,
            FooterDistance = margins.Footer,
            Columns = columns,
            ColumnGap = gap,
            ColumnWidth = Math.Max(36, (textWidth - (gap * (columns - 1))) / columns),
            TitlePage = titlePage,
            NumberFormat = numbering?.Format?.InnerText ?? "decimal",
            Borders = section.GetFirstChild<PageBorders>(),
        };

        var headerHeight = new[] { "default", "first", "even" }.Select(kind => MeasureStory(_headers.GetValueOrDefault(kind))).Max();
        var footerHeight = new[] { "default", "first", "even" }.Select(kind => MeasureStory(_footers.GetValueOrDefault(kind))).Max();

        _geometry.ContentTop = Math.Max(margins.Top, margins.Header + headerHeight + 4);
        _geometry.ContentBottom = Math.Min(height - margins.Bottom, height - margins.Footer - footerHeight - 4);

        if (_geometry.ContentBottom - _geometry.ContentTop < 72)
        {
            _geometry.ContentBottom = Math.Min(height - 18, _geometry.ContentTop + 72);
        }

        if (numbering?.Start?.Value is { } start)
        {
            _displayCounter = start - 1;
        }

        var startType = section.GetFirstChild<SectionType>()?.Val?.InnerText ?? "nextPage";

        if (_page is null || startType != "continuous")
        {
            NewPage(sectionStart: true);

            if ((startType == "evenPage" && _displayCounter % 2 == 1) || (startType == "oddPage" && _displayCounter % 2 == 0))
            {
                // Word inserts a blank page to start the section on the side it asks for.
                NewPage(sectionStart: true);
            }
        }
        else
        {
            // A continuous section carries on down the same page in its own columns.
            _pageInSection = Math.Max(_pageInSection, 1);
            _column = 0;
            _columnTop = _y;
        }
    }

    private void NewPage(bool sectionStart = false)
    {
        FinishPage();

        if (_layout.Pages.Count >= _options.MaxPages)
        {
            _stopped = true;
            _layout.Truncated = true;

            return;
        }

        _displayCounter++;
        _pageInSection = sectionStart ? 1 : _pageInSection + 1;

        _page = new WordLayoutPage
        {
            Index = _layout.Pages.Count + 1,
            DisplayNumber = WordListCounter.FormatNumber(_displayCounter, _geometry.NumberFormat),
            Section = _sectionNumber,
            Width = _geometry.Width,
            Height = _geometry.Height,
            ContentLeft = _geometry.Left,
            ContentRight = _geometry.Right,
            ContentTop = _geometry.ContentTop,
            ContentBottom = _geometry.ContentBottom,
            Background = _background,
            BodyBottom = _geometry.ContentTop,
        };

        _layout.Pages.Add(_page);
        _pageInfo.Add(new PageInfo(_geometry, _pageInSection == 1, _headers.ToDictionary(pair => pair.Key, pair => pair.Value), _footers.ToDictionary(pair => pair.Key, pair => pair.Value)));
        _y = _geometry.ContentTop;
        _columnTop = _y;
        _column = 0;
        _pictureBytesOnPage = 0;
        _footnoteReserve = 0;
        _pageFootnotes = [];
    }

    private void NextColumnOrPage()
    {
        if (_column + 1 < _geometry.Columns)
        {
            _column++;
            _y = _columnTop;

            return;
        }

        NewPage();
    }

    private double ColumnLeft => _geometry.Left + (_column * (_geometry.ColumnWidth + _geometry.ColumnGap));

    private double ColumnWidth => _geometry.ColumnWidth;

    private double Bottom => _geometry.ContentBottom - _footnoteReserve;

    private bool AtTopOfColumn => _y <= _columnTop + 0.01;

    private void FinishPage()
    {
        if (_page is null)
        {
            return;
        }

        if (_pageFootnotes.Count > 0)
        {
            var top = _geometry.ContentBottom - _footnoteReserve + 4;

            _page.Items.Add(new WordLineItem { X1 = _geometry.Left, Y1 = top, X2 = _geometry.Left + 144, Y2 = top, Color = "000000", Width = 0.5 });

            var y = top + 4;

            foreach (var footnote in _pageFootnotes)
            {
                foreach (var item in footnote.Box.Translate(_geometry.Left, y))
                {
                    _page.Items.Add(item);
                }

                y += footnote.Box.Height;
            }
        }

        _page = null;
    }

    private void FinishDocument()
    {
        var total = _layout.Pages.Count;
        var sectionCounts = _layout.Pages.GroupBy(page => page.Section).ToDictionary(group => group.Key, group => group.Count());

        for (var index = 0; index < _layout.Pages.Count; index++)
        {
            var page = _layout.Pages[index];
            var info = _pageInfo[index];
            var number = int.TryParse(page.DisplayNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : index + 1;
            var kind = info.Geometry.TitlePage && info.FirstOfSection ? "first" : _evenAndOddHeaders && number % 2 == 0 ? "even" : "default";
            var bodyItems = page.Items.ToList();

            page.Items.Clear();
            DrawPageFrame(page, info.Geometry);

            // Headers and footers are laid out once the page count is known, so their page fields take their real
            // values and are measured as they are drawn.
            _pageFieldValues = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PageToken] = page.DisplayNumber,
                [PagesToken] = total.ToString(CultureInfo.InvariantCulture),
                [SectionPagesToken] = sectionCounts.GetValueOrDefault(page.Section).ToString(CultureInfo.InvariantCulture),
            };

            if (info.Headers.GetValueOrDefault(kind) is { } header)
            {
                var box = LayoutStory(header, page.ContentRight - page.ContentLeft);

                page.Items.AddRange(box.Translate(page.ContentLeft, info.Geometry.HeaderDistance));
            }

            if (info.Footers.GetValueOrDefault(kind) is { } footer)
            {
                var box = LayoutStory(footer, page.ContentRight - page.ContentLeft);

                page.Items.AddRange(box.Translate(page.ContentLeft, page.Height - info.Geometry.FooterDistance - box.Height));
            }

            _pageFieldValues = null;
            page.Items.AddRange(bodyItems);

            foreach (var text in page.Items.OfType<WordTextItem>())
            {
                if (text.Text.Contains('\u0001', StringComparison.Ordinal))
                {
                    text.Text = text.Text
                        .Replace(PageToken, page.DisplayNumber, StringComparison.Ordinal)
                        .Replace(PagesToken, total.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                        .Replace(SectionPagesToken, sectionCounts.GetValueOrDefault(page.Section).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
                    text.Width = WordTextMeasurer.Measure(text.Text, text.Format.Font, text.Format.DrawnSize, text.Format.Bold);
                }
            }

            if (!page.HasBodyContent)
            {
                _layout.Issues.Add(new WordLayoutIssue("blank_page", page.Index, $"Page {page.Index} has no body content.", null));
            }
        }
    }

    private void DrawPageFrame(WordLayoutPage page, SectionGeometry geometry)
    {
        if (geometry.Borders is not { } borders)
        {
            return;
        }

        var fromText = borders.OffsetFrom?.InnerText == "text";

        void Side(BorderType border, Func<double, (double X1, double Y1, double X2, double Y2)> place)
        {
            var resolved = _resolver.Border(border);

            if (resolved is null || resolved.Style == "none" || resolved.Width <= 0)
            {
                return;
            }

            var (x1, y1, x2, y2) = place(resolved.Space);

            page.Items.Add(new WordLineItem { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Color = resolved.Color ?? "000000", Width = resolved.Width, Dotted = resolved.Style is "dotted" or "dashed" });
        }

        double Left(double space) => fromText ? geometry.Left - space : space;
        double Right(double space) => fromText ? geometry.Right + space : geometry.Width - space;
        double Top(double space) => fromText ? geometry.MarginTop - space : space;
        double BottomEdge(double space) => fromText ? geometry.Height - geometry.MarginBottom + space : geometry.Height - space;

        var left = Left(_resolver.Border(borders.LeftBorder)?.Space ?? 24);
        var right = Right(_resolver.Border(borders.RightBorder)?.Space ?? 24);
        var top = Top(_resolver.Border(borders.TopBorder)?.Space ?? 24);
        var bottom = BottomEdge(_resolver.Border(borders.BottomBorder)?.Space ?? 24);

        Side(borders.TopBorder, _ => (left, top, right, top));
        Side(borders.BottomBorder, _ => (left, bottom, right, bottom));
        Side(borders.LeftBorder, _ => (left, top, left, bottom));
        Side(borders.RightBorder, _ => (right, top, right, bottom));
    }

    private double MeasureStory(OpenXmlPart part)
    {
        return part is null ? 0 : LayoutStory(part, _geometry.Right - _geometry.Left).Height;
    }

    private WordBox LayoutStory(OpenXmlPart part, double width)
    {
        var root = part switch
        {
            HeaderPart header => (OpenXmlElement)header.Header,
            FooterPart footer => footer.Footer,
            _ => null,
        };

        return root is null ? new WordBox() : LayoutContainer(root.ChildElements, width, new LayoutContext(part, null));
    }

    private void Record(OpenXmlElement element)
    {
        if (_page is null)
        {
            return;
        }

        _layout.FirstPage.TryAdd(element, _page.Index);
        _layout.LastPage[element] = _page.Index;
        _page.HasBodyContent = true;
    }

    private void Issue(string kind, string message, OpenXmlElement source)
    {
        if (_layout.Issues.Count < 200)
        {
            _layout.Issues.Add(new WordLayoutIssue(kind, _page?.Index ?? _layout.Pages.Count, message, source));
        }
    }

    /// <summary>
    /// The page geometry of a section.
    /// </summary>
    private sealed class SectionGeometry
    {
        public double Width { get; set; }

        public double Height { get; set; }

        public double Left { get; set; }

        public double Right { get; set; }

        public double MarginTop { get; set; }

        public double MarginBottom { get; set; }

        public double HeaderDistance { get; set; }

        public double FooterDistance { get; set; }

        public double ContentTop { get; set; }

        public double ContentBottom { get; set; }

        public int Columns { get; set; } = 1;

        public double ColumnGap { get; set; }

        public double ColumnWidth { get; set; }

        public bool TitlePage { get; set; }

        public string NumberFormat { get; set; } = "decimal";

        public PageBorders Borders { get; set; }
    }

    /// <summary>
    /// What a page needs once the whole document is laid out: its geometry and the headers and footers in force.
    /// </summary>
    private sealed record PageInfo(SectionGeometry Geometry, bool FirstOfSection, Dictionary<string, OpenXmlPart> Headers, Dictionary<string, OpenXmlPart> Footers);

    /// <summary>
    /// A footnote placed at the bottom of a page.
    /// </summary>
    private sealed record FootnoteBox(long Id, WordBox Box);

    /// <summary>
    /// What the content being laid out belongs to: the part its pictures are read from, and the table style its
    /// paragraphs follow.
    /// </summary>
    private sealed record LayoutContext(OpenXmlPart Part, WordTableStyle Table)
    {
        /// <summary>
        /// Gets the conditional table formatting the content's cell is in, such as the header row's.
        /// </summary>
        public IReadOnlyList<WordTableStyleCondition> Conditions { get; init; }

        /// <summary>
        /// Gets the number of the footnote being laid out, for its reference mark.
        /// </summary>
        public string FootnoteNumber { get; init; }
    }
}
