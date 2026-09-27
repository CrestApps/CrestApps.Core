using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Checks the layout of a PDF across its pages: consistent page sizes and orientations, and consistent
/// margins.
/// </summary>
internal static class PdfLayoutChecks
{
    private const int MaxListed = 6;
    private const int MinLettersForMargins = 60;
    private const double MarginToleranceMillimetres = 10;

    /// <summary>
    /// Runs the layout checks.
    /// </summary>
    /// <param name="checks">The report to add to.</param>
    /// <param name="pages">The inspected pages.</param>
    /// <param name="sizesExplained">Whether mixed page sizes are what the document's definition asks for.</param>
    public static void Run(PdfCheckList checks, IReadOnlyList<PdfPageInspection> pages, bool sizesExplained)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(pages);

        CheckPageSizes(checks, pages, sizesExplained);
        CheckMargins(checks, pages);
    }

    /// <summary>
    /// Describes the size a page is shown at, taking its rotation into account.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>For example <c>A4 portrait</c>.</returns>
    public static string DescribeSize(PdfPageInspection page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var (width, height) = DisplaySize(page);

        return PdfPageSizes.Describe(width, height);
    }

    /// <summary>
    /// Gets the size a page is shown at, taking its rotation into account.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The width and height in points.</returns>
    public static (double Width, double Height) DisplaySize(PdfPageInspection page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var rotation = (((int)page.Page.Rotation.Value % 360) + 360) % 360;

        return rotation is 90 or 270
            ? (page.Visible.Height, page.Visible.Width)
            : (page.Visible.Width, page.Visible.Height);
    }

    private static void CheckPageSizes(PdfCheckList checks, IReadOnlyList<PdfPageInspection> pages, bool sizesExplained)
    {
        var groups = pages
            .GroupBy(DescribeSize, StringComparer.Ordinal)
            .OrderBy(group => group.Min(page => page.Number))
            .ToList();

        if (groups.Count <= 1)
        {
            var size = groups.Count == 1
                ? groups[0].Key
                : "no pages";

            checks.Pass("Page size", string.Create(CultureInfo.InvariantCulture, $"All {pages.Count} checked page(s) are {size}."));

            return;
        }

        var finding = "The pages mix sizes or orientations: " +
            string.Join("; ", groups.Select(group => $"{group.Key} on {PdfCheckList.Pages(group.Select(page => page.Number))}")) + ".";

        if (sizesExplained)
        {
            checks.Info("Page size", finding + " This matches the document's page setup.");

            return;
        }

        checks.Warn("Page size", finding + " A mix is right for a landscape table or a fold-out, but prints badly when it is accidental.", "Make the pages one size with edit_pdf_pages (operation 'crop' or re-create them), unless the mix is intended.");
    }

    private static void CheckMargins(PdfCheckList checks, IReadOnlyList<PdfPageInspection> pages)
    {
        var measured = new List<(PdfPageInspection Page, string Size, double Left, double Right)>();

        foreach (var page in pages)
        {
            if (page.VisibleLetters.Count < MinLettersForMargins)
            {
                continue;
            }

            var boxes = page.VisibleLetters
                .Where(letter => letter.TextOrientation == TextOrientation.Horizontal)
                .Select(PdfPageInspection.Box)
                .ToList();

            if (boxes.Count < MinLettersForMargins)
            {
                continue;
            }

            var left = boxes.Min(box => box.Left) - page.Visible.Left;
            var right = page.Visible.Right - boxes.Max(box => box.Right);

            measured.Add((page, DescribeSize(page), left, right));
        }

        if (measured.Count < 3)
        {
            checks.Info("Margins", "Too few pages carry enough text to compare their margins.");

            return;
        }

        var tolerance = PdfPageSizes.FromMillimetres(MarginToleranceMillimetres);
        var findings = new List<string>();
        var flagged = new List<int>();

        foreach (var group in measured.GroupBy(entry => entry.Size, StringComparer.Ordinal))
        {
            var entries = group.ToList();

            if (entries.Count < 3)
            {
                continue;
            }

            var medianLeft = Median(entries.Select(entry => entry.Left));
            var medianRight = Median(entries.Select(entry => entry.Right));

            foreach (var entry in entries)
            {
                // A page with less text than usual has a wider right margin by nature; only a narrower one
                // means the text runs further out than on the other pages.
                var leftOff = Math.Abs(entry.Left - medianLeft) > tolerance;
                var rightOff = medianRight - entry.Right > tolerance;

                if (!leftOff && !rightOff)
                {
                    continue;
                }

                flagged.Add(entry.Page.Number);

                var parts = new List<string>();

                if (leftOff)
                {
                    parts.Add($"left margin {PdfCheckList.Millimetres(entry.Left)} where most pages have {PdfCheckList.Millimetres(medianLeft)}");
                }

                if (rightOff)
                {
                    parts.Add($"text reaches {PdfCheckList.Millimetres(Math.Max(0, entry.Right))} from the right edge where most pages keep {PdfCheckList.Millimetres(medianRight)}");
                }

                findings.Add(string.Create(CultureInfo.InvariantCulture, $"page {entry.Page.Number}: {string.Join(", ", parts)}"));
            }
        }

        if (flagged.Count == 0)
        {
            checks.Pass("Margins", string.Create(CultureInfo.InvariantCulture, $"The text keeps consistent left and right margins on the {measured.Count} pages with enough text to compare."));

            return;
        }

        checks.Warn(
            "Margins",
            "Some pages place their text differently from the rest: " + PdfCheckList.List(findings, MaxListed) + ".",
            "Look at those pages with preview_pdf; a table or picture wider than the text, or a page from another document, is the usual cause.");
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();

        if (sorted.Count == 0)
        {
            return 0;
        }

        var middle = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
