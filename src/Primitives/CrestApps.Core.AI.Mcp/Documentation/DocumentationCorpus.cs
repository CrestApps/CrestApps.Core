using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// An in-memory, keyword-searchable collection of documentation entries. Sources that materialize a
/// full corpus (for example a crawled site or a downloaded search index) build a
/// <see cref="DocumentationCorpus"/> once and reuse it to answer queries with lightweight keyword
/// scoring, keeping the ranking behavior consistent across sources.
/// </summary>
public sealed partial class DocumentationCorpus
{
    private readonly IReadOnlyList<Entry> _entries;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentationCorpus"/> class.
    /// </summary>
    /// <param name="entries">The documentation entries that make up the corpus.</param>
    public DocumentationCorpus(IReadOnlyList<Entry> entries)
    {
        _entries = entries ?? [];
    }

    /// <summary>
    /// Gets the number of entries in the corpus.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Searches the corpus for entries relevant to the supplied query.
    /// </summary>
    /// <param name="query">The free-text query.</param>
    /// <param name="sourceName">The logical name of the source that owns this corpus.</param>
    /// <param name="maxResults">The maximum number of results to return.</param>
    /// <returns>The relevant entries ordered by descending relevance.</returns>
    public IReadOnlyList<DocumentationSearchResult> Search(string query, string sourceName, int maxResults)
    {
        if (_entries.Count == 0 || string.IsNullOrWhiteSpace(query) || maxResults <= 0)
        {
            return [];
        }

        var terms = Tokenize(query);

        if (terms.Length == 0)
        {
            return [];
        }

        var scored = new List<DocumentationSearchResult>();

        foreach (var entry in _entries)
        {
            var score = Score(entry, terms);

            if (score <= 0)
            {
                continue;
            }

            scored.Add(new DocumentationSearchResult
            {
                SourceName = sourceName,
                Title = string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title,
                Url = entry.Url,
                Snippet = BuildSnippet(entry.Text, terms),
                Score = score,
            });
        }

        return scored
            .OrderByDescending(result => result.Score)
            .Take(maxResults)
            .ToList();
    }

    private static double Score(Entry entry, string[] terms)
    {
        double score = 0;
        var matchedTerms = 0;

        foreach (var term in terms)
        {
            var bodyCount = CountOccurrences(entry.Text, term);
            var titleCount = CountOccurrences(entry.Title, term);

            if (bodyCount == 0 && titleCount == 0)
            {
                continue;
            }

            matchedTerms++;
            score += bodyCount + (titleCount * 5);
        }

        if (matchedTerms == 0)
        {
            return 0;
        }

        return score * matchedTerms;
    }

    private static int CountOccurrences(string text, string term)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += term.Length;
        }

        return count;
    }

    private static string BuildSnippet(string text, string[] terms)
    {
        const int windowSize = 240;

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var matchIndex = -1;

        foreach (var term in terms)
        {
            var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);

            if (index >= 0 && (matchIndex < 0 || index < matchIndex))
            {
                matchIndex = index;
            }
        }

        if (matchIndex < 0)
        {
            return text.Length <= windowSize ? text : text[..windowSize] + "…";
        }

        var start = Math.Max(0, matchIndex - (windowSize / 2));
        var length = Math.Min(windowSize, text.Length - start);
        var snippet = text.Substring(start, length).Trim();

        if (start > 0)
        {
            snippet = "…" + snippet;
        }

        if (start + length < text.Length)
        {
            snippet += "…";
        }

        return snippet;
    }

    private static string[] Tokenize(string query)
    {
        return NonWordRegex()
            .Split(query.ToLowerInvariant())
            .Where(token => token.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWordRegex();

    /// <summary>
    /// Represents a single documentation entry that can be searched.
    /// </summary>
    /// <param name="Url">The canonical URL of the entry.</param>
    /// <param name="Title">The title of the entry.</param>
    /// <param name="Text">The plain-text body of the entry.</param>
    public sealed record Entry(string Url, string Title, string Text);
}
