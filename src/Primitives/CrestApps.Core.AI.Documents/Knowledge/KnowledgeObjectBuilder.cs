using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Ingestion;
using CrestApps.Core.AI.Documents.Ingestion.Processors;
using CrestApps.Core.AI.Documents.Knowledge.Structure;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Documents.Knowledge;

/// <summary>
/// Turns an ingested document into the separate, individually retrievable objects a knowledge base stores.
/// </summary>
/// <remarks>
/// A page holding five hundred words, a chart and a table is four pieces of knowledge. One embedding averaged
/// over all of it retrieves none of them well, so each becomes its own object with its own text, its own
/// page, and a link back to the article and the document it came from.
/// <para>
/// The class is pure and stateless: the same document and options always produce the same objects, with the
/// same identifiers, so re-ingesting an unchanged file is a no-op rather than a duplicate.
/// </para>
/// </remarks>
public static class KnowledgeObjectBuilder
{
    private const int DocumentContentCharacters = 1000;
    private const int MaxTableRowsInContent = 200;

    /// <summary>
    /// Builds the knowledge objects for one ingested document.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="options">What is known about the file.</param>
    /// <param name="chunkedArticleText">The article text, already split to the chunk budget.</param>
    /// <returns>The objects, document first.</returns>
    public static IReadOnlyList<KnowledgeObject> Build(
        IngestionDocument document,
        KnowledgeObjectBuildOptions options,
        IReadOnlyList<string> chunkedArticleText)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Build(
            document,
            options,
            new Dictionary<int, IReadOnlyList<string>>
            {
                [1] = chunkedArticleText ?? [],
            });
    }

    /// <summary>
    /// Builds the knowledge objects for one ingested document that was split into articles.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="options">What is known about the file.</param>
    /// <param name="chunkedArticleText">Each article's text, already split to the chunk budget, keyed by the article's ordinal.</param>
    /// <returns>The objects, document first.</returns>
    public static IReadOnlyList<KnowledgeObject> Build(
        IngestionDocument document,
        KnowledgeObjectBuildOptions options,
        IReadOnlyDictionary<int, IReadOnlyList<string>> chunkedArticleText)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var fileKey = options.FileKey;
        var rootId = $"{KnowledgeContentTypes.Document}:{fileKey}";
        var segments = BuildSegments(document);
        var documentText = string.Join('\n', segments.Select(segment => segment.Text));
        var pages = document.Sections.Count;
        var title = string.IsNullOrWhiteSpace(options.Title) ? document.Identifier : options.Title;
        var folios = options.Structure?.Folios;

        var objects = new List<KnowledgeObject>
        {
            Create(options, rootId, rootId, null, KnowledgeContentTypes.Document, title, BuildDocumentContent(title, documentText), 0),
        };

        foreach (var entry in ResolveArticles(options, document, pages, title))
        {
            var articleId = $"{KnowledgeContentTypes.Article}:{fileKey}:{entry.Ordinal}";
            var articleSegments = segments.Where(segment => segment.ArticleOrdinal == entry.Ordinal).ToList();
            var articleText = string.Join('\n', articleSegments.Select(segment => segment.Text));
            var articleTitle = string.IsNullOrWhiteSpace(entry.Title) ? title : entry.Title;

            // An advertisement is kept so the document stays complete and excluded so it can never be
            // returned as an answer to a question about the document's subject.
            var isExcluded = entry.Type == KnowledgeArticleTypes.Advertisement;

            var article = Create(options, articleId, rootId, rootId, KnowledgeContentTypes.Article, articleTitle, BuildArticleContent(articleTitle, articleText), entry.Ordinal);

            article.PageStart = entry.PageStart > 0 ? entry.PageStart : null;
            article.PageEnd = entry.PageEnd > 0 ? entry.PageEnd : null;
            article.Folio = ReadFolio(folios, article.PageStart);
            article.Status = isExcluded ? KnowledgeObjectStatus.Excluded : article.Status;
            article.Put(new ArticleDetails
            {
                Authors = [.. entry.Authors],
                SectionLabel = entry.SectionLabel,
            });

            objects.Add(article);

            var articleChunks = chunkedArticleText is not null && chunkedArticleText.TryGetValue(entry.Ordinal, out var chunks)
                ? chunks
                : [];

            AddTextObjects(objects, options, rootId, articleId, articleTitle, entry.Ordinal, articleSegments, articleText, articleChunks, isExcluded, folios);
            AddFigureObjects(objects, document, options, rootId, articleId, entry.Ordinal, isExcluded, folios);
            AddTableObjects(objects, document, options, rootId, articleId, entry.Ordinal, isExcluded, folios);
        }

        var documentObject = objects[0];
        documentObject.PageStart = pages > 0 ? 1 : null;
        documentObject.PageEnd = pages > 0 ? pages : null;
        documentObject.ContentHash = options.ContentHash;

        return objects;
    }

    /// <summary>
    /// Resolves the articles to build. A document nothing was inferred about is one article, which is what
    /// every document was before structure analysis existed.
    /// </summary>
    /// <param name="options">What is known about the file.</param>
    /// <param name="document">The ingested document.</param>
    /// <param name="pages">How many pages the document has.</param>
    /// <param name="title">The document title.</param>
    /// <returns>The articles.</returns>
    private static IReadOnlyList<DocumentArticle> ResolveArticles(
        KnowledgeObjectBuildOptions options,
        IngestionDocument document,
        int pages,
        string title)
    {
        if (options.Structure?.Articles is { Count: > 0 } articles)
        {
            return articles;
        }

        return
        [
            new DocumentArticle
            {
                Ordinal = 1,
                Title = title,
                PageStart = pages > 0 ? 1 : 0,
                PageEnd = pages,
                Type = KnowledgeArticleTypes.Article,
            }
        ];
    }

    private static string ReadFolio(IReadOnlyDictionary<int, string> folios, int? page)
    {
        if (folios is null || page is null || !folios.TryGetValue(page.Value, out var folio))
        {
            return null;
        }

        return folio;
    }

    /// <summary>
    /// Reads the article an element belongs to. An element nothing stamped belongs to the first article,
    /// which for a document that was never split is the only one.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The article ordinal.</returns>
    private static int GetArticleOrdinal(IngestionDocumentElement element)
    {
        if (element.HasMetadata && element.Metadata.TryGetValue(ElementMetadataKeys.ArticleOrdinal, out var value))
        {
            return value switch
            {
                int ordinal => ordinal,
                long ordinal => (int)ordinal,
                _ => 1,
            };
        }

        return 1;
    }

    /// <summary>
    /// Collects the text that makes up the article, in reading order, remembering which page each piece came
    /// from so a chunk can report the pages it spans.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <returns>The text segments.</returns>
    private static List<TextSegment> BuildSegments(IngestionDocument document)
    {
        var segments = new List<TextSegment>();

        foreach (var element in document.EnumerateContent())
        {
            if (element is IngestionDocumentImage or IngestionDocumentTable)
            {
                continue;
            }

            // A caption belongs to its figure, and page furniture belongs to nobody.
            if (element.GetMetadataString(ElementMetadataKeys.IsCaptionFor) != null)
            {
                continue;
            }

            if (element.IsDecoration())
            {
                continue;
            }

            var text = element.GetSemanticText();

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            segments.Add(new TextSegment(text, element.PageNumber, GetArticleOrdinal(element)));
        }

        return segments;
    }

    private static void AddTextObjects(
        List<KnowledgeObject> objects,
        KnowledgeObjectBuildOptions options,
        string rootId,
        string articleId,
        string title,
        int articleOrdinal,
        List<TextSegment> segments,
        string articleText,
        IReadOnlyList<string> chunkedArticleText,
        bool isExcluded,
        IReadOnlyDictionary<int, string> folios)
    {
        if (chunkedArticleText == null || chunkedArticleText.Count == 0)
        {
            return;
        }

        var searchFrom = 0;

        for (var index = 0; index < chunkedArticleText.Count; index++)
        {
            var chunk = chunkedArticleText[index];

            if (string.IsNullOrWhiteSpace(chunk))
            {
                continue;
            }

            var range = Locate(articleText, chunk, ref searchFrom);
            var (pageStart, pageEnd) = GetPageRange(segments, articleText, range);
            var content = index == 0 && !string.IsNullOrWhiteSpace(title)
                ? title + "\n" + chunk
                : chunk;

            var entry = Create(
                options,
                $"{KnowledgeContentTypes.Text}:{options.FileKey}:{articleOrdinal}:{index}",
                rootId,
                articleId,
                KnowledgeContentTypes.Text,
                title,
                content,
                index);

            entry.PageStart = pageStart;
            entry.PageEnd = pageEnd;
            entry.Folio = ReadFolio(folios, pageStart);

            if (isExcluded)
            {
                entry.Status = KnowledgeObjectStatus.Excluded;
            }

            objects.Add(entry);
        }
    }

    private static void AddFigureObjects(
        List<KnowledgeObject> objects,
        IngestionDocument document,
        KnowledgeObjectBuildOptions options,
        string rootId,
        string articleId,
        int articleOrdinal,
        bool isExcluded,
        IReadOnlyDictionary<int, string> folios)
    {
        var ordinal = 0;

        foreach (var element in document.EnumerateContent())
        {
            if (element is not IngestionDocumentImage image || GetArticleOrdinal(element) != articleOrdinal)
            {
                continue;
            }

            var tier = image.GetMetadataString(FigureMetadataKeys.Tier);

            // A skipped figure was judged not worth keeping. A repeat of artwork already placed elsewhere in
            // the document is the same knowledge twice; the first placement carries it.
            if (tier == FigureTiers.Skip || image.GetMetadataString(FigureMetadataKeys.DuplicateOf) != null)
            {
                continue;
            }

            var caption = image.GetMetadataString(FigureMetadataKeys.Caption);
            var context = image.GetMetadataString(FigureMetadataKeys.Context);
            var description = image.AlternativeText;
            var isChart = IsChart(caption, description, options.ChartKeywords);
            var objectType = isChart ? KnowledgeContentTypes.Chart : KnowledgeContentTypes.Figure;

            var entry = Create(
                options,
                $"{objectType}:{options.FileKey}:{articleOrdinal}:{ordinal}",
                rootId,
                articleId,
                objectType,
                caption ?? options.Title,
                BuildFigureContent(caption, context, description),
                ordinal);

            entry.PageStart = image.PageNumber;
            entry.PageEnd = image.PageNumber;
            entry.Folio = ReadFolio(folios, image.PageNumber);
            entry.ContentHash = image.GetMetadataString(FigureMetadataKeys.ContentHash);
            entry.MediaType = image.MediaType;
            entry.StoragePath = image.GetMetadataString(FigureMetadataKeys.StoragePath);

            // A figure worth transcribing that has not been transcribed yet is searchable by its caption and
            // stays on the backfill queue. Text is never held back waiting for a picture.
            entry.Status = isExcluded
                ? KnowledgeObjectStatus.Excluded
                : tier == FigureTiers.Describe && string.IsNullOrWhiteSpace(description)
                    ? KnowledgeObjectStatus.PendingDescription
                    : KnowledgeObjectStatus.Ready;

            entry.Put(new FigureDetails
            {
                Caption = caption,
                CaptionSource = image.GetMetadataString(FigureMetadataKeys.CaptionSource),
                Context = context,
                Tier = tier,
                Description = description,
                DescriptionModel = image.GetMetadataString(FigureMetadataKeys.DescriptionModel),
                DescriptionPromptVersion = image.GetMetadataString(FigureMetadataKeys.DescriptionPromptVersion),
                PixelWidth = GetInt(image, FigureMetadataKeys.PixelWidth),
                PixelHeight = GetInt(image, FigureMetadataKeys.PixelHeight),
            });

            var confidence = image.GetMetadataString(FigureMetadataKeys.ValueConfidence);

            if (isChart || confidence is ChartValueConfidence.Exact or ChartValueConfidence.AxesOnly)
            {
                // A value is only stored when it came out of the file's own geometry. A number read off a
                // picture by eye looks exactly like one lifted from the geometry, and only one is true.
                entry.ObjectType = KnowledgeContentTypes.Chart;
                entry.CanonicalId = $"{KnowledgeContentTypes.Chart}:{options.FileKey}:{articleOrdinal}:{ordinal}";

                entry.Put(new ChartDetails
                {
                    // The type and the axis titles are carried only when a reader recorded them off the
                    // chart itself, so a chart nothing could be told about stays blank rather than typed.
                    ChartType = image.GetMetadataString(FigureMetadataKeys.ChartType),
                    ValueConfidence = string.IsNullOrWhiteSpace(confidence) ? ChartValueConfidence.Descriptive : confidence,
                    Series = confidence == ChartValueConfidence.Exact ? ReadSeries(image) : [],
                    AxisX = image.GetMetadataString(FigureMetadataKeys.ChartAxisX),
                    AxisY = image.GetMetadataString(FigureMetadataKeys.ChartAxisY),
                });
            }

            objects.Add(entry);
            ordinal++;
        }
    }

    private static void AddTableObjects(
        List<KnowledgeObject> objects,
        IngestionDocument document,
        KnowledgeObjectBuildOptions options,
        string rootId,
        string articleId,
        int articleOrdinal,
        bool isExcluded,
        IReadOnlyDictionary<int, string> folios)
    {
        var ordinal = 0;

        foreach (var element in document.EnumerateContent())
        {
            if (element is not IngestionDocumentTable table || GetArticleOrdinal(element) != articleOrdinal)
            {
                continue;
            }

            var details = BuildTableDetails(table);
            var caption = table.GetMetadataString(FigureMetadataKeys.Caption);

            details.Caption = caption;

            var entry = Create(
                options,
                $"{KnowledgeContentTypes.Table}:{options.FileKey}:{articleOrdinal}:{ordinal}",
                rootId,
                articleId,
                KnowledgeContentTypes.Table,
                caption ?? options.Title,
                BuildTableContent(caption, details),
                ordinal);

            entry.PageStart = table.PageNumber;
            entry.PageEnd = table.PageNumber;
            entry.Folio = ReadFolio(folios, table.PageNumber);
            entry.Put(details);

            if (isExcluded)
            {
                entry.Status = KnowledgeObjectStatus.Excluded;
            }

            objects.Add(entry);
            ordinal++;
        }
    }

    private static KnowledgeObject Create(
        KnowledgeObjectBuildOptions options,
        string canonicalId,
        string rootId,
        string parentId,
        string objectType,
        string title,
        string content,
        int ordinal)
    {
        return new KnowledgeObject
        {
            ItemId = UniqueId.GenerateId(),
            Source = options.DataSourceId,
            CanonicalId = canonicalId,
            RootId = rootId,
            ParentId = parentId,
            ObjectType = objectType,
            Title = title,
            Content = content,
            Language = options.Language,
            IndexerId = options.IndexerId,
            SourceItemId = options.SourceItemId,
            Ordinal = ordinal,
            Status = KnowledgeObjectStatus.Ready,
        };
    }

    /// <summary>
    /// Reads the series a reader lifted off the chart's own geometry.
    /// </summary>
    /// <param name="image">The figure element.</param>
    /// <returns>The series, or none when nothing was recorded or it could not be read.</returns>
    private static List<ChartSeries> ReadSeries(IngestionDocumentImage image)
    {
        var raw = image.GetMetadataString(FigureMetadataKeys.ChartSeries);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ChartSeries>>(raw) ?? [];
        }
        catch (JsonException)
        {
            // A series that cannot be read is a chart with no series, which is honest. It is never an
            // exception, because a reader wrote this and a builder must not fail on it.
            return [];
        }
    }

    private static string BuildDocumentContent(string title, string articleText)
    {
        var body = articleText.Length > DocumentContentCharacters
            ? articleText[..DocumentContentCharacters]
            : articleText;

        return Join(title, body);
    }

    private static string BuildArticleContent(string title, string articleText)
    {
        var body = articleText.Length > DocumentContentCharacters
            ? articleText[..DocumentContentCharacters]
            : articleText;

        return Join(title, body);
    }

    /// <summary>
    /// Builds the text a figure is found by. Before a figure is transcribed that is its caption and the
    /// prose around it; afterwards it is the transcription, which is where a printed value actually lives.
    /// </summary>
    /// <param name="caption">The caption.</param>
    /// <param name="context">The surrounding body text.</param>
    /// <param name="description">The transcription, when there is one.</param>
    /// <returns>The text to embed.</returns>
    public static string BuildFigureContent(string caption, string context, string description)
    {
        return Join(caption, description, context);
    }

    private static string BuildTableContent(string caption, TableDetails details)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(caption))
        {
            builder.Append(caption);
        }

        if (details.Columns.Count > 0)
        {
            Append(builder, string.Join(" | ", details.Columns));
        }

        // Each row is serialized with its column names so a row retrieved on its own still says what its
        // values mean.
        foreach (var row in details.Rows.Take(MaxTableRowsInContent))
        {
            var cells = new List<string>();

            for (var column = 0; column < row.Length; column++)
            {
                var name = column < details.Columns.Count ? details.Columns[column] : null;

                cells.Add(string.IsNullOrWhiteSpace(name) ? row[column] : $"{name}={row[column]}");
            }

            Append(builder, string.Join("; ", cells));
        }

        return builder.ToString();
    }

    private static TableDetails BuildTableDetails(IngestionDocumentTable table)
    {
        var details = new TableDetails();
        var cells = table.Cells;

        if (cells == null || cells.Length == 0)
        {
            return details;
        }

        var rows = cells.GetLength(0);
        var columns = cells.GetLength(1);

        for (var column = 0; column < columns; column++)
        {
            details.Columns.Add(cells[0, column]?.Text ?? string.Empty);
        }

        for (var row = 1; row < rows; row++)
        {
            var values = new string[columns];

            for (var column = 0; column < columns; column++)
            {
                values[column] = cells[row, column]?.Text ?? string.Empty;
            }

            details.Rows.Add(values);
        }

        return details;
    }

    /// <summary>
    /// Finds where a chunk sits in the article text, so the pages it covers can be worked out.
    /// </summary>
    /// <param name="articleText">The whole article text.</param>
    /// <param name="chunk">The chunk.</param>
    /// <param name="searchFrom">Where to start looking, advanced past the match.</param>
    /// <returns>The character range, or <see langword="null"/> when the chunk cannot be located.</returns>
    /// <remarks>
    /// Normalization can rewrite the text between the article and its chunks, so a chunk is not always a
    /// substring of it. A chunk that cannot be found simply reports no pages, which is better than reporting
    /// the wrong ones.
    /// </remarks>
    private static (int Start, int End)? Locate(string articleText, string chunk, ref int searchFrom)
    {
        if (string.IsNullOrEmpty(articleText))
        {
            return null;
        }

        var start = articleText.IndexOf(chunk, Math.Min(searchFrom, articleText.Length), StringComparison.Ordinal);

        if (start < 0)
        {
            return null;
        }

        searchFrom = start + chunk.Length;

        return (start, searchFrom);
    }

    private static (int? PageStart, int? PageEnd) GetPageRange(
        List<TextSegment> segments,
        string articleText,
        (int Start, int End)? range)
    {
        if (range is not { } located || segments.Count == 0 || string.IsNullOrEmpty(articleText))
        {
            return (null, null);
        }

        int? first = null;
        int? last = null;
        var offset = 0;

        foreach (var segment in segments)
        {
            var end = offset + segment.Text.Length;

            if (end > located.Start && offset < located.End && segment.PageNumber.HasValue)
            {
                first ??= segment.PageNumber;
                last = segment.PageNumber;
            }

            // The segments were joined with a single newline.
            offset = end + 1;
        }

        return (first, last);
    }

    /// <summary>
    /// Determines whether a figure reads as a chart, which is what decides whether it can ever carry values.
    /// </summary>
    /// <param name="caption">The caption.</param>
    /// <param name="description">The transcription.</param>
    /// <param name="keywords">The words that say a figure is a chart, in the languages the corpus is written in.</param>
    /// <returns><see langword="true"/> when the figure is a chart.</returns>
    private static bool IsChart(string caption, string description, IReadOnlyList<string> keywords)
    {
        var words = keywords is { Count: > 0 } ? keywords : DefaultChartKeywords;

        return Mentions(caption, words) || Mentions(description, words);
    }

    private static bool Mentions(string text, IReadOnlyList<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var keyword in keywords)
        {
            if (!string.IsNullOrWhiteSpace(keyword) && text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the words that say a figure is a chart when nothing more specific is configured. A caption is
    /// written in the document's language, so the list covers the common European spellings rather than
    /// English alone; a host with another language adds to it through
    /// <see cref="KnowledgeObjectBuildOptions.ChartKeywords"/>.
    /// </summary>
    public static IReadOnlyList<string> DefaultChartKeywords { get; } =
    [
        "chart",
        "graph",
        "plot",
        "axis",
        "histogram",
        "scatter",
        "diagram",     // English, German, Dutch, Hungarian, Scandinavian
        "diagramm",    // German
        "grafikon",    // Hungarian
        "gráfico",     // Spanish, Portuguese
        "grafico",     // Italian
        "graphique",   // French
        "grafiek",     // Dutch
        "wykres",      // Polish
        "график",      // Russian, Bulgarian
        "γράφημα",     // Greek
    ];

    private static string Join(params string[] parts)
    {
        return string.Join('\n', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static void Append(StringBuilder builder, string value)
    {
        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(value);
    }

    private static int? GetInt(IngestionDocumentElement element, string key)
    {
        if (!element.HasMetadata || !element.Metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        return value is int number ? number : null;
    }

    private readonly record struct TextSegment(string Text, int? PageNumber, int ArticleOrdinal);
}
