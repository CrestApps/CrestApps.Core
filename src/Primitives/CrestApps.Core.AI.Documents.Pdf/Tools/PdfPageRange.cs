using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Reads the page selections people write: <c>3</c>, <c>1-3,5</c>, <c>7-</c>, <c>-2</c>, <c>last</c>,
/// <c>odd</c>, <c>even</c>, <c>all</c>, <c>5-1</c> (backwards).
/// </summary>
internal static class PdfPageRange
{
    /// <summary>
    /// Reads a page selection.
    /// </summary>
    /// <param name="selection">The selection, or <see langword="null"/> for every page.</param>
    /// <param name="pageCount">The number of pages the document has.</param>
    /// <param name="keepOrderAndDuplicates">Whether the pages keep the order written and may repeat, as a reorder needs; otherwise they are sorted and distinct.</param>
    /// <returns>The one-based page numbers.</returns>
    public static List<int> Parse(string selection, int pageCount, bool keepOrderAndDuplicates = false)
    {
        if (pageCount <= 0)
        {
            throw new PdfToolException("The document has no pages.");
        }

        if (string.IsNullOrWhiteSpace(selection) || selection.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return [.. Enumerable.Range(1, pageCount)];
        }

        var pages = new List<int>();

        foreach (var raw in selection.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = raw.ToLowerInvariant()
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("pages", string.Empty, StringComparison.Ordinal)
                .Replace("page", string.Empty, StringComparison.Ordinal)
                .Replace('–', '-')
                .Replace("to", "-", StringComparison.Ordinal);

            switch (part)
            {
                case "all":
                    pages.AddRange(Enumerable.Range(1, pageCount));

                    continue;
                case "first":
                    pages.Add(1);

                    continue;
                case "last":
                    pages.Add(pageCount);

                    continue;
                case "odd":
                    pages.AddRange(Enumerable.Range(1, pageCount).Where(page => page % 2 == 1));

                    continue;
                case "even":
                    pages.AddRange(Enumerable.Range(1, pageCount).Where(page => page % 2 == 0));

                    continue;
            }

            var dash = part.IndexOf('-', 1);

            if (part.StartsWith('-') && dash < 0)
            {
                // "-3" is the first three pages.
                var count = ParseNumber(part[1..], raw, pageCount);
                pages.AddRange(Enumerable.Range(1, Math.Min(count, pageCount)));

                continue;
            }

            if (dash < 0)
            {
                pages.Add(Check(ParseNumber(part, raw, pageCount), raw, pageCount));

                continue;
            }

            var from = ParseNumber(part[..dash], raw, pageCount);
            var to = dash == part.Length - 1 ? pageCount : ParseNumber(part[(dash + 1)..], raw, pageCount);

            Check(from, raw, pageCount);
            Check(to, raw, pageCount);

            if (from <= to)
            {
                pages.AddRange(Enumerable.Range(from, to - from + 1));
            }
            else
            {
                for (var page = from; page >= to; page--)
                {
                    pages.Add(page);
                }
            }
        }

        if (pages.Count == 0)
        {
            throw new PdfToolException($"\"{selection}\" selects no pages. Use page numbers such as \"1-3,5\", or \"all\".");
        }

        return keepOrderAndDuplicates
            ? pages
            : [.. pages.Distinct().Order()];
    }

    /// <summary>
    /// Writes a set of pages compactly, for example <c>1-3, 5</c>.
    /// </summary>
    /// <param name="pages">The pages.</param>
    /// <returns>The compact form.</returns>
    public static string Describe(IEnumerable<int> pages)
    {
        var sorted = pages.Distinct().Order().ToList();
        var parts = new List<string>();
        var index = 0;

        while (index < sorted.Count)
        {
            var start = sorted[index];
            var end = start;

            while (index + 1 < sorted.Count && sorted[index + 1] == end + 1)
            {
                end = sorted[++index];
            }

            parts.Add(start == end
                ? start.ToString(CultureInfo.InvariantCulture)
                : string.Create(CultureInfo.InvariantCulture, $"{start}-{end}"));

            index++;
        }

        return string.Join(", ", parts);
    }

    private static int ParseNumber(string text, string raw, int pageCount)
    {
        if (text == "last")
        {
            return pageCount;
        }

        if (text.StartsWith("last-", StringComparison.Ordinal) &&
            int.TryParse(text["last-".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var back))
        {
            return pageCount - back;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            throw new PdfToolException($"\"{raw}\" is not a page number or range. Use numbers such as \"1-3,5\", or \"all\", \"last\", \"odd\", \"even\".");
        }

        return number;
    }

    private static int Check(int page, string raw, int pageCount)
    {
        if (page < 1 || page > pageCount)
        {
            throw new PdfToolException($"Page {page} (from \"{raw}\") does not exist; the document has {pageCount} page(s).");
        }

        return page;
    }
}
