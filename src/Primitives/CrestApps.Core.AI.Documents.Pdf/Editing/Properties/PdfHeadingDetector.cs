using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using UglyToad.PdfPig.Content;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Finds the headings of a PDF from how its text is drawn, to build bookmarks for a file that has none.
/// </summary>
/// <remarks>
/// The body text size is the size most characters are drawn at. A line whose dominant size is clearly
/// larger is a heading candidate; lines repeated at the same place on many pages (running heads and
/// footers) and lines that are only a number are not. Headings are ranked into levels by their size — the
/// largest tier first — and a largest tier used only once near the start is taken as the document's title
/// rather than a section of it.
/// </remarks>
internal static partial class PdfHeadingDetector
{
    private const int MaxHeadingLength = 150;

    /// <summary>
    /// Finds the headings of a document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="maxDepth">The deepest heading level kept.</param>
    /// <param name="maxHeadings">The most headings returned.</param>
    /// <param name="title">The line taken as the document's title, when one was.</param>
    /// <returns>The headings as a bookmark tree.</returns>
    public static List<PdfBookmarkNode> Detect(PigDocument document, int maxDepth, int maxHeadings, out string title)
    {
        ArgumentNullException.ThrowIfNull(document);

        title = null;

        var lines = new List<LineInfo>();
        var sizes = new Dictionary<double, int>();

        foreach (var page in document.GetPages())
        {
            ReadLines(page, lines, sizes);
        }

        if (sizes.Count == 0)
        {
            return [];
        }

        var body = sizes.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).First().Key;
        var running = FindRunningText(lines, document.NumberOfPages);
        var candidates = lines
            .Where(line => line.Size >= body * 1.15 && line.Size >= body + 1 && IsHeadingText(line.Text) && !running.Contains(Normalize(line.Text)))
            .ToList();

        var headings = Merge(candidates);

        if (headings.Count == 0)
        {
            return [];
        }

        var tiers = new List<double>();

        foreach (var size in headings.Select(heading => heading.Size).Distinct().OrderDescending())
        {
            if (tiers.Count == 0 || size < tiers[^1] - 0.75)
            {
                tiers.Add(size);
            }
        }

        int TierOf(double size)
        {
            // Tiers run from the largest size down; a size belongs to the first tier it reaches.
            for (var index = 0; index < tiers.Count; index++)
            {
                if (size >= tiers[index] - 0.75)
                {
                    return index;
                }
            }

            return tiers.Count - 1;
        }

        var firstTier = 0;
        var topTier = headings.Where(heading => TierOf(heading.Size) == 0).ToList();

        if (tiers.Count >= 2 && topTier.Count == 1 && topTier[0].Page <= 2)
        {
            title = topTier[0].Text;
            headings.Remove(topTier[0]);
            firstTier = 1;
        }

        var roots = new List<PdfBookmarkNode>();
        var stack = new List<(int Level, PdfBookmarkNode Node)>();
        var count = 0;

        foreach (var heading in headings)
        {
            var level = TierOf(heading.Size) - firstTier + 1;

            if (level < 1 || level > maxDepth)
            {
                continue;
            }

            if (count >= maxHeadings)
            {
                break;
            }

            var node = new PdfBookmarkNode
            {
                Title = heading.Text,
                Page = heading.Page,
                Top = heading.Top + (heading.Size * 0.3),
                Open = level == 1,
            };

            while (stack.Count > 0 && stack[^1].Level >= level)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            if (stack.Count == 0)
            {
                roots.Add(node);
            }
            else
            {
                stack[^1].Node.Children.Add(node);
            }

            stack.Add((level, node));
            count++;
        }

        return roots;
    }

    private static void ReadLines(Page page, List<LineInfo> lines, Dictionary<double, int> sizes)
    {
        var visible = PdfBox.VisibleArea(page);
        var blockNumber = 0;

        foreach (var block in PdfPageText.GetBlocks(page))
        {
            blockNumber++;

            foreach (var line in block.TextLines)
            {
                var letters = line.Words
                    .SelectMany(word => word.Letters)
                    .Where(letter => !string.IsNullOrWhiteSpace(letter.Value))
                    .ToList();

                if (letters.Count == 0)
                {
                    continue;
                }

                var bySize = new Dictionary<double, int>();

                foreach (var letter in letters)
                {
                    var size = Math.Round(letter.PointSize * 2, MidpointRounding.AwayFromZero) / 2;

                    bySize[size] = bySize.GetValueOrDefault(size) + 1;
                    sizes[size] = sizes.GetValueOrDefault(size) + 1;
                }

                var dominant = bySize.OrderByDescending(entry => entry.Value).ThenByDescending(entry => entry.Key).First().Key;
                var box = PdfBox.From(line.BoundingBox);

                lines.Add(new LineInfo
                {
                    Page = page.Number,
                    Block = blockNumber,
                    Text = CollapseSpaces(line.Text),
                    Size = dominant,
                    Top = box.Top,
                    Bottom = box.Bottom,
                    InMargin = box.Top > visible.Top - (visible.Height * 0.1) || box.Bottom < visible.Bottom + (visible.Height * 0.1),
                });
            }
        }
    }

    private static HashSet<string> FindRunningText(List<LineInfo> lines, int pageCount)
    {
        var running = new HashSet<string>(StringComparer.Ordinal);

        if (pageCount < 2)
        {
            return running;
        }

        foreach (var group in lines.GroupBy(line => Normalize(line.Text), StringComparer.Ordinal))
        {
            var pages = group.Select(line => line.Page).Distinct().Count();
            var inMargin = group.All(line => line.InMargin);

            // Text on most pages is a running head wherever it sits; text on a few pages is one only when it
            // sits in the top or bottom margin every time.
            if ((pages >= 3 && pages * 2 >= pageCount) || (pages >= 2 && inMargin))
            {
                running.Add(group.Key);
            }
        }

        return running;
    }

    private static List<LineInfo> Merge(List<LineInfo> candidates)
    {
        var headings = new List<LineInfo>();

        foreach (var line in candidates)
        {
            var previous = headings.Count > 0 ? headings[^1] : null;

            // A heading that wraps is two lines of the same size in the same block, one right under the other.
            if (previous is not null &&
                previous.Page == line.Page &&
                previous.Block == line.Block &&
                Math.Abs(previous.Size - line.Size) < 0.3 &&
                previous.Bottom - line.Top < line.Size * 1.2 &&
                previous.Text.Length + line.Text.Length < MaxHeadingLength)
            {
                previous.Text += " " + line.Text;
                previous.Bottom = line.Bottom;

                continue;
            }

            headings.Add(new LineInfo
            {
                Page = line.Page,
                Block = line.Block,
                Text = line.Text,
                Size = line.Size,
                Top = line.Top,
                Bottom = line.Bottom,
                InMargin = line.InMargin,
            });
        }

        return headings;
    }

    private static bool IsHeadingText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxHeadingLength || !text.Any(char.IsLetter))
        {
            return false;
        }

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 20 && !PageNumberPattern().IsMatch(text);
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var character in text.ToLower(CultureInfo.InvariantCulture))
        {
            builder.Append(char.IsDigit(character) ? '#' : character);
        }

        return CollapseSpaces(builder.ToString());
    }

    private static string CollapseSpaces(string text)
    {
        return WhitespacePattern().Replace(text ?? string.Empty, " ").Trim();
    }

    [GeneratedRegex(@"^\s*(page\s*)?\d+(\s*(of|/)\s*\d+)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PageNumberPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    private sealed class LineInfo
    {
        public int Page { get; init; }

        public int Block { get; init; }

        public string Text { get; set; }

        public double Size { get; init; }

        public double Top { get; init; }

        public double Bottom { get; set; }

        public bool InMargin { get; init; }
    }
}
