using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// A table read out of a PDF, possibly continued across several pages.
/// </summary>
internal sealed class PdfExtractedTable
{
    /// <summary>
    /// Gets the one-based pages the table is on.
    /// </summary>
    public List<int> Pages { get; } = [];

    /// <summary>
    /// Gets the cells, row by row; the first row is taken as the header.
    /// </summary>
    public List<List<string>> Rows { get; } = [];

    /// <summary>
    /// Gets or sets where the table starts, on its first page, in user space.
    /// </summary>
    public PdfBox? Box { get; set; }

    /// <summary>
    /// Gets the number of columns.
    /// </summary>
    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(row => row.Count);

    /// <summary>
    /// Gets the header row.
    /// </summary>
    public List<string> Header => Rows.Count == 0 ? [] : Rows[0];

    /// <summary>
    /// Builds tables from the regions a reader found, joining a table that runs on from one page to the next.
    /// </summary>
    /// <param name="regions">The tables, in page order and top to bottom within a page.</param>
    /// <param name="mergeAcrossPages">Whether a table continued on the next page is joined to the part before it.</param>
    /// <returns>The tables.</returns>
    /// <remarks>
    /// A table is taken to continue when it is the first table on the page after the last table of the page
    /// before, with the same number of columns. A header row the continuation repeats is dropped.
    /// </remarks>
    public static List<PdfExtractedTable> Build(IReadOnlyList<PdfRegion> regions, bool mergeAcrossPages)
    {
        ArgumentNullException.ThrowIfNull(regions);

        var tables = new List<PdfExtractedTable>();

        for (var index = 0; index < regions.Count; index++)
        {
            var region = regions[index];

            if (region.Rows is not { Count: > 0 })
            {
                continue;
            }

            var previous = tables.Count > 0
                ? tables[^1]
                : null;

            if (mergeAcrossPages &&
                previous is not null &&
                IsLastOnItsPage(regions, index - 1) &&
                IsFirstOnItsPage(regions, index) &&
                region.Page == previous.Pages[^1] + 1 &&
                region.ColumnCount == previous.ColumnCount)
            {
                var rows = region.Rows;
                var start = RowsEqual(rows[0], previous.Header)
                    ? 1
                    : 0;

                for (var row = start; row < rows.Count; row++)
                {
                    previous.Rows.Add([.. rows[row]]);
                }

                previous.Pages.Add(region.Page);

                continue;
            }

            var table = new PdfExtractedTable { Box = region.Box };

            table.Pages.Add(region.Page);

            foreach (var row in region.Rows)
            {
                table.Rows.Add([.. row]);
            }

            tables.Add(table);
        }

        return tables;
    }

    /// <summary>
    /// Describes the pages the table is on, for example <c>page 3</c> or <c>pages 3-4</c>.
    /// </summary>
    /// <returns>The description.</returns>
    public string DescribePages()
    {
        return Pages.Count == 1
            ? "page " + Pages[0].ToString(CultureInfo.InvariantCulture)
            : "pages " + PdfPageRange.Describe(Pages);
    }

    private static bool IsLastOnItsPage(IReadOnlyList<PdfRegion> regions, int index)
    {
        return index >= 0 && (index + 1 >= regions.Count || regions[index + 1].Page != regions[index].Page);
    }

    private static bool IsFirstOnItsPage(IReadOnlyList<PdfRegion> regions, int index)
    {
        return index == 0 || regions[index - 1].Page != regions[index].Page;
    }

    private static bool RowsEqual(List<string> first, List<string> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            if (!string.Equals(PdfTextPatterns.OneLine(first[index]), PdfTextPatterns.OneLine(second[index]), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
