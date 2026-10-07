using System.Globalization;
using CrestApps.Core.AI.Documents.Word.Workspace;

namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <summary>
/// Reads and writes page selections such as <c>1</c>, <c>2-4</c>, <c>1,3,5-7</c> or <c>last</c>.
/// </summary>
internal static class WordPageRange
{
    /// <summary>
    /// Reads a page selection.
    /// </summary>
    /// <param name="text">The selection.</param>
    /// <param name="pageCount">The number of pages.</param>
    /// <returns>The one-based page numbers, in order, without repeats.</returns>
    public static List<int> Parse(string text, int pageCount)
    {
        var pages = new List<int>();
        var seen = new HashSet<int>();

        if (string.IsNullOrWhiteSpace(text) || pageCount <= 0)
        {
            return pages;
        }

        foreach (var part in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-', 2, StringSplitOptions.TrimEntries);
            var from = ReadPage(range[0], pageCount);
            var to = range.Length == 2 ? ReadPage(string.IsNullOrEmpty(range[1]) ? "last" : range[1], pageCount) : from;

            if (from is null || to is null)
            {
                throw new WordToolException($"\"{part}\" is not a page or a range of pages. Use numbers from 1 to {pageCount}, such as \"2\", \"1-3\" or \"last\".");
            }

            // The selection comes from the model, so a range such as "1-2147483647" is clamped to the document
            // before it is walked: the loop then runs at most once per page.
            var first = Math.Clamp(Math.Min(from.Value, to.Value), 1, pageCount);
            var last = Math.Clamp(Math.Max(from.Value, to.Value), 1, pageCount);

            if (Math.Max(from.Value, to.Value) < 1 || Math.Min(from.Value, to.Value) > pageCount)
            {
                continue;
            }

            for (var page = first; page <= last; page++)
            {
                if (seen.Add(page))
                {
                    pages.Add(page);
                }
            }
        }

        return pages;
    }

    /// <summary>
    /// Writes a list of pages compactly, such as <c>2-4, 7</c>.
    /// </summary>
    /// <param name="pages">The pages.</param>
    /// <returns>The description.</returns>
    public static string Describe(IEnumerable<int> pages)
    {
        var sorted = pages.Distinct().Order().ToList();
        var parts = new List<string>();

        for (var index = 0; index < sorted.Count; index++)
        {
            var start = sorted[index];

            while (index + 1 < sorted.Count && sorted[index + 1] == sorted[index] + 1)
            {
                index++;
            }

            parts.Add(start == sorted[index]
                ? start.ToString(CultureInfo.InvariantCulture)
                : string.Create(CultureInfo.InvariantCulture, $"{start}-{sorted[index]}"));
        }

        return string.Join(", ", parts);
    }

    private static int? ReadPage(string text, int pageCount)
    {
        if (string.Equals(text, "last", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "end", StringComparison.OrdinalIgnoreCase))
        {
            return pageCount;
        }

        if (string.Equals(text, "first", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) ? page : null;
    }
}
