using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Reads the text of a page the way a person reads it: as words, lines and blocks in reading order.
/// </summary>
/// <remarks>
/// A page's raw text is its glyphs in the order they were drawn, and that order is not the reading order: a
/// generator that positions each word itself writes no spaces at all, and a two-column page is often drawn
/// across both columns. Every tool that reads a page's text goes through here so they all read the same
/// words.
/// </remarks>
internal static class PdfPageText
{
    // Reading order is decided by geometry alone. Drawing order is exactly what cannot be trusted here: a page
    // laid out in columns is routinely drawn across them.
    private static readonly UnsupervisedReadingOrderDetector _readingOrder = new(
        5,
        UnsupervisedReadingOrderDetector.SpatialReasoningRules.ColumnWise,
        useRenderingOrder: false);

    /// <summary>
    /// Returns the words on a page.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The words, in the order the extractor finds them.</returns>
    public static List<Word> GetWords(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        try
        {
            return [.. page.GetWords(NearestNeighbourWordExtractor.Instance)];
        }
        catch (Exception)
        {
            // The nearest-neighbour extractor can fail on pathological glyph geometry; the default one is
            // simpler and always produces something.
            return [.. page.GetWords()];
        }
    }

    /// <summary>
    /// Returns the text blocks of a page in reading order.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The blocks, ordered for reading.</returns>
    public static List<TextBlock> GetBlocks(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var words = GetWords(page).Where(word => !string.IsNullOrWhiteSpace(word.Text)).ToList();

        if (words.Count == 0)
        {
            return [];
        }

        try
        {
            var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);

            return [.. _readingOrder.Get(blocks)];
        }
        catch (Exception)
        {
            return [.. RecursiveXYCut.Instance.GetBlocks(words)];
        }
    }

    /// <summary>
    /// Returns a page's text in reading order, one paragraph per block.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The text.</returns>
    public static string GetText(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var blocks = GetBlocks(page);

        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var block in blocks)
        {
            foreach (var line in block.TextLines)
            {
                builder.AppendLine(line.Text);
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Returns a page's text as single-spaced words, the form a search matches against.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The words joined by spaces, lines by newlines.</returns>
    public static string GetPlainText(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var blocks = GetBlocks(page);

        return string.Join("\n", blocks.SelectMany(block => block.TextLines).Select(line => line.Text));
    }
}
