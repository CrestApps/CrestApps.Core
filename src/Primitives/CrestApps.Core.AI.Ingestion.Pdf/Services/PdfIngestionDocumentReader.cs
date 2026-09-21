using System.Security.Cryptography;
using System.Text.Json;
using CrestApps.Core.AI.Ingestion.Knowledge.Structure;
using CrestApps.Core.Ingestion;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Tokens;

namespace CrestApps.Core.AI.Ingestion.Pdf.Services;

/// <summary>
/// Reads PDF files into an <see cref="IngestionDocument"/> using PdfPig.
/// </summary>
/// <remarks>
/// Pages are segmented into blocks and put into reading order, so a two column article reads down one column
/// and then the other rather than across both. Blocks that repeat at the edge of every page are classified as
/// decoration and left out, guarded so that body text can never be mistaken for a running head.
/// </remarks>
public sealed class PdfIngestionDocumentReader : IngestionDocumentReader
{
    private const string PdfMediaType = "application/pdf";

    // The classifier's own defaults. Concurrency is pinned to one task: the reader is a singleton on an
    // already asynchronous path, and an unbounded degree of parallelism there buys nothing and makes the
    // output ordering harder to reason about.
    private const double DecorationSimilarityThreshold = 0.25;
    private const int DecorationBlocksPerPage = 5;
    private const int DecorationMaxDegreeOfParallelism = 1;

    // Reading order is decided by geometry alone. The shared instance also consults the order the runs were
    // drawn in, and that is precisely what cannot be trusted: a page laid out in columns is routinely drawn
    // across them, which is how a two column article extracts as interleaved half sentences.
    private static readonly UnsupervisedReadingOrderDetector _readingOrderDetector = new(
        5,
        UnsupervisedReadingOrderDetector.SpatialReasoningRules.ColumnWise,
        useRenderingOrder: false);

    private readonly PdfLayoutOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfIngestionDocumentReader"/> class with default layout
    /// options.
    /// </summary>
    public PdfIngestionDocumentReader()
        : this(new PdfLayoutOptions(), NullLogger<PdfIngestionDocumentReader>.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfIngestionDocumentReader"/> class.
    /// </summary>
    /// <param name="options">The layout options.</param>
    public PdfIngestionDocumentReader(IOptions<PdfLayoutOptions> options)
        : this(options?.Value, NullLogger<PdfIngestionDocumentReader>.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfIngestionDocumentReader"/> class.
    /// </summary>
    /// <param name="options">The layout options.</param>
    /// <param name="logger">The logger.</param>
    public PdfIngestionDocumentReader(
        IOptions<PdfLayoutOptions> options,
        ILogger<PdfIngestionDocumentReader> logger)
        : this(options?.Value, logger)
    {
    }

    private PdfIngestionDocumentReader(PdfLayoutOptions options, ILogger logger)
    {
        _options = options ?? new PdfLayoutOptions();
        _logger = logger ?? NullLogger<PdfIngestionDocumentReader>.Instance;
    }

    /// <summary>
    /// Reads the operation.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="identifier">The identifier.</param>
    /// <param name="mediaType">The media type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(mediaType, PdfMediaType, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Media type '{mediaType}' is not supported. Only '{PdfMediaType}' is accepted.");
        }

        ArgumentNullException.ThrowIfNull(source);

        MemoryStream buffer = null;
        var workingStream = source;

        if (source.CanSeek)
        {
            source.Position = 0;
        }
        else
        {
            buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            workingStream = buffer;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var pdf = PdfDocument.Open(workingStream);

            if (!_options.UseLayoutAnalysis)
            {
                return ReadRawPageText(pdf, identifier, cancellationToken);
            }

            try
            {
                return ReadWithLayoutAnalysis(pdf, identifier, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Layout analysis is an improvement on reading the text, not a precondition for it. A page
                // whose glyph geometry defeats the segmenter must cost this document its structure, not its
                // content, so fall back to the reader that shipped before any of this existed.
                _logger.LogWarning(ex, "Layout analysis failed for '{Identifier}'. Falling back to raw page text.", identifier);

                return ReadRawPageText(pdf, identifier, cancellationToken);
            }
        }
        finally
        {
            if (buffer != null)
            {
                await buffer.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Reads the document the way it was read before layout analysis existed: the raw content-stream text,
    /// one paragraph per page. This is the escape hatch when segmentation misbehaves on a corpus.
    /// </summary>
    /// <param name="pdf">The opened document.</param>
    /// <param name="identifier">The document identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document.</returns>
    private IngestionDocument ReadRawPageText(PdfDocument pdf, string identifier, CancellationToken cancellationToken)
    {
        var document = new IngestionDocument(identifier);

        for (var pageNumber = 1; pageNumber <= pdf.NumberOfPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageText = pdf.GetPage(pageNumber).Text?.Trim();

            if (string.IsNullOrWhiteSpace(pageText))
            {
                continue;
            }

            var section = new IngestionDocumentSection
            {
                PageNumber = pageNumber,
            };

            section.Elements.Add(new IngestionDocumentParagraph(pageText)
            {
                Text = pageText,
                PageNumber = pageNumber,
            });

            document.Sections.Add(section);
        }

        CaptureOutline(pdf, document, identifier);

        return document;
    }

    private IngestionDocument ReadWithLayoutAnalysis(PdfDocument pdf, string identifier, CancellationToken cancellationToken)
    {
        // The decoration classifier compares every page against every other, so all pages have to be
        // segmented before any block can be classified.
        var heights = new List<double>(pdf.NumberOfPages);
        var pages = new List<IReadOnlyList<TextBlock>>(pdf.NumberOfPages);

        // Pages whose segmentation threw, as opposed to pages that simply hold no letters. The first kind
        // still has text worth keeping and gets it back unsegmented; the second is a scanned image and is
        // handled by the figure path.
        var unsegmented = new HashSet<int>();

        for (var pageNumber = 1; pageNumber <= pdf.NumberOfPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = pdf.GetPage(pageNumber);

            heights.Add(page.Height);

            try
            {
                pages.Add(SegmentPage(page));
            }
            catch (Exception ex)
            {
                // One page the segmenter cannot make sense of is one page without structure, not a document
                // that cannot be read.
                _logger.LogWarning(ex, "Segmenting page {PageNumber} of '{Identifier}' failed. Its text is kept unsegmented.", pageNumber, identifier);

                unsegmented.Add(pageNumber);
                pages.Add([]);
            }
        }

        var decoration = GetDecorationBlocks(pages);
        var document = new IngestionDocument(identifier);
        var figureIdsByHash = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageNumber = pageIndex + 1;
            var page = pdf.GetPage(pageNumber);
            var section = new IngestionDocumentSection
            {
                PageNumber = pageNumber,
            };

            section.Metadata[ElementMetadataKeys.PageWidth] = page.Width;
            section.Metadata[ElementMetadataKeys.PageHeight] = page.Height;

            var taggedHeadings = ReadTaggedHeadings(page, identifier);

            foreach (var block in pages[pageIndex])
            {
                var element = CreateElement(block, pageNumber, decoration.Contains(block), heights[pageIndex], taggedHeadings);

                if (element != null)
                {
                    section.Elements.Add(element);
                }
            }

            if (unsegmented.Contains(pageNumber))
            {
                var pageText = page.Text?.Trim();

                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    section.Elements.Add(new IngestionDocumentParagraph(pageText)
                    {
                        Text = pageText,
                        PageNumber = pageNumber,
                    });
                }
            }

            var segments = _options.EmitTables || _options.EmitVectorFigures
                ? ReadSegments(page, identifier)
                : [];

            var tableBounds = new List<double[]>();

            if (_options.EmitTables)
            {
                AddTables(section, page, segments, tableBounds);
            }

            if (_options.EmitImages)
            {
                AddImages(section, page, identifier, figureIdsByHash);
            }

            if (_options.EmitVectorFigures)
            {
                AddVectorFigures(section, page, identifier, segments, tableBounds, figureIdsByHash);
            }

            if (section.Elements.Count > 0)
            {
                document.Sections.Add(section);
            }
        }

        CaptureOutline(pdf, document, identifier);

        return document;
    }

    /// <summary>
    /// Records the document's own outline, when it has one.
    /// </summary>
    /// <param name="pdf">The opened document.</param>
    /// <param name="document">The document being built.</param>
    /// <param name="identifier">The document identifier, used only for logging.</param>
    /// <remarks>
    /// An outline is the document stating its own structure, which beats anything that can be inferred from
    /// how a page looks. Manuals, reports and books carry one almost without exception; magazines and
    /// newspapers rarely do, and they fall through to the signals that suit them.
    /// <para>
    /// Nothing here may fail a read. A malformed outline is a document without one, which is exactly what
    /// the analyzer already copes with.
    /// </para>
    /// </remarks>
    private void CaptureOutline(PdfDocument pdf, IngestionDocument document, string identifier)
    {
        if (document.Sections.Count == 0)
        {
            return;
        }

        try
        {
            if (!pdf.TryGetBookmarks(out var bookmarks) || bookmarks.Roots.Count == 0)
            {
                return;
            }

            var entries = new List<DocumentOutlineEntry>();

            foreach (var root in bookmarks.Roots)
            {
                CollectOutline(root, entries);
            }

            if (entries.Count == 0)
            {
                return;
            }

            // The outline describes the whole file, and an IngestionDocument carries no metadata of its own,
            // so the first section is where it has to live.
            document.Sections[0].Metadata[ElementMetadataKeys.Outline] = entries;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "Could not read the outline of '{Identifier}'. The document is treated as having none.", identifier);
            }
        }
    }

    /// <summary>
    /// Flattens one outline node and its descendants into document order.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="entries">The entries collected so far.</param>
    /// <remarks>
    /// A node that groups others without pointing anywhere itself — a part that is only a heading over its
    /// chapters — takes the page of the first descendant that does point somewhere. Dropping it instead
    /// would flatten its children up a level and lose the nesting the document went to the trouble of
    /// stating.
    /// </remarks>
    private static void CollectOutline(BookmarkNode node, List<DocumentOutlineEntry> entries)
    {
        var page = node is DocumentBookmarkNode document ? document.PageNumber : FindFirstPage(node);
        var title = node.Title?.Trim();

        if (page > 0 && !string.IsNullOrEmpty(title))
        {
            entries.Add(new DocumentOutlineEntry
            {
                Title = title,
                Level = node.Level,
                PageNumber = page,
            });
        }

        foreach (var child in node.Children)
        {
            CollectOutline(child, entries);
        }
    }

    /// <summary>
    /// Finds the first page any descendant of a node points at.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The page, or zero when nothing beneath it points anywhere.</returns>
    private static int FindFirstPage(BookmarkNode node)
    {
        foreach (var child in node.Children)
        {
            if (child is DocumentBookmarkNode document)
            {
                return document.PageNumber;
            }

            var page = FindFirstPage(child);

            if (page > 0)
            {
                return page;
            }
        }

        return 0;
    }

    /// <summary>
    /// Reads the lines a page draws, unless there are so many that reading them would cost minutes.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="identifier">The document identifier, used only for logging.</param>
    /// <returns>The segments, or none when the page is too dense to group.</returns>
    /// <remarks>
    /// Both table and figure detection group segments by comparing every one with every other. A page of
    /// dense vector artwork — a map, an advertisement drawn as geometry — can carry tens of thousands, and
    /// past the ceiling the page keeps its text and its placed images and simply reports no drawn tables or
    /// figures. Tables laid out with whitespace alone are still found, because that detector reads words.
    /// </remarks>
    private List<PdfSegment> ReadSegments(Page page, string identifier)
    {
        List<PdfSegment> segments;

        try
        {
            segments = PdfPageGeometry.ReadSegments(page);
        }
        catch (Exception ex)
        {
            // A page whose geometry cannot be read keeps its text. Nothing here may cost the page that.
            _logger.LogWarning(ex, "Reading the drawn geometry failed on page {PageNumber} of '{Identifier}'.", page.Number, identifier);

            return [];
        }

        if (_options.MaxVectorSegmentsPerPage <= 0 || segments.Count <= _options.MaxVectorSegmentsPerPage)
        {
            return segments;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "PDF ingestion: skipped drawn table and figure detection on page {PageNumber} of '{Identifier}' because it draws {Count} segments, above the limit of {Limit}.",
                page.Number,
                identifier,
                segments.Count,
                _options.MaxVectorSegmentsPerPage);
        }

        return [];
    }

    /// <summary>
    /// Emits one table element per ruled table on the page.
    /// </summary>
    /// <param name="section">The section being built.</param>
    /// <param name="page">The page.</param>
    /// <param name="segments">The lines the page draws.</param>
    /// <param name="tableBounds">Collects the regions the tables occupy, so a table grid is never also read as a drawing.</param>
    private void AddTables(IngestionDocumentSection section, Page page, IReadOnlyList<PdfSegment> segments, List<double[]> tableBounds)
    {
        List<DetectedTable> tables;

        try
        {
            tables = PdfTableDetector.Detect(page, segments);

            // A table the page rules is found from its rules. One laid out with nothing but alignment is
            // found from the alignment, and only where a ruled table did not already claim the region.
            tables.AddRange(PdfGridTableDetector.Detect(page, tables.Select(table => table.Bounds).ToList()));
        }
        catch (Exception ex)
        {
            // A table that could not be read is a table read as prose, which is what it was before. Nothing
            // here may cost the page its text.
            _logger.LogWarning(ex, "Table detection failed on page {PageNumber}.", page.Number);

            return;
        }

        foreach (var table in tables)
        {
            // Normalized here for the same reason prose is: a ligature or a hyphenated break left in a cell
            // makes that word unsearchable, and a table is where the words worth searching for often sit.
            var markdown = PdfTextNormalizer.Normalize(table.ToMarkdown());

            if (string.IsNullOrWhiteSpace(markdown))
            {
                continue;
            }

            var cells = new IngestionDocumentElement[table.RowCount, table.ColumnCount];

            for (var row = 0; row < table.RowCount; row++)
            {
                for (var column = 0; column < table.ColumnCount; column++)
                {
                    var text = PdfTextNormalizer.Normalize(table.Cells[row][column] ?? string.Empty);

                    // An empty cell is a real cell: the table is a grid, and dropping the blanks would
                    // shift every value after them into the wrong column.
                    cells[row, column] = new IngestionDocumentParagraph(text.Length == 0 ? " " : text)
                    {
                        Text = text,
                        PageNumber = page.Number,
                    };
                }
            }

            var element = new IngestionDocumentTable(markdown, cells)
            {
                Text = markdown,
                PageNumber = page.Number,
            };

            element.Metadata[ElementMetadataKeys.BoundingBox] = table.Bounds;
            section.Elements.Add(element);
            tableBounds.Add(table.Bounds);
        }
    }

    /// <summary>
    /// Emits one image element per figure the page draws rather than places, rendered as a line drawing.
    /// </summary>
    /// <param name="section">The section being built.</param>
    /// <param name="page">The page.</param>
    /// <param name="identifier">The document identifier, which figure identifiers are built from.</param>
    /// <param name="segments">The lines the page draws.</param>
    /// <param name="tableBounds">The regions the page's tables occupy.</param>
    /// <param name="figureIdsByHash">The first figure seen for each set of bytes, across the whole document.</param>
    private void AddVectorFigures(
        IngestionDocumentSection section,
        Page page,
        string identifier,
        List<PdfSegment> segments,
        IReadOnlyList<double[]> tableBounds,
        Dictionary<string, string> figureIdsByHash)
    {
        List<DetectedVectorFigure> figures;

        try
        {
            figures = PdfVectorFigureDetector.Detect(page, segments, tableBounds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector figure detection failed on page {PageNumber}.", page.Number);

            return;
        }

        var ordinal = section.Elements.Count(element => element is IngestionDocumentImage);

        foreach (var figure in figures)
        {
            var content = PngLineDrawingWriter.Write(figure.Segments, figure.Bounds);

            if (content is null)
            {
                continue;
            }

            ordinal++;

            var figureId = $"{identifier}-p{page.Number}-v{ordinal}";
            var element = new IngestionDocumentImage($"![]({figureId})")
            {
                Content = content,
                MediaType = "image/png",
                PageNumber = page.Number,
            };

            element.Metadata[FigureMetadataKeys.Id] = figureId;
            element.Metadata[ElementMetadataKeys.BoundingBox] = figure.Bounds;
            element.Metadata[FigureMetadataKeys.ImageOrdinal] = ordinal;
            element.Metadata[FigureMetadataKeys.IsVectorFigure] = true;
            element.Metadata[FigureMetadataKeys.ContentHash] = Convert.ToHexStringLower(SHA256.HashData(content));

            var hash = (string)element.Metadata[FigureMetadataKeys.ContentHash];

            if (figureIdsByHash.TryGetValue(hash, out var firstFigureId))
            {
                element.Metadata[FigureMetadataKeys.DuplicateOf] = firstFigureId;
            }
            else
            {
                figureIdsByHash[hash] = figureId;
            }

            AddChartValues(element, page, figure);

            section.Elements.Add(element);
        }
    }

    /// <summary>
    /// Reads the chart's series off its own geometry, when its axes can be solved from their printed tick
    /// labels.
    /// </summary>
    /// <param name="element">The figure element.</param>
    /// <param name="page">The page.</param>
    /// <param name="figure">The detected figure.</param>
    private void AddChartValues(IngestionDocumentImage element, Page page, DetectedVectorFigure figure)
    {
        ChartExtraction extraction;

        try
        {
            extraction = VectorPathChartDataExtractor.Extract(page, figure);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chart extraction failed on page {PageNumber}.", page.Number);

            return;
        }

        element.Metadata[FigureMetadataKeys.ValueConfidence] = extraction.ValueConfidence;

        if (extraction.Series.Count > 0)
        {
            element.Metadata[FigureMetadataKeys.ChartSeries] = JsonSerializer.Serialize(extraction.Series);
        }

        // Each of these is written only when the chart itself stated it. A key left off says the chart did
        // not say, which is the honest answer; a key written with a fallback would say it did.
        if (!string.IsNullOrEmpty(extraction.ChartType))
        {
            element.Metadata[FigureMetadataKeys.ChartType] = extraction.ChartType;
        }

        if (!string.IsNullOrEmpty(extraction.AxisX))
        {
            element.Metadata[FigureMetadataKeys.ChartAxisX] = extraction.AxisX;
        }

        if (!string.IsNullOrEmpty(extraction.AxisY))
        {
            element.Metadata[FigureMetadataKeys.ChartAxisY] = extraction.AxisY;
        }
    }

    /// <summary>
    /// Emits one image element per figure on the page. An image keeps no text of its own: an undescribed
    /// figure has nothing to say, and inventing something for it would put words into the index that are
    /// not in the document.
    /// </summary>
    /// <param name="section">The section being built.</param>
    /// <param name="page">The page the images are read from.</param>
    /// <param name="identifier">The document identifier, which figure identifiers are built from.</param>
    /// <param name="figureIdsByHash">The first figure seen for each set of bytes, across the whole document.</param>
    private void AddImages(
        IngestionDocumentSection section,
        Page page,
        string identifier,
        Dictionary<string, string> figureIdsByHash)
    {
        var ordinal = 0;
        var unsupported = 0;

        foreach (var image in page.GetImages())
        {
            if (image.WidthInSamples < _options.MinImageSamples || image.HeightInSamples < _options.MinImageSamples)
            {
                continue;
            }

            if (!TryGetImageContent(image, out var content, out var mediaType))
            {
                unsupported++;

                continue;
            }

            ordinal++;

            var figureId = $"{identifier}-p{page.Number}-{ordinal}";
            var element = new IngestionDocumentImage($"![]({figureId})")
            {
                Content = content,
                MediaType = mediaType,
                PageNumber = page.Number,
            };

            var bounds = image.BoundingBox;

            element.Metadata[FigureMetadataKeys.Id] = figureId;
            element.Metadata[ElementMetadataKeys.BoundingBox] = new[] { bounds.Left, bounds.Bottom, bounds.Right, bounds.Top };
            element.Metadata[FigureMetadataKeys.ImageOrdinal] = ordinal;
            element.Metadata[FigureMetadataKeys.PixelWidth] = image.WidthInSamples;
            element.Metadata[FigureMetadataKeys.PixelHeight] = image.HeightInSamples;
            element.Metadata[FigureMetadataKeys.IsInlineImage] = image.IsInlineImage;

            var hash = Convert.ToHexStringLower(SHA256.HashData(content));

            element.Metadata[FigureMetadataKeys.ContentHash] = hash;

            // The same artwork placed twice is one figure, not two. The repeat keeps its element so a page
            // still reports what is on it, but it points back at the figure it duplicates.
            if (figureIdsByHash.TryGetValue(hash, out var firstFigureId))
            {
                element.Metadata[FigureMetadataKeys.DuplicateOf] = firstFigureId;
            }
            else
            {
                figureIdsByHash[hash] = figureId;
            }

            section.Elements.Add(element);
        }

        if (unsupported > 0)
        {
            section.Metadata[ElementMetadataKeys.UnsupportedImageCount] = unsupported;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("PDF ingestion: skipped {Count} image(s) on page {PageNumber} of '{Identifier}' because their encoding is not supported.", unsupported, page.Number, identifier);
            }
        }
    }

    /// <summary>
    /// Reads the bytes of one image in a form something downstream can actually open.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="content">The decoded bytes.</param>
    /// <param name="mediaType">The media type of the decoded bytes.</param>
    /// <returns><see langword="true"/> when the image could be decoded.</returns>
    private static bool TryGetImageContent(IPdfImage image, out byte[] content, out string mediaType)
    {
        if (image.TryGetPng(out var png) && png != null && png.Length > 0)
        {
            content = png;
            mediaType = "image/png";

            return true;
        }

        // A JPEG is already a file the moment it leaves the content stream, so it needs no decoding at all.
        if (IsJpeg(image))
        {
            var raw = image.RawBytes.ToArray();

            if (StartsWithJpegMarker(raw))
            {
                content = raw;
                mediaType = "image/jpeg";

                return true;
            }
        }

        content = null;
        mediaType = null;

        return false;
    }

    /// <summary>
    /// Says whether an image's bytes are a JPEG file as they stand.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <returns><see langword="true"/> when the only filter applied is the JPEG one.</returns>
    /// <remarks>
    /// The raw bytes carry every filter the producer applied, so they are a JPEG only when the JPEG filter
    /// is the whole of the chain. A stream filtered <c>[/ASCII85Decode /DCTDecode]</c> — ordinary output
    /// from a PostScript workflow — is still ASCII85 text at this point, and handing it out labelled as a
    /// JPEG stored an unopenable file, hashed the wrong bytes for the duplicate and transcription caches,
    /// and sent that text to a vision model as though it were a picture.
    /// </remarks>
    private static bool IsJpeg(IPdfImage image)
    {
        if (image.ImageDictionary == null)
        {
            return false;
        }

        if (!image.ImageDictionary.TryGet(NameToken.Create("Filter"), out var filter) &&
            !image.ImageDictionary.TryGet(NameToken.Create("F"), out filter))
        {
            return false;
        }

        return filter switch
        {
            NameToken name => IsJpegFilterName(name.Data),
            ArrayToken array => array.Data.Count == 1 &&
                array.Data[0] is NameToken only &&
                IsJpegFilterName(only.Data),
            _ => false,
        };
    }

    /// <summary>
    /// Checks the two bytes that begin every JPEG file.
    /// </summary>
    /// <param name="content">The bytes.</param>
    /// <returns><see langword="true"/> when the bytes open with the JPEG start-of-image marker.</returns>
    private static bool StartsWithJpegMarker(byte[] content)
    {
        return content.Length > 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF;
    }

    private static bool IsJpegFilterName(string name)
    {
        return string.Equals(name, "DCTDecode", StringComparison.Ordinal) ||
            string.Equals(name, "DCT", StringComparison.Ordinal);
    }

    private static List<TextBlock> SegmentPage(Page page)
    {
        var letters = page.Letters;

        if (letters == null || letters.Count == 0)
        {
            return [];
        }

        var words = NearestNeighbourWordExtractor.Instance.GetWords(letters);
        var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);

        if (blocks.Count == 0)
        {
            return [];
        }

        return _readingOrderDetector
            .Get(blocks)
            .OrderBy(block => block.ReadingOrder)
            .ToList();
    }

    /// <summary>
    /// Classifies the blocks that repeat at the edge of every page, then applies the guards that keep body
    /// text out of the result. The classifier is a heuristic, and a heuristic that silently deletes prose is
    /// worse than one that leaves a running head in.
    /// </summary>
    /// <param name="pages">The segmented blocks of every page.</param>
    /// <returns>The blocks safe to treat as decoration.</returns>
    private HashSet<TextBlock> GetDecorationBlocks(List<IReadOnlyList<TextBlock>> pages)
    {
        var decoration = new HashSet<TextBlock>(TextBlockReferenceComparer.Instance);

        if (!_options.StripDecoration || pages.Count == 0)
        {
            return decoration;
        }

        IReadOnlyList<IReadOnlyList<TextBlock>> classified;

        try
        {
            classified = DecorationTextBlockClassifier.Get(
                pages,
                DecorationSimilarityThreshold,
                DecorationBlocksPerPage,
                DecorationMaxDegreeOfParallelism);
        }
        catch (Exception)
        {
            // Classification is an optimization, never a requirement. A document it cannot handle keeps all
            // of its text rather than failing to read.
            return decoration;
        }

        for (var pageIndex = 0; pageIndex < classified.Count && pageIndex < pages.Count; pageIndex++)
        {
            var candidates = classified[pageIndex];

            if (candidates == null || candidates.Count == 0)
            {
                continue;
            }

            // A page whose only block was classified as decoration would be emptied outright.
            if (pages[pageIndex].Count <= 1)
            {
                continue;
            }

            foreach (var block in candidates)
            {
                if (IsSafeToDrop(block))
                {
                    decoration.Add(block);
                }
            }
        }

        return decoration;
    }

    private bool IsSafeToDrop(TextBlock block)
    {
        var text = block.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (text.Length > _options.MaxDecorationCharacters)
        {
            return false;
        }

        return !TextSegmentation.ContainsSentenceBoundary(text);
    }

    /// <summary>
    /// Reads the headings a tagged PDF marks, keyed by the text they carry.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="identifier">The document identifier, used only for logging.</param>
    /// <returns>The heading level of each marked heading, or an empty map for an untagged page.</returns>
    /// <remarks>
    /// A tagged PDF says which of its text is a heading and at what rank, which is the thing every other
    /// signal in this reader has to infer. Accessible documents — anything published under a policy that
    /// requires it, which is most government and much corporate output — carry these tags.
    /// <para>
    /// Marked content and the blocks layout analysis produces are two different views of a page, and the
    /// honest way to join them is the text they carry: a block whose text is a marked heading's text is that
    /// heading. Matching on geometry would mean reconciling two sets of bounds that were never meant to
    /// agree.
    /// </para>
    /// </remarks>
    private Dictionary<string, int> ReadTaggedHeadings(Page page, string identifier)
    {
        var headings = new Dictionary<string, int>(StringComparer.Ordinal);

        try
        {
            foreach (var content in page.GetMarkedContents())
            {
                CollectTaggedHeadings(content, headings);
            }
        }
        catch (Exception ex)
        {
            // A page whose marked content cannot be read is a page without tags, which is the ordinary case.
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "Could not read the marked content of '{Identifier}'. The page is treated as untagged.", identifier);
            }
        }

        return headings;
    }

    /// <summary>
    /// Collects one marked-content element and its descendants.
    /// </summary>
    /// <param name="content">The marked content.</param>
    /// <param name="headings">The headings collected so far.</param>
    private static void CollectTaggedHeadings(MarkedContentElement content, Dictionary<string, int> headings)
    {
        if (!content.IsArtifact)
        {
            var level = GetTagHeadingLevel(content.Tag);

            if (level > 0)
            {
                // ActualText is what the document says the content reads as, which is what a tag exists to
                // provide when the glyphs themselves do not spell it.
                var text = PdfTextNormalizer.Normalize(
                    string.IsNullOrWhiteSpace(content.ActualText)
                        ? string.Concat(content.Letters.Select(letter => letter.Value))
                        : content.ActualText);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    headings[text] = level;
                }
            }
        }

        foreach (var child in content.Children)
        {
            CollectTaggedHeadings(child, headings);
        }
    }

    /// <summary>
    /// Reads the heading level a structure tag names.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The one-based level, or zero when the tag is not a heading.</returns>
    /// <remarks>
    /// <c>H1</c> to <c>H6</c> state their rank. A bare <c>H</c> is a heading whose rank the document leaves
    /// to its position in the structure tree; it is taken as the outermost, which is what it is in the
    /// documents that use it at all.
    /// </remarks>
    private static int GetTagHeadingLevel(string tag)
    {
        if (string.IsNullOrEmpty(tag) || tag[0] is not ('h' or 'H'))
        {
            return 0;
        }

        if (tag.Length == 1)
        {
            return 1;
        }

        return tag.Length == 2 && tag[1] is >= '1' and <= '6' ? tag[1] - '0' : 0;
    }

    private IngestionDocumentElement CreateElement(
        TextBlock block,
        int pageNumber,
        bool isDecoration,
        double pageHeight,
        Dictionary<string, int> taggedHeadings)
    {
        var text = PdfTextNormalizer.Normalize(block.Text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (isDecoration && !_options.EmitDecorationAsHeaderFooter)
        {
            return null;
        }

        IngestionDocumentElement element;

        if (isDecoration)
        {
            var isHeader = block.BoundingBox.Bottom >= pageHeight * 2 / 3;

            element = isHeader
                ? new IngestionDocumentHeader(text)
                : new IngestionDocumentFooter(text);
        }
        else
        {
            element = new IngestionDocumentParagraph(text);
        }

        // Both Text and PageNumber are assigned on every element. The constructor argument is the markdown
        // rendering; Text defaults to null, and an element with no Text is invisible to every consumer.
        element.Text = text;
        element.PageNumber = pageNumber;

        var bounds = block.BoundingBox;

        element.Metadata[ElementMetadataKeys.BoundingBox] = new[] { bounds.Left, bounds.Bottom, bounds.Right, bounds.Top };

        // A rank the document tagged outranks anything measured from type size, and it is the only structural
        // signal a heading set in body-sized type ever carries.
        if (!isDecoration && taggedHeadings.Count > 0 && taggedHeadings.TryGetValue(text, out var headingLevel))
        {
            element.Metadata[ElementMetadataKeys.HeadingLevel] = headingLevel;
        }

        var modalPointSize = GetModalPointSize(block);

        if (modalPointSize.HasValue)
        {
            element.Metadata[ElementMetadataKeys.ModalPointSize] = modalPointSize.Value;
        }

        var modalFontName = GetModalFontName(block);

        if (!string.IsNullOrEmpty(modalFontName))
        {
            element.Metadata[ElementMetadataKeys.ModalFontName] = modalFontName;
        }

        if (isDecoration)
        {
            element.Metadata[ElementMetadataKeys.IsDecoration] = true;
        }

        return element;
    }

    private static double? GetModalPointSize(TextBlock block)
    {
        var group = EnumerateLetters(block)
            .GroupBy(letter => Math.Round(letter.PointSize, 2))
            .OrderByDescending(entry => entry.Count())
            .ThenByDescending(entry => entry.Key)
            .FirstOrDefault();

        return group?.Key;
    }

    private static string GetModalFontName(TextBlock block)
    {
        var group = EnumerateLetters(block)
            .Where(letter => !string.IsNullOrEmpty(letter.FontName))
            .GroupBy(letter => letter.FontName, StringComparer.Ordinal)
            .OrderByDescending(entry => entry.Count())
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        return group?.Key;
    }

    private static IEnumerable<Letter> EnumerateLetters(TextBlock block)
    {
        foreach (var line in block.TextLines)
        {
            foreach (var word in line.Words)
            {
                foreach (var letter in word.Letters)
                {
                    yield return letter;
                }
            }
        }
    }

    /// <summary>
    /// Compares blocks by identity. The classifier hands back the very instances it was given, and two
    /// distinct blocks holding the same running head must not collapse into one.
    /// </summary>
    private sealed class TextBlockReferenceComparer : IEqualityComparer<TextBlock>
    {
        /// <summary>
        /// Gets the shared comparer.
        /// </summary>
        public static TextBlockReferenceComparer Instance { get; } = new();

        /// <summary>
        /// Determines whether two blocks are the same instance.
        /// </summary>
        /// <param name="x">The first block.</param>
        /// <param name="y">The second block.</param>
        /// <returns><see langword="true"/> when both references point at the same block.</returns>
        public bool Equals(TextBlock x, TextBlock y)
        {
            return ReferenceEquals(x, y);
        }

        /// <summary>
        /// Gets the identity hash code of a block.
        /// </summary>
        /// <param name="obj">The block.</param>
        /// <returns>The identity hash code.</returns>
        public int GetHashCode(TextBlock obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
