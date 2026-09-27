using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Pdf.Services;
using CrestApps.Core.Ingestion;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// What the document ingestion reader finds on a PDF's pages that page text alone does not give away: the
/// tables, whether ruled or laid out with whitespace, and the drawings made of vector paths.
/// </summary>
/// <remarks>
/// The reader is the one uploads are indexed with, so a table the tools report is the table search
/// retrieval sees. It is resolved from the host, which may register its own, and falls back to the built-in
/// PDF reader.
/// </remarks>
internal sealed class PdfIngestedContent
{
    private const string PdfMediaType = "application/pdf";

    private readonly Dictionary<int, List<PdfRegion>> _tables = [];
    private readonly Dictionary<int, List<PdfRegion>> _drawings = [];

    /// <summary>
    /// Gets why the reader could not read the document, when it could not.
    /// </summary>
    public string Warning { get; private set; }

    /// <summary>
    /// Gets every table found, in page order.
    /// </summary>
    public List<PdfRegion> AllTables => [.. _tables.OrderBy(entry => entry.Key).SelectMany(entry => entry.Value)];

    /// <summary>
    /// Reads a PDF with the ingestion reader.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="readableBytes">The file, readable without a password.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="pages">The one-based pages of interest.</param>
    /// <param name="pageCount">The number of pages the file has.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tables and drawings found on those pages.</returns>
    public static async Task<PdfIngestedContent> ReadAsync(
        IServiceProvider services,
        byte[] readableBytes,
        string fileName,
        IReadOnlyList<int> pages,
        int pageCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readableBytes);
        ArgumentNullException.ThrowIfNull(pages);

        var content = new PdfIngestedContent();
        var wanted = pages.ToHashSet();
        var (bytes, pageMap) = PdfReadableCopy.Subset(readableBytes, pages, pageCount);
        var reader = services?.GetKeyedService<IngestionDocumentReader>(".pdf") ?? new PdfIngestionDocumentReader();

        IngestionDocument document;

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            document = await reader.ReadAsync(stream, string.IsNullOrWhiteSpace(fileName) ? "document.pdf" : fileName, PdfMediaType, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            content.Warning = "The document reader could not detect tables and drawings: " + ex.Message;

            return content;
        }

        foreach (var element in document?.EnumerateContent() ?? [])
        {
            if (element?.PageNumber is not int readPage)
            {
                continue;
            }

            var page = MapPage(pageMap, readPage);

            if (!wanted.Contains(page))
            {
                continue;
            }

            switch (element)
            {
                case IngestionDocumentTable table:
                    {
                        var rows = ReadRows(table);

                        if (rows.Count > 0)
                        {
                            Add(content._tables, page, new PdfRegion
                            {
                                Kind = PdfRegion.TableKind,
                                Page = page,
                                Box = ReadBox(element),
                                Rows = rows,
                            });
                        }

                        break;
                    }

                case IngestionDocumentImage image when IsVectorFigure(image):
                    Add(content._drawings, page, new PdfRegion
                    {
                        Kind = PdfRegion.DrawingKind,
                        Page = page,
                        Box = ReadBox(element),
                        Content = image.Content?.ToArray(),
                        MediaType = image.MediaType,
                    });

                    break;
            }
        }

        return content;
    }

    /// <summary>
    /// Gets the tables found on a page, top to bottom.
    /// </summary>
    /// <param name="page">The one-based page number.</param>
    /// <returns>The tables.</returns>
    public List<PdfRegion> TablesOn(int page)
    {
        return _tables.TryGetValue(page, out var tables)
            ? tables
            : [];
    }

    /// <summary>
    /// Gets the drawings found on a page.
    /// </summary>
    /// <param name="page">The one-based page number.</param>
    /// <returns>The drawings.</returns>
    public List<PdfRegion> DrawingsOn(int page)
    {
        return _drawings.TryGetValue(page, out var drawings)
            ? drawings
            : [];
    }

    private static int MapPage(List<int> pageMap, int readPage)
    {
        if (pageMap is null)
        {
            return readPage;
        }

        return readPage >= 1 && readPage <= pageMap.Count
            ? pageMap[readPage - 1]
            : 0;
    }

    private static void Add(Dictionary<int, List<PdfRegion>> regions, int page, PdfRegion region)
    {
        if (!regions.TryGetValue(page, out var list))
        {
            list = [];
            regions[page] = list;
        }

        list.Add(region);

        // Top to bottom, so "the first table on the next page" is the one a reader sees first.
        list.Sort((first, second) => (second.Box?.Top ?? 0).CompareTo(first.Box?.Top ?? 0));
    }

    private static List<List<string>> ReadRows(IngestionDocumentTable table)
    {
        var rows = new List<List<string>>();
        var cells = table.Cells;

        if (cells is null || cells.Length == 0)
        {
            return rows;
        }

        for (var row = 0; row < cells.GetLength(0); row++)
        {
            var values = new List<string>(cells.GetLength(1));

            for (var column = 0; column < cells.GetLength(1); column++)
            {
                values.Add(PdfTextPatterns.OneLine(cells[row, column]?.Text ?? string.Empty));
            }

            // A row with nothing in it carries no information and only pads the table.
            if (values.Exists(value => value.Length > 0))
            {
                rows.Add(values);
            }
        }

        return rows;
    }

    private static PdfBox? ReadBox(IngestionDocumentElement element)
    {
        if (element.HasMetadata &&
            element.Metadata.TryGetValue(ElementMetadataKeys.BoundingBox, out var value) &&
            PdfBoxes.TryRead(value, out var box))
        {
            return box;
        }

        return null;
    }

    private static bool IsVectorFigure(IngestionDocumentImage image)
    {
        return image.HasMetadata &&
            image.Metadata.TryGetValue(FigureMetadataKeys.IsVectorFigure, out var value) &&
            value is true;
    }
}
