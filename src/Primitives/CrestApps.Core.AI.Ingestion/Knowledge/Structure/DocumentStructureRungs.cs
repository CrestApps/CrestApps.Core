using System.Text.RegularExpressions;
using CrestApps.Core.Ingestion;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Ingestion.Knowledge.Structure;

/// <summary>
/// The work the built-in strategies do, and the primitives they share.
/// </summary>
/// <remarks>
/// This is one class rather than four because the strategies share more than they differ: reading a page
/// number off a page, deciding whether a block is a running head, measuring how far one title is from
/// another. Splitting those across four files would mean a fifth file holding what all four need, and every
/// change to a shared rule would land in whichever file it was least expected.
/// <para>
/// What a caller sees is the strategies, which are small and separate. This is the library they call into.
/// </para>
/// </remarks>
internal static partial class DocumentStructureRungs
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
    internal const int MaxTocPageIndex = 10;

    /// <summary>
    /// How many "title ... page" lines a page needs before it is believed to be a table of contents. One or
    /// two such lines happen by accident in ordinary prose; five do not.
    /// </summary>
    internal const int MinimumTocEntries = 5;

    /// <summary>
    /// How much bigger than the body a line has to be before it is treated as a heading.
    /// </summary>
    internal const double HeadingSizeRatio = 1.5;

    /// <summary>
    /// How different a heading may be from a table-of-contents title and still be taken as the same title.
    /// Printed headings drop subtitles, change case and hyphenate, so an exact match finds almost nothing.
    /// </summary>
    internal const double MaxTitleDistance = 0.3;

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
    internal const double PrefixPenalty = 0.5;

    /// <summary>
    /// How near an edge a number has to be printed before it is taken for the page number rather than for
    /// part of the text, as a fraction of the page height.
    /// </summary>
    internal const double EdgeBand = 0.12;

    /// <summary>
    /// How far down a page an article's opening heading may sit, as a fraction of the page height measured
    /// from the bottom. A title opens a page; a heading halfway down it is a subheading inside one.
    /// </summary>
    internal const double HeadingTopBand = 0.7;

    /// <summary>
    /// How many headed pages a document needs before its headings alone are taken as article boundaries.
    /// Below this the document is barely divided, and one article is the safer answer.
    /// </summary>
    internal const int MinimumInferredArticles = 3;

    /// <summary>
    /// The longest a heading may be and still be stored as an article title.
    /// </summary>
    internal const int MaxInferredTitleCharacters = 200;

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
    internal const double MinLabelledPageRatio = 0.6;

    /// <summary>
    /// Divides a document by the contents page it carries.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="labels">The section labels by page.</param>
    /// <param name="pageCount">How many pages the document has.</param>
    /// <returns>The divisions, or none when there is no contents page the headings can be matched to.</returns>
    /// <remarks>
    /// A contents page states the titles and the pages they are on, which turns "where does an article
    /// begin" from a judgement into a fuzzy string match. It is still only as good as the match: a document
    /// whose printed headings resemble nothing it lists yields nothing here.
    /// </remarks>
    internal static List<DocumentArticle> BuildFromTableOfContents(
        IngestionDocument document,
        IReadOnlyDictionary<int, string> labels,
        int pageCount)
    {
        var (seeds, tocPageIndex) = ReadTableOfContents(document);

        if (seeds.Count == 0)
        {
            return [];
        }

        var boundaries = MatchHeadings(document, seeds, tocPageIndex);

        return boundaries.Count == 0 ? [] : BuildArticles(document, boundaries, labels, pageCount, splitAdvertisements: true);
    }

    /// <summary>
    /// Divides a document by the headings its type size implies.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="labels">The section labels by page.</param>
    /// <param name="pageCount">How many pages the document has.</param>
    /// <returns>The divisions, or none when nothing on the page reads as a heading.</returns>
    /// <remarks>
    /// The last rung before giving up, and the only one that guesses. A contents page is the answer key, not
    /// a precondition: plenty of publications have none that can be read — the page is laid out so no title
    /// and page number share a line, or what looks like one is a parts list — and answering "one article"
    /// for a twenty-three page magazine is a worse answer than reading its own headings.
    /// </remarks>
    internal static List<DocumentArticle> BuildFromInferredHeadings(
        IngestionDocument document,
        IReadOnlyDictionary<int, string> labels,
        int pageCount)
    {
        var boundaries = InferHeadingBoundaries(document);

        return boundaries.Count == 0 ? [] : BuildArticles(document, boundaries, labels, pageCount, splitAdvertisements: false);
    }

    /// <summary>
    /// Reads the outline a reader captured, when there is one.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <returns>The outline in document order, or an empty list.</returns>
    /// <remarks>
    /// The outline is recorded on the first section because an <c>IngestionDocument</c> carries no metadata
    /// of its own. A reader that cannot see an outline — anything reading a format that has none, or a
    /// provider-backed reader — records nothing and the analyzer carries on to the signals it can read.
    /// </remarks>
    internal static IReadOnlyList<DocumentOutlineEntry> ReadOutline(IngestionDocument document)
    {
        if (document.Sections.Count == 0 || !document.Sections[0].HasMetadata)
        {
            return [];
        }

        if (!document.Sections[0].Metadata.TryGetValue(ElementMetadataKeys.Outline, out var value))
        {
            return [];
        }

        return value as IReadOnlyList<DocumentOutlineEntry> ?? [];
    }

    /// <summary>
    /// Turns an outline into the divisions it describes.
    /// </summary>
    /// <param name="entries">The outline entries, in document order.</param>
    /// <param name="pageCount">How many pages the document has.</param>
    /// <returns>The divisions, nested, or none when the outline says nothing usable.</returns>
    /// <remarks>
    /// A division runs until the next one at its own level or shallower, which is what makes a chapter span
    /// its own sections rather than stopping at the first of them. The levels an outline declares are not
    /// always a tidy sequence — an outline may step from the first level to the third — so nesting is taken
    /// from the order the levels appear in rather than from their values.
    /// <para>
    /// Deeper divisions are emitted after the ones containing them, which is what makes the innermost
    /// division own a page the two of them share, and so keeps a chapter's text from being stored twice.
    /// </para>
    /// </remarks>
    internal static List<DocumentArticle> BuildFromOutline(IReadOnlyList<DocumentOutlineEntry> entries, int pageCount)
    {
        if (entries.Count == 0 || pageCount <= 0)
        {
            return [];
        }

        var ordered = entries
            .Where(entry => entry.PageNumber > 0 && !string.IsNullOrWhiteSpace(entry.Title))
            .Select((entry, index) => (Entry: entry, Index: index))
            .OrderBy(pair => pair.Entry.PageNumber)
            .ThenBy(pair => pair.Index)
            .Select(pair => pair.Entry)
            .ToList();

        if (ordered.Count == 0)
        {
            return [];
        }

        var articles = new List<DocumentArticle>(ordered.Count);
        var open = new List<(int Level, int Ordinal)>();

        for (var index = 0; index < ordered.Count; index++)
        {
            var entry = ordered[index];

            while (open.Count > 0 && open[^1].Level >= entry.Level)
            {
                open.RemoveAt(open.Count - 1);
            }

            var ordinal = index + 1;
            var pageEnd = pageCount;

            for (var next = index + 1; next < ordered.Count; next++)
            {
                if (ordered[next].Level <= entry.Level)
                {
                    pageEnd = Math.Max(entry.PageNumber, ordered[next].PageNumber - 1);

                    break;
                }
            }

            // Whatever precedes the first entry is front matter the outline does not name. It belongs to the
            // first division rather than to nothing, because an element owned by no division is an element
            // whose text is never stored.
            var pageStart = index == 0 ? 1 : entry.PageNumber;

            articles.Add(new DocumentArticle
            {
                Ordinal = ordinal,
                Depth = open.Count + 1,
                ParentOrdinal = open.Count > 0 ? open[^1].Ordinal : 0,
                Title = entry.Title,
                PageStart = Math.Min(pageStart, pageCount),
                PageEnd = Math.Min(Math.Max(pageEnd, pageStart), pageCount),
            });

            open.Add((entry.Level, ordinal));
        }

        return articles;
    }

    /// <summary>
    /// How many stated headings a document needs before they are taken as divisions.
    /// </summary>
    /// <remarks>
    /// One heading is a title. Dividing a document at its title produces one division covering all of it,
    /// which is the answer the analyzer already gives when it finds nothing.
    /// </remarks>
    internal const int MinimumStatedHeadings = 2;

    /// <summary>
    /// Divides a document by the heading levels its elements state.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="pageCount">How many pages the document has.</param>
    /// <returns>The divisions, nested, or none when nothing states a heading level.</returns>
    /// <remarks>
    /// Unlike every signal below it, this reads a level the document declared rather than one worked out
    /// from type size and position. A Word paragraph styled <c>Heading 2</c> is a second-level heading
    /// because the document says so, and the same is true of an <c>h2</c>, a tagged PDF's <c>H2</c> and a
    /// layout service's section heading. Whichever reader produced them, the levels mean the same thing.
    /// <para>
    /// Divisions are bounded by element rather than by page, so three headings on one page produce three
    /// divisions. A page-bounded answer would merge them and store the page's text under whichever came
    /// last.
    /// </para>
    /// </remarks>
    internal static List<DocumentArticle> BuildFromHeadingLevels(IngestionDocument document, int pageCount)
    {
        if (pageCount <= 0)
        {
            return [];
        }

        var headings = new List<(int Level, string Title, int Page, int ElementIndex)>();
        var elementIndex = 0;

        for (var sectionIndex = 0; sectionIndex < document.Sections.Count; sectionIndex++)
        {
            var section = document.Sections[sectionIndex];
            var page = GetPageNumber(document, sectionIndex);

            foreach (var element in section.Elements)
            {
                var level = GetHeadingLevel(element);
                var text = level > 0 ? CollapseHeading(element.GetSemanticText()) : null;

                if (level > 0 && !string.IsNullOrEmpty(text))
                {
                    headings.Add((level, text, page, elementIndex));
                }

                elementIndex++;
            }
        }

        if (headings.Count < MinimumStatedHeadings)
        {
            return [];
        }

        var articles = new List<DocumentArticle>(headings.Count);
        var open = new List<(int Level, int Ordinal)>();

        for (var index = 0; index < headings.Count; index++)
        {
            var heading = headings[index];

            while (open.Count > 0 && open[^1].Level >= heading.Level)
            {
                open.RemoveAt(open.Count - 1);
            }

            var pageEnd = pageCount;

            for (var next = index + 1; next < headings.Count; next++)
            {
                if (headings[next].Level <= heading.Level)
                {
                    pageEnd = Math.Max(heading.Page, headings[next].Page);

                    break;
                }
            }

            // Whatever precedes the first heading belongs to the first division, for the same reason front
            // matter does: an element owned by no division is one whose text is never stored.
            var isFirst = index == 0;

            articles.Add(new DocumentArticle
            {
                Ordinal = index + 1,
                Depth = open.Count + 1,
                ParentOrdinal = open.Count > 0 ? open[^1].Ordinal : 0,
                Title = heading.Title,
                ElementStart = isFirst ? 0 : heading.ElementIndex,
                PageStart = isFirst ? 1 : heading.Page,
                PageEnd = Math.Min(Math.Max(pageEnd, heading.Page), pageCount),
            });

            open.Add((heading.Level, index + 1));
        }

        return articles;
    }

    /// <summary>
    /// Reads the heading level an element states, if it states one.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The one-based level, or zero.</returns>
    internal static int GetHeadingLevel(IngestionDocumentElement element)
    {
        if (!element.HasMetadata || !element.Metadata.TryGetValue(ElementMetadataKeys.HeadingLevel, out var value))
        {
            return 0;
        }

        return value switch
        {
            int level when level > 0 => level,
            long level when level > 0 => (int)level,
            _ => 0,
        };
    }

    internal static (List<TocSeed> Seeds, int PageIndex) ReadTableOfContents(IngestionDocument document)
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
    internal static (string Title, string Author) SplitAuthor(string line)
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
    internal static Dictionary<int, string> CaptureFolios(IngestionDocument document)
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
    internal static Dictionary<int, string> CaptureSectionLabels(IngestionDocument document)
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
    internal static List<Boundary> MatchHeadings(IngestionDocument document, List<TocSeed> seeds, int tocPageIndex)
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
    internal static List<Boundary> InferHeadingBoundaries(IngestionDocument document)
    {
        var boundaries = new List<Boundary>();
        var bodySize = GetBodyPointSize(document);

        if (bodySize <= 0)
        {
            return boundaries;
        }

        var gate = bodySize * HeadingSizeRatio;
        var elementIndex = 0;

        for (var index = 0; index < document.Sections.Count; index++)
        {
            var section = document.Sections[index];
            var pageHeight = GetDouble(section, ElementMetadataKeys.PageHeight) ?? 0;
            var page = GetPageNumber(document, index);
            var onThisPage = 0;
            IngestionDocumentElement previous = null;

            foreach (var element in section.Elements)
            {
                var position = elementIndex++;

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

                if (top is null)
                {
                    continue;
                }

                // The first heading on a page has to sit near its top, which is what keeps a pull quote
                // halfway down a magazine feature from opening an article. A page that has already opened
                // one takes later headings wherever they fall: a newspaper page carries four or five
                // stories, and every headline after the first is somewhere below the fold.
                if (onThisPage == 0 && pageHeight > 0 && top.Value < pageHeight * HeadingTopBand)
                {
                    continue;
                }

                // A headline that wraps is one headline. Its second line is the same size, directly beneath
                // the first and overlapping it across the page, and opening a division at it would file the
                // article under half a sentence and leave the other half titling nothing.
                if (onThisPage > 0 && ContinuesHeading(previous, element, size.Value))
                {
                    var merged = boundaries[^1];

                    boundaries[^1] = merged with
                    {
                        Seed = merged.Seed with { Title = CollapseHeading($"{merged.Seed.Title} {text}") },
                    };

                    previous = element;

                    continue;
                }

                onThisPage++;
                previous = element;
                boundaries.Add(new Boundary(page, new TocSeed(text, null, page), position));
            }
        }

        return boundaries.Count >= MinimumInferredArticles ? boundaries : [];
    }

    /// <summary>
    /// Gets the top edge of an element.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The top coordinate, or <see langword="null"/> when the element carries no bounds.</returns>
    /// <summary>
    /// How much bigger or smaller a block may be than the line above it and still be the same headline.
    /// </summary>
    internal const double HeadingContinuationSizeTolerance = 0.12;

    /// <summary>
    /// How far below a headline its next line may begin, as a multiple of the type size.
    /// </summary>
    /// <remarks>
    /// Leading is close to the type size in display setting and rarely more than twice it. A gap larger than
    /// this is a different piece of type, not the rest of this one.
    /// </remarks>
    internal const double HeadingContinuationLeading = 2.0;

    /// <summary>
    /// Decides whether a block is the next line of the heading above it rather than a heading of its own.
    /// </summary>
    /// <param name="previous">The block accepted as a heading immediately before this one.</param>
    /// <param name="element">The block being considered.</param>
    /// <param name="size">The block's type size.</param>
    /// <returns><see langword="true"/> when the block continues the heading above it.</returns>
    /// <remarks>
    /// A headline that wraps produces two blocks the segmenter has no reason to join: they are separate runs
    /// of type at the same size, one directly beneath the other, overlapping across the measure. Treating
    /// the second as a heading of its own splits one article into two, titles the first with half a sentence
    /// and gives the article's pages to the half that says least.
    /// <para>
    /// All three conditions are required. Same size alone would join a headline to the standfirst beneath
    /// it; adjacency alone would join a headline to the first line of body text set large.
    /// </para>
    /// </remarks>
    internal static bool ContinuesHeading(IngestionDocumentElement previous, IngestionDocumentElement element, double size)
    {
        if (previous is null)
        {
            return false;
        }

        var previousSize = GetDouble(previous, ElementMetadataKeys.ModalPointSize);

        if (previousSize is null || previousSize.Value <= 0)
        {
            return false;
        }

        if (Math.Abs(size - previousSize.Value) > previousSize.Value * HeadingContinuationSizeTolerance)
        {
            return false;
        }

        var above = GetBounds(previous);
        var below = GetBounds(element);

        if (above is null || below is null)
        {
            return false;
        }

        // The next line sits below the one above it, by no more than ordinary leading.
        var gap = above.Value.Bottom - below.Value.Top;

        if (gap < -previousSize.Value || gap > previousSize.Value * HeadingContinuationLeading)
        {
            return false;
        }

        // And beneath it rather than beside it: two headlines in adjacent columns are the same size and the
        // same height, and are two headlines.
        var overlap = Math.Min(above.Value.Right, below.Value.Right) - Math.Max(above.Value.Left, below.Value.Left);
        var narrowest = Math.Min(above.Value.Right - above.Value.Left, below.Value.Right - below.Value.Left);

        return narrowest > 0 && overlap > narrowest / 2;
    }

    /// <summary>
    /// Reads an element's bounds.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The bounds, or <see langword="null"/> when the element carries none.</returns>
    internal static (double Left, double Bottom, double Right, double Top)? GetBounds(IngestionDocumentElement element)
    {
        if (!element.HasMetadata || !element.Metadata.TryGetValue(ElementMetadataKeys.BoundingBox, out var value))
        {
            return null;
        }

        if (value is not double[] { Length: 4 } bounds)
        {
            return null;
        }

        return (bounds[0], bounds[1], bounds[2], bounds[3]);
    }

    internal static double? GetTop(IngestionDocumentElement element)
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
    internal static string CollapseHeading(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var collapsed = CollapseOverprint(WhitespaceRun().Replace(text, " ").Trim());

        return collapsed.Length <= MaxInferredTitleCharacters
            ? collapsed
            : collapsed[..MaxInferredTitleCharacters].TrimEnd();
    }

    /// <summary>
    /// Collapses a word repeated immediately after itself.
    /// </summary>
    /// <param name="text">The heading text.</param>
    /// <returns>The heading with overprinting removed.</returns>
    /// <remarks>
    /// Display type is routinely set twice, slightly offset, to fake a weight the font does not have. Both
    /// runs are real text, so a reader that takes a page at its word reads every headline word twice:
    /// "COMMAND COMMAND &amp; &amp; CONQUER". Nothing downstream can tell that from a title, and it is the
    /// title a reader of the knowledge base sees.
    /// <para>
    /// Only an immediate repeat is collapsed, and only in a heading. English headings that legitimately
    /// repeat a word next to itself are vanishingly rare, and body text is left alone entirely.
    /// </para>
    /// </remarks>
    internal static string CollapseOverprint(string text)
    {
        if (text.Length == 0 || !text.Contains(' ', StringComparison.Ordinal))
        {
            return text;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 2)
        {
            return text;
        }

        var kept = new List<string>(words.Length) { words[0] };
        var collapsed = false;

        for (var index = 1; index < words.Length; index++)
        {
            if (string.Equals(words[index], kept[^1], StringComparison.Ordinal))
            {
                collapsed = true;

                continue;
            }

            kept.Add(words[index]);
        }

        return collapsed ? string.Join(' ', kept) : text;
    }

    internal static List<DocumentArticle> BuildArticles(
        IngestionDocument document,
        List<Boundary> boundaries,
        IReadOnlyDictionary<int, string> labels,
        int pageCount,
        bool splitAdvertisements)
    {
        var titled = new List<DocumentArticle>();

        for (var index = 0; index < boundaries.Count; index++)
        {
            var boundary = boundaries[index];

            // Two articles can open on one page, which is ordinary in a newspaper. The one that opens first
            // still ends on that page; only the last of them carries on to the next boundary's page.
            var pageEnd = index + 1 < boundaries.Count
                ? (boundaries[index + 1].Page == boundary.Page ? boundary.Page : boundaries[index + 1].Page - 1)
                : pageCount;

            titled.Add(new DocumentArticle
            {
                Title = boundary.Seed.Title,
                Authors = boundary.Seed.Author is null ? [] : [boundary.Seed.Author],
                ElementStart = boundary.ElementStart,
                PageStart = boundary.Page,
                PageEnd = Math.Max(boundary.Page, pageEnd),
                SectionLabel = labels.TryGetValue(boundary.Page, out var label) ? label : null,
                Type = KnowledgeArticleTypes.Article,
            });
        }

        var articles = splitAdvertisements
            ? SplitAdvertisements(titled, boundaries, labels, pageCount)
            : titled;

        // Whatever comes before the first matched title is front matter: the cover, the contents, the
        // masthead. It is one article, and it is never an advertisement - a contents page carries no
        // heading and no running head, which is exactly the shape an advertisement has.
        if (boundaries[0].Page > 1)
        {
            articles.Insert(0, new DocumentArticle
            {
                Title = document.Identifier,

                // Front matter opens at the document's first element whenever anything else is bounded by
                // one, so that stamping has somewhere to start.
                ElementStart = boundaries[0].ElementStart >= 0 ? 0 : -1,
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
    internal static List<DocumentArticle> SplitAdvertisements(
        List<DocumentArticle> articles,
        List<Boundary> boundaries,
        IReadOnlyDictionary<int, string> labels,
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
    internal static List<DocumentArticle> Number(List<DocumentArticle> articles)
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
    internal static int GetPageNumber(IngestionDocument document, int index)
    {
        var pageNumber = document.Sections[index].PageNumber;

        return pageNumber is > 0 ? pageNumber.Value : index + 1;
    }

    internal static bool LabelsPagesConsistently(List<DocumentArticle> articles, IReadOnlyDictionary<int, string> labels)
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

    internal static DocumentArticle Rewrite(DocumentArticle article, int pageStart, int pageEnd, string type, int ordinal = 0)
    {
        return new DocumentArticle
        {
            Ordinal = ordinal,
            Depth = article.Depth,
            ParentOrdinal = article.ParentOrdinal,
            Title = article.Title,
            Authors = article.Authors,
            SectionLabel = article.SectionLabel,
            Type = type,

            // A rewrite moves an article's page range; it does not move where the article opens. Dropping
            // this would leave an element-bounded article with no opening element, and stamping would give
            // its text to whichever article was still open.
            ElementStart = pageStart == article.PageStart ? article.ElementStart : -1,
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
    internal static void Stamp(
        IngestionDocument document,
        List<DocumentArticle> articles,
        Dictionary<int, string> folios,
        Dictionary<int, string> labels)
    {
        // A division that knows which element opens it is bounded by that element, so several divisions can
        // share a page. Everything that only knows a page keeps the page-bounded behaviour.
        if (articles.Exists(article => article.ElementStart >= 0))
        {
            StampByElement(document, articles, folios, labels);

            return;
        }

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
    /// Assigns every element to the division that opens at or before it.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="articles">The divisions, in document order.</param>
    /// <param name="folios">The folios by page.</param>
    /// <param name="labels">The section labels by page.</param>
    /// <remarks>
    /// Walking the elements once and carrying the division forward is what gives a page with three headings
    /// on it three divisions. A section keeps the division its last element belongs to, because a section's
    /// own metadata describes the page and a page that spans a boundary belongs to whichever division is
    /// still open at its end.
    /// </remarks>
    internal static void StampByElement(
        IngestionDocument document,
        List<DocumentArticle> articles,
        Dictionary<int, string> folios,
        Dictionary<int, string> labels)
    {
        var starts = new Dictionary<int, DocumentArticle>();

        foreach (var article in articles)
        {
            if (article.ElementStart >= 0)
            {
                starts[article.ElementStart] = article;
            }
        }

        var current = articles[0];
        var elementIndex = 0;

        for (var sectionIndex = 0; sectionIndex < document.Sections.Count; sectionIndex++)
        {
            var page = GetPageNumber(document, sectionIndex);
            var section = document.Sections[sectionIndex];

            if (folios.TryGetValue(page, out var folio))
            {
                section.Metadata[ElementMetadataKeys.Folio] = folio;
            }

            if (labels.TryGetValue(page, out var label))
            {
                section.Metadata[ElementMetadataKeys.SectionLabel] = label;
            }

            foreach (var element in section.Elements)
            {
                if (starts.TryGetValue(elementIndex, out var opened))
                {
                    current = opened;
                }

                element.Metadata[ElementMetadataKeys.ArticleOrdinal] = current.Ordinal;
                elementIndex++;
            }

            section.Metadata[ElementMetadataKeys.ArticleOrdinal] = current.Ordinal;
        }
    }

    /// <summary>
    /// Builds the answer for a document nothing was inferred about.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="pageCount">How many pages the document has.</param>
    /// <param name="folios">The folios, when they were read before the analysis gave up.</param>
    /// <returns>One article covering the whole document.</returns>
    internal static DocumentStructure Single(IngestionDocument document, int pageCount, Dictionary<int, string> folios = null)
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
    internal static double GetBodyPointSize(IngestionDocument document)
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

    internal static bool IsNearEdge(IngestionDocumentElement element, double pageHeight)
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

    internal static bool IsDecoration(IngestionDocumentElement element)
    {
        return IngestionDocumentElementExtensions.IsDecoration(element);
    }

    internal static double? GetDouble(IngestionDocumentElement element, string key)
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
    internal static string Normalize(string value)
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
    internal static double Distance(string left, string right)
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
    internal static partial Regex TocLine();

    [GeneratedRegex(@"^\D*(\d{1,3})\D*$", RegexOptions.CultureInvariant)]
    internal static partial Regex FolioToken();

    // Uppercase letters and spaces in any script, so this reads a Hungarian, Greek or Cyrillic running head
    // as readily as a Latin one.
    [GeneratedRegex(@"^[\p{Lu}\p{Lm}\p{Lo}][\p{Lu}\p{Lm}\p{Lo}\s'’\-]{5,}$", RegexOptions.CultureInvariant)]
    internal static partial Regex SectionLabel();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    internal static partial Regex WhitespaceRun();

    internal readonly record struct TocSeed(string Title, string Author, int PrintedPage);

    internal readonly record struct Boundary(int Page, TocSeed Seed, int ElementStart = -1);
}
