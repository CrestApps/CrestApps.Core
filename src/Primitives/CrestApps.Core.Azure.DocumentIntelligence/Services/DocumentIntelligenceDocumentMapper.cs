using System.Globalization;
using System.Text;
using Azure.AI.DocumentIntelligence;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Processors;
using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.Azure.DocumentIntelligence.Services;

/// <summary>
/// Turns an analysis result into the element model the rest of the ingestion pipeline works on.
/// </summary>
/// <remarks>
/// This is the whole point of the provider path: paragraph roles, reading order, table structure and figure
/// captions arrive as facts rather than as inferences from glyph positions. The mapping is kept separate from
/// the client so it can be tested without a service.
/// </remarks>
internal static class DocumentIntelligenceDocumentMapper
{
    /// <summary>
    /// The identifier the service uses for a figure, kept so its image can be downloaded afterwards.
    /// </summary>
    public const string ProviderFigureIdKey = "crestapps.provider.figureId";

    private const double PointsPerInch = 72;

    /// <summary>
    /// Maps an analysis result onto an ingestion document.
    /// </summary>
    /// <param name="result">The analysis result.</param>
    /// <param name="identifier">The document identifier.</param>
    /// <returns>The mapped document.</returns>
    public static IngestionDocument Map(AnalyzeResult result, string identifier)
    {
        ArgumentNullException.ThrowIfNull(result);

        var pages = BuildPageLookup(result);
        var entries = new List<MappedElement>();
        var suppressed = new List<Span>();

        AddTables(result, pages, entries, suppressed);
        AddFigures(result, identifier, pages, entries, suppressed);
        AddParagraphs(result, pages, entries, suppressed);

        return Assemble(identifier, entries, pages);
    }

    private static void AddTables(
        AnalyzeResult result,
        Dictionary<int, DocumentPage> pages,
        List<MappedElement> entries,
        List<Span> suppressed)
    {
        if (result.Tables == null)
        {
            return;
        }

        foreach (var table in result.Tables)
        {
            var region = GetFirstRegion(table.BoundingRegions);
            var pageNumber = region?.PageNumber ?? 1;
            var cells = BuildCells(table);
            var markdown = RenderMarkdown(cells);

            var element = new IngestionDocumentTable(markdown, cells)
            {
                Text = markdown,
                PageNumber = pageNumber,
            };

            ApplyBounds(element, region, pages);

            if (table.Caption != null && !string.IsNullOrWhiteSpace(table.Caption.Content))
            {
                element.Metadata[FigureMetadataKeys.Caption] = table.Caption.Content;
                element.Metadata[FigureMetadataKeys.CaptionSource] = CaptionSources.Provider;
                Suppress(suppressed, table.Caption.Spans);
            }

            entries.Add(new MappedElement(GetOffset(table.Spans), pageNumber, element));

            // The cell text is already carried by the table, so the paragraphs covering the same span must
            // not be emitted a second time.
            Suppress(suppressed, table.Spans);
        }
    }

    private static void AddFigures(
        AnalyzeResult result,
        string identifier,
        Dictionary<int, DocumentPage> pages,
        List<MappedElement> entries,
        List<Span> suppressed)
    {
        if (result.Figures == null)
        {
            return;
        }

        var ordinalByPage = new Dictionary<int, int>();

        foreach (var figure in result.Figures)
        {
            var region = GetFirstRegion(figure.BoundingRegions);
            var pageNumber = region?.PageNumber ?? 1;

            ordinalByPage.TryGetValue(pageNumber, out var ordinal);
            ordinal++;
            ordinalByPage[pageNumber] = ordinal;

            var figureId = $"{identifier}-p{pageNumber}-{ordinal}";
            var element = new IngestionDocumentImage($"![]({figureId})")
            {
                PageNumber = pageNumber,
            };

            element.Metadata[FigureMetadataKeys.Id] = figureId;
            element.Metadata[FigureMetadataKeys.ImageOrdinal] = ordinal;
            ApplyBounds(element, region, pages);

            if (!string.IsNullOrWhiteSpace(figure.Id))
            {
                element.Metadata[ProviderFigureIdKey] = figure.Id;
            }

            var caption = figure.Caption?.Content;

            if (!string.IsNullOrWhiteSpace(caption))
            {
                element.Metadata[FigureMetadataKeys.Caption] = caption;
                element.Metadata[FigureMetadataKeys.CaptionSource] = CaptionSources.Provider;
                element.Metadata[FigureMetadataKeys.Bucket] = CaptionBuckets.Figure;

                var parsed = ParseOrdinal(caption);

                if (parsed.HasValue)
                {
                    element.Metadata[FigureMetadataKeys.Ordinal] = parsed.Value;
                }

                // The caption belongs to the figure, so the paragraph carrying it is not emitted again.
                Suppress(suppressed, figure.Caption.Spans);
            }

            entries.Add(new MappedElement(GetOffset(figure.Spans), pageNumber, element));
        }
    }

    private static void AddParagraphs(
        AnalyzeResult result,
        Dictionary<int, DocumentPage> pages,
        List<MappedElement> entries,
        List<Span> suppressed)
    {
        if (result.Paragraphs == null)
        {
            return;
        }

        foreach (var paragraph in result.Paragraphs)
        {
            if (string.IsNullOrWhiteSpace(paragraph.Content))
            {
                continue;
            }

            var offset = GetOffset(paragraph.Spans);

            if (IsSuppressed(suppressed, offset))
            {
                continue;
            }

            var region = GetFirstRegion(paragraph.BoundingRegions);
            var pageNumber = region?.PageNumber ?? 1;
            var role = paragraph.Role;
            var isDecoration = role == ParagraphRole.PageHeader
                || role == ParagraphRole.PageFooter
                || role == ParagraphRole.PageNumber;

            IngestionDocumentElement element;

            if (isDecoration)
            {
                element = role == ParagraphRole.PageHeader
                    ? new IngestionDocumentHeader(paragraph.Content)
                    : new IngestionDocumentFooter(paragraph.Content);
            }
            else
            {
                element = new IngestionDocumentParagraph(paragraph.Content);
            }

            element.Text = paragraph.Content;
            element.PageNumber = pageNumber;
            ApplyBounds(element, region, pages);

            if (isDecoration)
            {
                element.Metadata[ElementMetadataKeys.IsDecoration] = true;
            }

            // A heading the service identified is worth far more than one inferred from font size, and it is
            // what a later structure pass uses to find where an article begins.
            if (role == ParagraphRole.Title || role == ParagraphRole.SectionHeading)
            {
                element.Metadata[ElementMetadataKeys.SectionLabel] = paragraph.Content;
            }

            if (role == ParagraphRole.PageNumber)
            {
                element.Metadata[ElementMetadataKeys.Folio] = paragraph.Content;
            }

            entries.Add(new MappedElement(offset, pageNumber, element));
        }
    }

    /// <summary>
    /// Puts the mapped elements back into document order and groups them into one section per page.
    /// </summary>
    /// <param name="identifier">The document identifier.</param>
    /// <param name="entries">The mapped elements.</param>
    /// <param name="pages">The pages reported by the service.</param>
    /// <returns>The assembled document.</returns>
    private static IngestionDocument Assemble(
        string identifier,
        List<MappedElement> entries,
        Dictionary<int, DocumentPage> pages)
    {
        var document = new IngestionDocument(identifier);

        // The service reports content in reading order, and every element carries where it starts, so
        // ordering by that offset restores the order a person would read the page in.
        var ordered = entries
            .OrderBy(entry => entry.PageNumber)
            .ThenBy(entry => entry.Offset)
            .ToList();

        IngestionDocumentSection section = null;
        var currentPage = int.MinValue;

        foreach (var entry in ordered)
        {
            if (entry.PageNumber != currentPage)
            {
                section = new IngestionDocumentSection
                {
                    PageNumber = entry.PageNumber,
                };

                if (pages.TryGetValue(entry.PageNumber, out var page))
                {
                    var scale = GetScale(page);

                    if (page.Width.HasValue)
                    {
                        section.Metadata[ElementMetadataKeys.PageWidth] = page.Width.Value * scale;
                    }

                    if (page.Height.HasValue)
                    {
                        section.Metadata[ElementMetadataKeys.PageHeight] = page.Height.Value * scale;
                    }
                }

                document.Sections.Add(section);
                currentPage = entry.PageNumber;
            }

            section.Elements.Add(entry.Element);
        }

        return document;
    }

    private static Dictionary<int, DocumentPage> BuildPageLookup(AnalyzeResult result)
    {
        var pages = new Dictionary<int, DocumentPage>();

        if (result.Pages == null)
        {
            return pages;
        }

        foreach (var page in result.Pages)
        {
            pages[page.PageNumber] = page;
        }

        return pages;
    }

    /// <summary>
    /// Converts a reported region into the bounds convention the rest of the pipeline uses: points, with the
    /// origin at the bottom-left of the page. The service reports inches from the top-left.
    /// </summary>
    /// <param name="element">The element to annotate.</param>
    /// <param name="region">The reported region.</param>
    /// <param name="pages">The pages reported by the service.</param>
    private static void ApplyBounds(
        IngestionDocumentElement element,
        BoundingRegion? region,
        Dictionary<int, DocumentPage> pages)
    {
        if (region is not { } value || value.Polygon == null || value.Polygon.Count < 4)
        {
            return;
        }

        var polygon = value.Polygon;

        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;

        for (var index = 0; index + 1 < polygon.Count; index += 2)
        {
            minX = Math.Min(minX, polygon[index]);
            maxX = Math.Max(maxX, polygon[index]);
            minY = Math.Min(minY, polygon[index + 1]);
            maxY = Math.Max(maxY, polygon[index + 1]);
        }

        var scale = 1d;
        var pageHeight = 0d;

        if (pages.TryGetValue(value.PageNumber, out var page))
        {
            scale = GetScale(page);
            pageHeight = (page.Height ?? 0) * scale;
        }

        var left = minX * scale;
        var right = maxX * scale;
        var top = pageHeight - (minY * scale);
        var bottom = pageHeight - (maxY * scale);

        element.Metadata[ElementMetadataKeys.BoundingBox] = new[] { left, bottom, right, top };
    }

    private static BoundingRegion? GetFirstRegion(IReadOnlyList<BoundingRegion> regions)
    {
        return regions is { Count: > 0 } ? regions[0] : null;
    }

    private static double GetScale(DocumentPage page)
    {
        return page.Unit == LengthUnit.Inch ? PointsPerInch : 1d;
    }

    private static IngestionDocumentElement[,] BuildCells(DocumentTable table)
    {
        var rows = Math.Max(1, table.RowCount);
        var columns = Math.Max(1, table.ColumnCount);
        var cells = new IngestionDocumentElement[rows, columns];

        if (table.Cells != null)
        {
            foreach (var cell in table.Cells)
            {
                if (cell.RowIndex >= rows || cell.ColumnIndex >= columns)
                {
                    continue;
                }

                var content = cell.Content ?? string.Empty;

                cells[cell.RowIndex, cell.ColumnIndex] = new IngestionDocumentParagraph(content)
                {
                    Text = content,
                };
            }
        }

        return cells;
    }

    private static string RenderMarkdown(IngestionDocumentElement[,] cells)
    {
        var builder = new StringBuilder();

        for (var row = 0; row < cells.GetLength(0); row++)
        {
            if (row > 0)
            {
                builder.Append('\n');
            }

            for (var column = 0; column < cells.GetLength(1); column++)
            {
                if (column > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(cells[row, column]?.Text);
            }
        }

        return builder.ToString();
    }

    private static int GetOffset(IReadOnlyList<DocumentSpan> spans)
    {
        if (spans == null || spans.Count == 0)
        {
            return int.MaxValue;
        }

        return spans.Min(span => span.Offset);
    }

    private static void Suppress(List<Span> suppressed, IReadOnlyList<DocumentSpan> spans)
    {
        if (spans == null)
        {
            return;
        }

        foreach (var span in spans)
        {
            suppressed.Add(new Span(span.Offset, span.Offset + span.Length));
        }
    }

    private static bool IsSuppressed(List<Span> suppressed, int offset)
    {
        foreach (var span in suppressed)
        {
            if (offset >= span.Start && offset < span.End)
            {
                return true;
            }
        }

        return false;
    }

    private static int? ParseOrdinal(string caption)
    {
        var digits = new StringBuilder();

        foreach (var character in caption)
        {
            if (char.IsDigit(character))
            {
                digits.Append(character);

                continue;
            }

            if (digits.Length > 0)
            {
                break;
            }
        }

        return digits.Length > 0 &&
            int.TryParse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal)
            ? ordinal
            : null;
    }

    private readonly record struct Span(int Start, int End);

    private readonly record struct MappedElement(int Offset, int PageNumber, IngestionDocumentElement Element);
}
