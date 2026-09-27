using System.Globalization;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Checks the fonts of a PDF: whether they are embedded, whether their text maps back to Unicode, and
/// whether any are Type 3 fonts.
/// </summary>
internal static class PdfFontChecks
{
    private const int MaxListed = 6;
    private const int MaxInventory = 25;

    /// <summary>
    /// Runs the font checks.
    /// </summary>
    /// <param name="checks">The report to add to.</param>
    /// <param name="objects">The document, opened with PDFsharp.</param>
    /// <param name="pages">The one-based pages checked.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static void Run(PdfCheckList checks, PdfDocument objects, IReadOnlyList<int> pages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(pages);

        var fonts = PdfFontInventory.Collect(objects, pages);
        var scan = PdfContentScanner.Scan(objects, pages, cancellationToken);

        if (fonts.Count == 0)
        {
            checks.Info("Fonts", "The checked pages use no fonts.");

            return;
        }

        // A file often repeats one font in a dictionary per page; they are reported together.
        string Used(IEnumerable<PdfFontInfo> group)
        {
            var members = group.ToList();
            var used = members
                .Where(font => scan.FontPages.ContainsKey(font.Dictionary))
                .SelectMany(font => scan.FontPages[font.Dictionary])
                .ToList();

            return used.Count > 0
                ? PdfCheckList.Pages(used)
                : PdfCheckList.Pages(members.SelectMany(font => font.Pages)) + " (listed, not seen in use)";
        }

        IEnumerable<string> Describe(IEnumerable<PdfFontInfo> selection)
        {
            return selection
                .GroupBy(font => font.Describe(), StringComparer.Ordinal)
                .Select(group => $"{group.Key} on {Used(group)}");
        }

        var missing = fonts.Where(font => !font.IsEmbedded && !font.IsStandard14).ToList();
        var standard = fonts.Where(font => !font.IsEmbedded && font.IsStandard14).ToList();
        var type3 = fonts.Where(font => string.Equals(font.Subtype, "Type3", StringComparison.Ordinal)).ToList();
        var unmapped = fonts.Where(font => !font.HasUnicodeMapping && scan.TextFontPages.ContainsKey(font.Dictionary)).ToList();

        if (missing.Count > 0)
        {
            checks.Fail(
                "Font embedding",
                "Fonts are not embedded, so each viewer substitutes a font of its own and the text may change look, width and line breaks: " +
                    PdfCheckList.List(Describe(missing), MaxListed) + ".",
                "Re-export the document with fonts embedded; a composed document always embeds its fonts.");
        }
        else if (standard.Count > 0)
        {
            checks.Warn(
                "Font embedding",
                "Standard fonts are used without being embedded; viewers draw them from their own copies, which is fine for reading but not allowed by PDF/A and PDF/UA: " +
                    PdfCheckList.List(Describe(standard), MaxListed) + ".",
                "If the file must meet PDF/A or PDF/UA, re-export it with all fonts embedded.");
        }
        else
        {
            checks.Pass("Font embedding", string.Create(CultureInfo.InvariantCulture, $"All {fonts.GroupBy(font => font.Describe(), StringComparer.Ordinal).Count()} font(s) are embedded."));
        }

        if (unmapped.Count > 0)
        {
            checks.Warn(
                "Unicode mapping",
                "Text is drawn in fonts that do not map back to Unicode, so copying, searching and screen readers may get garbage: " +
                    PdfCheckList.List(unmapped.GroupBy(font => font.Describe(), StringComparer.Ordinal).Select(group => $"{group.Key} on {PdfCheckList.Pages(group.SelectMany(font => scan.TextFontPages[font.Dictionary]))}"), MaxListed) + ".",
                "Check the text with extract_pdf_text; if it is garbled, recognise it with ocr_pdf or rebuild the file from its source.");
        }
        else
        {
            checks.Pass("Unicode mapping", "Every font that draws text maps its glyphs back to Unicode.");
        }

        if (type3.Count > 0)
        {
            checks.Warn(
                "Type 3 fonts",
                "Type 3 fonts draw their glyphs as graphics; they often look rough when zoomed or printed and extract poorly: " +
                    PdfCheckList.List(Describe(type3), MaxListed) + ".",
                "Re-export the document with outline (TrueType or OpenType) fonts.");
        }

        var groups = fonts.GroupBy(font => (Description: font.Describe(), font.Encoding)).ToList();
        var inventory = groups
            .Take(MaxInventory)
            .Select(group => $"{group.Key.Description}, {group.Key.Encoding} encoding, {Used(group)}");

        var more = groups.Count > MaxInventory
            ? string.Create(CultureInfo.InvariantCulture, $" and {groups.Count - MaxInventory} more")
            : string.Empty;

        checks.Info("Font list", string.Create(CultureInfo.InvariantCulture, $"{groups.Count} font(s): ") + string.Join("; ", inventory) + more + ".");
    }
}
