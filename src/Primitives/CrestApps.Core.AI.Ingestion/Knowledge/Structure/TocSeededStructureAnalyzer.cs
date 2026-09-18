using System.Text.RegularExpressions;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Ingestion.Knowledge.Structure;

/// <summary>
/// Splits a document into articles using its own table of contents as the answer key.
/// </summary>
/// <remarks>
/// Deciding where one article ends and the next begins from headings alone is guesswork: a big line of type
/// might be a title, a pull quote or an advertisement. A table of contents states the answer — these are the
/// titles, in this order, on these printed pages — so the only remaining question is which page carries
/// which title, and that is a fuzzy string match rather than a judgement call.
/// <para>
/// Every step degrades to the step before it. No table of contents, no headings big enough to be titles, or
/// anything at all going wrong, and the document is one article — which is exactly what it was before any of
/// this existed.
/// </para>
/// </remarks>
public sealed partial class TocSeededStructureAnalyzer : IDocumentStructureAnalyzer
{
    /// <summary>
    /// How many leading pages may hold the table of contents. Past that it is a list inside an article, not
    /// the document's own front matter.
    /// </summary>
    /// <remarks>
    /// Front matter is longer than it first appears. A trade magazine opens with a cover, an inside cover
    /// advertisement, a masthead and an editor's letter before it lists anything, which routinely puts the
    /// contents on page five or six; a book adds a half title and a copyright page. A limit of four pages
    /// was enough for the one document this was first measured against and silently produced a single
    /// article for every publication laid out any other way — the contents page was there, and nothing
    /// looked at it.
    /// <para>
    /// Scanning further is cheap and well guarded: a page needs <see cref="MinimumTocEntries"/> entries
    /// before it is believed at all, the page with the most entries wins, and a document whose headings
    /// then match nothing falls back to one article anyway.
    /// </para>
    /// </remarks>
    private const int MaxTocPageIndex = 10;

    /// <summary>
    /// How many "title ... page" lines a page needs before it is believed to be a table of contents. One or
    /// two such lines happen by accident in ordinary prose; five do not.
    /// </summary>
    private const int MinimumTocEntries = 5;

    /// <summary>
    /// How much bigger than the body a line has to be before it is treated as a heading.
    /// </summary>
    private const double HeadingSizeRatio = 1.5;

    /// <summary>
    /// How different a heading may be from a table-of-contents title and still be taken as the same title.
    /// Printed headings drop subtitles, change case and hyphenate, so an exact match finds almost nothing.
    /// </summary>
    private const double MaxTitleDistance = 0.3;

    /// <summary>
    /// How much of what a prefix match left out still counts against it, as a fraction of that difference.
    /// </summary>
    /// <remarks>
    /// A heading that is a prefix of a listed title is a near match, never an equal one. Scoring it as equal
    /// made a longer heading a perfect match for every shorter entry it happens to begin with, so a themed
    /// issue listing both "the short title" and "the short title spelled out in full" filed the longer
    /// heading under the shorter entry. Half of what was dropped is small enough to keep every dropped
    /// subtitle inside <see cref="MaxTitleDistance"/> — a prefix has to cover half the longer title before it
    /// is forgiven at all — and large enough that a title printed in full always beats one that merely starts
    /// the same way.
    /// </remarks>
    private const double PrefixPenalty = 0.5;

    /// <summary>
    /// How near an edge a number has to be printed before it is taken for the page number rather than for
    /// part of the text, as a fraction of the page height.
    /// </summary>
    private const double EdgeBand = 0.12;

    /// <summary>
    /// How far down a page an article's opening heading may sit, as a fraction of the page height measured
    /// from the bottom. A title opens a page; a heading halfway down it is a subheading inside one.
    /// </summary>
    private const double HeadingTopBand = 0.7;

    /// <summary>
    /// How many headed pages a document needs before its headings alone are taken as article boundaries.
    /// Below this the document is barely divided, and one article is the safer answer.
    /// </summary>
    private const int MinimumInferredArticles = 3;

    /// <summary>
    /// The longest a heading may be and still be stored as an article title.
    /// </summary>
    private const int MaxInferredTitleCharacters = 200;

    /// <summary>
    /// What share of the pages inside articles have to carry a running head before the absence of one on a
    /// page is read as evidence of anything.
    /// </summary>
    /// <remarks>
    /// A magazine prints a running head on every editorial page, so a page without one is an advertisement.
    /// A report that prints one on its chapter openers and nowhere else says nothing at all by omitting it,
    /// and reading that omission as "advertisement" excludes most of the document from the index. Twenty
    /// labelled pages in a five-hundred page book must not arm a rule that then discards the other
    /// four-hundred-and-eighty.
    /// <para>
    /// The share is measured over the pages the rule would actually judge, not over the whole file. Front
    /// matter carries no running head by convention and would otherwise drag every document under the bar.
    /// </para>
    /// </remarks>
    private const double MinLabelledPageRatio = 0.6;

    private readonly ILogger<TocSeededStructureAnalyzer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TocSeededStructureAnalyzer"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public TocSeededStructureAnalyzer(ILogger<TocSeededStructureAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public DocumentStructure Analyze(IngestionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var pageCount = document.Sections.Count;

        if (pageCount == 0)
        {
            return Single(document, 0);
        }

        try
        {
            var folios = CaptureFolios(document);
            var (seeds, tocPageIndex) = ReadTableOfContents(document);
            var labels = CaptureSectionLabels(document);

            var boundaries = seeds.Count == 0
                ? []
                : MatchHeadings(document, seeds, tocPageIndex);

            // A contents page is the answer key, not a precondition. Plenty of publications have none that
            // can be read — the page is laid out so that no title and page number share a line, or what
            // looks like a contents page is a parts list — and answering "one article" for a
            // twenty-three page magazine is a worse answer than reading its own headings.
            if (boundaries.Count == 0)
            {
                boundaries = InferHeadingBoundaries(document);
            }

            if (boundaries.Count == 0)
            {
                return Single(document, pageCount, folios);
            }

            var articles = BuildArticles(document, boundaries, labels, pageCount);

            if (articles.Count == 0)
            {
                return Single(document, pageCount, folios);
            }

            Stamp(document, articles, folios, labels);

            return new DocumentStructure
            {
                Articles = articles,
                Folios = folios,
                IsInferred = true,
            };
        }
        catch (Exception ex)
        {
            // A document that could not be understood is still a document. One article is the answer that is
            // never wrong, only less useful.
            _logger.LogWarning(ex, "Structure analysis failed for '{Identifier}'. The document is treated as one article.", document.Identifier);

            return Single(document, pageCount);
        }
    }

    /// <summary>
    /// Reads the table of contents, when the document has one in its front matter.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <returns>The titles it lists, in the order it lists them.</returns>
    private static (List<TocSeed> Seeds, int PageIndex) ReadTableOfContents(IngestionDocument document)
    {
        var best = new List<TocSeed>();
        var bestIndex = -1;
        var limit = Math.Min(MaxTocPageIndex, document.Sections.Count);

        for (var index = 0; index < limit; index++)
        {
            var seeds = new List<TocSeed>();

            foreach (var element in document.Sections[index].Elements)
            {
                var text = element.GetSemanticText();

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var match = TocLine().Match(line);

                    if (!match.Success)
                    {
                        continue;
                    }

                    var title = match.Groups[1].Value.Trim(' ', '.', '·', '…', '-', '–', '—');

                    if (title.Length < 3)
                    {
                        continue;
                    }

                    var (cleanTitle, author) = SplitAuthor(title);

                    seeds.Add(new TocSeed(cleanTitle, author, int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)));
                }
            }

            if (seeds.Count >= MinimumTocEntries && seeds.Count > best.Count)
            {
                best = seeds;
                bestIndex = index;
            }
        }

        return (best, bestIndex);
    }

    /// <summary>
    /// Splits a contents line into its title and, when one is printed after a dash, its author.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>The title and the author, which may be <see langword="null"/>.</returns>
    private static (string Title, string Author) SplitAuthor(string line)
    {
        var separator = line.LastIndexOfAny(['–', '—']);

        if (separator <= 0 || separator >= line.Length - 1)
        {
            return (line, null);
        }

        var candidate = line[(separator + 1)..].Trim();

        // A byline is a name, not a sentence. Anything long or wordy is part of the title.
        if (candidate.Length is 0 or > 40 || candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 4)
        {
            return (line, null);
        }

        var title = line[..separator].Trim();

        return title.Length < 3 ? (line, null) : (title, candidate);
    }

    /// <summary>
    /// Reads the page number printed on each page.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <returns>The folio of each page, keyed by the page's position in the file.</returns>
    private static Dictionary<int, string> CaptureFolios(IngestionDocument document)
    {
        var folios = new Dictionary<int, string>();

        for (var index = 0; index < document.Sections.Count; index++)
        {
            var section = document.Sections[index];
            var pageHeight = GetDouble(section, ElementMetadataKeys.PageHeight) ?? 0;

            foreach (var element in section.Elements)
            {
                if (!IsDecoration(element))
                {
                    continue;
                }

                var text = element.GetSemanticText();

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var match = FolioToken().Match(text.Trim());

                if (!match.Success)
                {
                    continue;
                }

                if (pageHeight > 0 && !IsNearEdge(element, pageHeight))
                {
                    continue;
                }

                folios[GetPageNumber(document, index)] = match.Groups[1].Value;

                break;
            }
        }

        return folios;
    }

    /// <summary>
    /// Reads the running-head labels that recur across the document.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <returns>The label on each page, keyed by the page's position in the file.</returns>
    /// <remarks>
    /// A label recurring on one page only is a title that happens to be set in capitals. Two pages is the
    /// smallest number that makes it a running head.
    /// </remarks>
    private static Dictionary<int, string> CaptureSectionLabels(IngestionDocument document)
    {
        var candidates = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < document.Sections.Count; index++)
        {
            foreach (var element in document.Sections[index].Elements)
            {
                if (!IsDecoration(element))
                {
                    continue;
                }

                var text = element.GetSemanticText()?.Trim();

                if (string.IsNullOrWhiteSpace(text) || !SectionLabel().IsMatch(text))
                {
                    continue;
                }

                if (!candidates.TryGetValue(text, out var pages))
                {
                    pages = [];
                    candidates[text] = pages;
                }

                pages.Add(GetPageNumber(document, index));
            }
        }

        var labels = new Dictionary<int, string>();

        foreach (var (label, pages) in candidates)
        {
            if (pages.Count < 2)
            {
                continue;
            }

            foreach (var page in pages)
            {
                labels[page] = label;
            }
        }

        return labels;
    }

    /// <summary>
    /// Finds the page each listed title is actually printed on.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="seeds">The titles the table of contents listed.</param>
    /// <returns>The first page of each article that was found, in page order.</returns>
    /// <remarks>
    /// A heading is scored against every entry that is still free and binds to the closest of them, not to
    /// the first that scores well enough. A contents page grouped by theme lists its titles in its own order
    /// rather than in page order, so taking the first entry under the threshold handed each heading to
    /// whichever of the entries it resembled happened to be listed earliest, and a heading that matched one
    /// entry exactly was filed under another.
    /// </remarks>
    private static List<Boundary> MatchHeadings(IngestionDocument document, List<TocSeed> seeds, int tocPageIndex)
    {
        var bodySize = GetBodyPointSize(document);
        var boundaries = new List<Boundary>();
        var used = new HashSet<int>();
        var titles = seeds.Select(seed => Normalize(seed.Title)).ToList();

        for (var index = 0; index < document.Sections.Count; index++)
        {
            // A title printed in the contents is not the article. Where the document carries no type sizes
            // the heading test cannot run at all, and every contents line then matched its own entry and
            // put every boundary on the contents page, collapsing the whole document into one article.
            if (index == tocPageIndex)
            {
                continue;
            }

            foreach (var element in document.Sections[index].Elements)
            {
                if (IsDecoration(element))
                {
                    continue;
                }

                var size = GetDouble(element, ElementMetadataKeys.ModalPointSize);

                if (bodySize > 0 && (size is null || size.Value < bodySize * HeadingSizeRatio))
                {
                    continue;
                }

                var text = Normalize(element.GetSemanticText());

                if (text.Length < 3)
                {
                    continue;
                }

                var closest = -1;
                var closestDistance = double.MaxValue;

                for (var seedIndex = 0; seedIndex < seeds.Count; seedIndex++)
                {
                    if (used.Contains(seedIndex))
                    {
                        continue;
                    }

                    var distance = Distance(text, titles[seedIndex]);

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closest = seedIndex;
                    }
                }

                if (closest < 0 || closestDistance > MaxTitleDistance)
                {
                    continue;
                }

                used.Add(closest);
                boundaries.Add(new Boundary(GetPageNumber(document, index), seeds[closest]));
            }
        }

        return boundaries
            .GroupBy(boundary => boundary.Page)
            .Select(group => group.First())
            .OrderBy(boundary => boundary.Page)
            .ToList();
    }

    /// <summary>
    /// Turns the matched pages into articles, filling the gaps between them.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="boundaries">The first page of each matched article.</param>
    /// <param name="labels">The running-head label on each page.</param>
    /// <param name="pageCount">How many pages the document has.</param>
    /// <returns>The articles.</returns>
    /// <summary>
    /// Finds the article boundaries from the document's own headings, for a document whose contents page
    /// could not be read.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <returns>One boundary per page that opens with a heading, or an empty list.</returns>
    /// <remarks>
    /// This is weaker evidence than a table of contents and is treated as such. A heading only starts an
    /// article when it is the topmost thing on its page and sits in the band where a title is printed, so a
    /// pull quote or a subheading part way down a page cannot split an article in two. A document that
    /// yields fewer than <see cref="MinimumInferredArticles"/> of them is left whole, because splitting a
    /// document into two on this much evidence buys little and risks more.
    /// <para>
    /// Type size is the signal, so a document that carries none — anything a provider-backed reader
    /// produced — yields nothing here and stays one article.
    /// </para>
    /// </remarks>
    private static List<Boundary> InferHeadingBoundaries(IngestionDocument document)
    {
        var boundaries = new List<Boundary>();
        var bodySize = GetBodyPointSize(document);

        if (bodySize <= 0)
        {
            return boundaries;
        }

        var gate = bodySize * HeadingSizeRatio;

        for (var index = 0; index < document.Sections.Count; index++)
        {
            var section = document.Sections[index];
            var pageHeight = GetDouble(section, ElementMetadataKeys.PageHeight) ?? 0;

            string title = null;
            var highest = double.MinValue;

            foreach (var element in section.Elements)
            {
                if (IsDecoration(element))
                {
                    continue;
                }

                var size = GetDouble(element, ElementMetadataKeys.ModalPointSize);

                if (size is null || size.Value < gate)
                {
                    continue;
                }

                var text = CollapseHeading(element.GetSemanticText());

                if (text.Length < 3)
                {
                    continue;
                }

                var top = GetTop(element);

                if (top is null || (pageHeight > 0 && top.Value < pageHeight * HeadingTopBand))
                {
                    continue;
                }

                if (top.Value > highest)
                {
                    highest = top.Value;
                    title = text;
                }
            }

            if (title is null)
            {
                continue;
            }

            var page = GetPageNumber(document, index);

            boundaries.Add(new Boundary(page, new TocSeed(title, null, page)));
        }

        return boundaries.Count >= MinimumInferredArticles ? boundaries : [];
    }

    /// <summary>
    /// Gets the top edge of an element.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The top coordinate, or <see langword="null"/> when the element carries no bounds.</returns>
    private static double? GetTop(IngestionDocumentElement element)
    {
        if (!element.HasMetadata ||
            !element.Metadata.TryGetValue(ElementMetadataKeys.BoundingBox, out var raw) ||
            raw is not double[] { Length: 4 } bounds)
        {
            return null;
        }

        return bounds[3];
    }

    /// <summary>
    /// Renders a heading as the single line an article title has to be.
    /// </summary>
    /// <param name="text">The heading text.</param>
    /// <returns>The collapsed title, trimmed to a storable length.</returns>
    private static string CollapseHeading(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var collapsed = WhitespaceRun().Replace(text, " ").Trim();

        return collapsed.Length <= MaxInferredTitleCharacters
            ? collapsed
            : collapsed[..MaxInferredTitleCharacters].TrimEnd();
    }

    private static List<DocumentArticle> BuildArticles(
        IngestionDocument document,
        List<Boundary> boundaries,
        Dictionary<int, string> labels,
        int pageCount)
    {
        var titled = new List<DocumentArticle>();

        for (var index = 0; index < boundaries.Count; index++)
        {
            var boundary = boundaries[index];
            var pageEnd = index + 1 < boundaries.Count ? boundaries[index + 1].Page - 1 : pageCount;

            titled.Add(new DocumentArticle
            {
                Title = boundary.Seed.Title,
                Authors = boundary.Seed.Author is null ? [] : [boundary.Seed.Author],
                PageStart = boundary.Page,
                PageEnd = Math.Max(boundary.Page, pageEnd),
                SectionLabel = labels.TryGetValue(boundary.Page, out var label) ? label : null,
                Type = KnowledgeArticleTypes.Article,
            });
        }

        var articles = SplitAdvertisements(titled, boundaries, labels, pageCount);

        // Whatever comes before the first matched title is front matter: the cover, the contents, the
        // masthead. It is one article, and it is never an advertisement - a contents page carries no
        // heading and no running head, which is exactly the shape an advertisement has.
        if (boundaries[0].Page > 1)
        {
            articles.Insert(0, new DocumentArticle
            {
                Title = document.Identifier,
                PageStart = 1,
                PageEnd = boundaries[0].Page - 1,
                SectionLabel = labels.TryGetValue(1, out var frontLabel) ? frontLabel : null,
                Type = KnowledgeArticleTypes.Article,
            });
        }

        return Number(articles);
    }

    /// <summary>
    /// Takes the pages that carry neither a title nor a section label out of the articles they were folded
    /// into, and marks them as advertisements.
    /// </summary>
    /// <param name="articles">The articles.</param>
    /// <param name="boundaries">The first page of each matched article.</param>
    /// <param name="labels">The running-head label on each page.</param>
    /// <param name="pageCount">How many pages the document has.</param>
    /// <returns>The articles, with advertisement pages separated out.</returns>
    /// <remarks>
    /// An advertisement folded into the article it interrupts pollutes that article's text with copy about
    /// something else entirely, and answers a question with it.
    /// <para>
    /// The absence of a running head only means something in a document that prints running heads on
    /// substantially every page. A report with a table of contents and no page furniture has no labels
    /// anywhere, and one that labels only its chapter openers has a few; treating every unlabelled page of
    /// either as an advertisement would exclude most of the document. Both cases are the same case, so the
    /// rule arms on the share of pages that carry a label rather than on whether any page does.
    /// </para>
    /// </remarks>
    private static List<DocumentArticle> SplitAdvertisements(
        List<DocumentArticle> articles,
        List<Boundary> boundaries,
        Dictionary<int, string> labels,
        int pageCount)
    {
        if (!LabelsPagesConsistently(articles, labels))
        {
            return articles;
        }

        var titled = boundaries.Select(boundary => boundary.Page).ToHashSet();
        var result = new List<DocumentArticle>();

        foreach (var article in articles)
        {
            var runStart = article.PageStart;

            for (var page = article.PageStart; page <= article.PageEnd; page++)
            {
                var isAdvertisement = !titled.Contains(page) && !labels.ContainsKey(page) && page != article.PageStart;

                if (!isAdvertisement)
                {
                    continue;
                }

                if (page > runStart)
                {
                    result.Add(Rewrite(article, runStart, page - 1, article.Type));
                }

                var runEnd = page;

                while (runEnd + 1 <= article.PageEnd && !titled.Contains(runEnd + 1) && !labels.ContainsKey(runEnd + 1))
                {
                    runEnd++;
                }

                result.Add(new DocumentArticle
                {
                    Title = $"Advertisement, p. {page}",
                    PageStart = page,
                    PageEnd = runEnd,
                    Type = KnowledgeArticleTypes.Advertisement,
                });

                page = runEnd;
                runStart = runEnd + 1;
            }

            if (runStart <= article.PageEnd)
            {
                result.Add(Rewrite(article, runStart, article.PageEnd, article.Type));
            }
        }

        return result.Count == 0
            ? [new DocumentArticle { Ordinal = 1, PageStart = 1, PageEnd = pageCount }]
            : result;
    }

    /// <summary>
    /// Numbers the articles in page order, so an ordinal is always the article's position in the document.
    /// </summary>
    /// <param name="articles">The articles.</param>
    /// <returns>The numbered articles.</returns>
    private static List<DocumentArticle> Number(List<DocumentArticle> articles)
    {
        var ordinal = 1;

        return articles
            .OrderBy(article => article.PageStart)
            .Select(article => Rewrite(article, article.PageStart, article.PageEnd, article.Type, ordinal++))
            .ToList();
    }

    /// <summary>
    /// Decides whether this document labels its pages consistently enough for a missing label to mean
    /// anything.
    /// </summary>
    /// <param name="articles">The articles the advertisement rule would judge.</param>
    /// <param name="labels">The running head read from each page that carries one.</param>
    /// <returns><see langword="true"/> when a missing running head is evidence; otherwise <see langword="false"/>.</returns>
    /// <summary>
    /// Gets the page a section was read from.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="index">The section's position in the document.</param>
    /// <returns>The printed-on page number.</returns>
    /// <remarks>
    /// A section's position and its page number are the same only while every page produces a section, and
    /// a reader drops a page that yielded nothing — a blank leaf, or one holding only artwork it discarded.
    /// Everything downstream, a figure's page above all, carries the real page number, so keying anything by
    /// position silently shifts every article boundary, running head and folio after the first missing page.
    /// </remarks>
    private static int GetPageNumber(IngestionDocument document, int index)
    {
        var pageNumber = document.Sections[index].PageNumber;

        return pageNumber is > 0 ? pageNumber.Value : index + 1;
    }

    private static bool LabelsPagesConsistently(List<DocumentArticle> articles, Dictionary<int, string> labels)
    {
        if (labels.Count == 0)
        {
            return false;
        }

        var covered = 0;
        var labelled = 0;

        foreach (var article in articles)
        {
            for (var page = article.PageStart; page <= article.PageEnd; page++)
            {
                covered++;

                if (labels.ContainsKey(page))
                {
                    labelled++;
                }
            }
        }

        return covered > 0 && labelled >= covered * MinLabelledPageRatio;
    }

    private static DocumentArticle Rewrite(DocumentArticle article, int pageStart, int pageEnd, string type, int ordinal = 0)
    {
        return new DocumentArticle
        {
            Ordinal = ordinal,
            Title = article.Title,
            Authors = article.Authors,
            SectionLabel = article.SectionLabel,
            Type = type,
            PageStart = pageStart,
            PageEnd = pageEnd,
        };
    }

    /// <summary>
    /// Records what was inferred on the document itself, so nothing downstream has to re-derive it.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="articles">The articles.</param>
    /// <param name="folios">The folio of each page.</param>
    /// <param name="labels">The running-head label on each page.</param>
    private static void Stamp(
        IngestionDocument document,
        List<DocumentArticle> articles,
        Dictionary<int, string> folios,
        Dictionary<int, string> labels)
    {
        var byPage = new Dictionary<int, DocumentArticle>();

        foreach (var article in articles)
        {
            for (var page = article.PageStart; page <= article.PageEnd; page++)
            {
                byPage[page] = article;
            }
        }

        for (var index = 0; index < document.Sections.Count; index++)
        {
            var page = GetPageNumber(document, index);
            var section = document.Sections[index];

            if (folios.TryGetValue(page, out var folio))
            {
                section.Metadata[ElementMetadataKeys.Folio] = folio;
            }

            if (labels.TryGetValue(page, out var label))
            {
                section.Metadata[ElementMetadataKeys.SectionLabel] = label;
            }

            if (!byPage.TryGetValue(page, out var article))
            {
                continue;
            }

            section.Metadata[ElementMetadataKeys.ArticleOrdinal] = article.Ordinal;

            foreach (var element in section.Elements)
            {
                element.Metadata[ElementMetadataKeys.ArticleOrdinal] = article.Ordinal;
            }
        }
    }

    /// <summary>
    /// Builds the answer for a document nothing was inferred about.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="pageCount">How many pages the document has.</param>
    /// <param name="folios">The folios, when they were read before the analysis gave up.</param>
    /// <returns>One article covering the whole document.</returns>
    private static DocumentStructure Single(IngestionDocument document, int pageCount, Dictionary<int, string> folios = null)
    {
        return new DocumentStructure
        {
            Articles =
            [
                new DocumentArticle
                {
                    Ordinal = 1,
                    Title = document.Identifier,
                    PageStart = pageCount > 0 ? 1 : 0,
                    PageEnd = pageCount,
                    Type = KnowledgeArticleTypes.Article,
                }
            ],
            Folios = folios ?? new Dictionary<int, string>(),
            IsInferred = false,
        };
    }

    /// <summary>
    /// Finds the size ordinary body text is set in, which is whatever size the most text is set in.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <returns>The body point size, or zero when nothing recorded one.</returns>
    private static double GetBodyPointSize(IngestionDocument document)
    {
        var weights = new Dictionary<double, int>();

        foreach (var element in document.EnumerateContent())
        {
            if (IsDecoration(element))
            {
                continue;
            }

            var size = GetDouble(element, ElementMetadataKeys.ModalPointSize);
            var text = element.GetSemanticText();

            if (size is null or <= 0 || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var rounded = Math.Round(size.Value, 1);

            weights[rounded] = weights.GetValueOrDefault(rounded) + text.Length;
        }

        return weights.Count == 0 ? 0 : weights.MaxBy(entry => entry.Value).Key;
    }

    private static bool IsNearEdge(IngestionDocumentElement element, double pageHeight)
    {
        if (!element.HasMetadata ||
            !element.Metadata.TryGetValue(ElementMetadataKeys.BoundingBox, out var raw) ||
            raw is not double[] { Length: 4 } box)
        {
            return true;
        }

        var band = pageHeight * EdgeBand;

        return box[1] <= band || box[3] >= pageHeight - band;
    }

    private static bool IsDecoration(IngestionDocumentElement element)
    {
        return IngestionDocumentElementExtensions.IsDecoration(element);
    }

    private static double? GetDouble(IngestionDocumentElement element, string key)
    {
        if (!element.HasMetadata || !element.Metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            double number => number,
            float number => number,
            int number => number,
            _ => null,
        };
    }

    /// <summary>
    /// Folds a title down to what two spellings of it have in common: letters and digits, lower-cased, with
    /// everything else removed.
    /// </summary>
    /// <param name="value">The title.</param>
    /// <returns>The folded title.</returns>
    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Measures how different two folded titles are, as a fraction of the longer one.
    /// </summary>
    /// <param name="left">One title.</param>
    /// <param name="right">The other title.</param>
    /// <returns>Zero when they are the same, one when they share nothing.</returns>
    /// <remarks>
    /// A printed heading routinely drops the subtitle a contents line carries, so a heading that is a prefix
    /// of a listed title counts as very nearly the same title rather than as a difference proportional to
    /// what it left out. It never counts as exactly the same title: what the prefix dropped still tells the
    /// two apart, at the <see cref="PrefixPenalty"/> discount, so a heading spelled out in full always
    /// outscores one that only begins the same way.
    /// </remarks>
    private static double Distance(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 1;
        }

        if (left.StartsWith(right, StringComparison.Ordinal) || right.StartsWith(left, StringComparison.Ordinal))
        {
            var shorter = Math.Min(left.Length, right.Length);
            var longer = Math.Max(left.Length, right.Length);
            var dropped = 1 - ((double)shorter / longer);

            return shorter >= 8 && shorter >= longer / 2.0 ? dropped * PrefixPenalty : dropped;
        }

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;

                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return (double)previous[right.Length] / Math.Max(left.Length, right.Length);
    }

    [GeneratedRegex(@"^(.+?)[\s.·…]+(\d{1,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex TocLine();

    [GeneratedRegex(@"^\D*(\d{1,3})\D*$", RegexOptions.CultureInvariant)]
    private static partial Regex FolioToken();

    // Uppercase letters and spaces in any script, so this reads a Hungarian, Greek or Cyrillic running head
    // as readily as a Latin one.
    [GeneratedRegex(@"^[\p{Lu}\p{Lm}\p{Lo}][\p{Lu}\p{Lm}\p{Lo}\s'’\-]{5,}$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionLabel();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();

    private readonly record struct TocSeed(string Title, string Author, int PrintedPage);

    private readonly record struct Boundary(int Page, TocSeed Seed);
}
