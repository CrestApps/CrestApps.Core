using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Describes the pages a reading tool looked at, the same way in every answer.
/// </summary>
internal static class PdfPageSelection
{
    /// <summary>
    /// Describes a selection of pages.
    /// </summary>
    /// <param name="pages">The one-based pages.</param>
    /// <param name="pageCount">The number of pages the document has.</param>
    /// <returns>For example <c>all 12 page(s)</c>, <c>page 3</c> or <c>pages 1-3, 5</c>.</returns>
    public static string Describe(IReadOnlyCollection<int> pages, int pageCount)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (pages.Count == pageCount)
        {
            return "all " + pageCount.ToString(CultureInfo.InvariantCulture) + " page(s)";
        }

        return pages.Count == 1
            ? "page " + pages.First().ToString(CultureInfo.InvariantCulture)
            : "pages " + PdfPageRange.Describe(pages);
    }
}
