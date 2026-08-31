using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

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

        var phrases = ExtractPhrases(query);
        var scored = new List<DocumentationSearchResult>();

        foreach (var entry in _entries)
        {
            var score = Score(entry, terms, phrases);

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

    private static double Score(Entry entry, string[] terms, string[] phrases)
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

        // Precision bonus: reward pages where the query's consecutive keywords appear together as an exact
        // phrase (for example "box office"), not merely scattered across the page. This lifts a page that
        // actually discusses the phrase above one that happens to mention the individual words in unrelated
        // places, without changing which pages match (a page matching a phrase always matches its terms).
        double phraseBonus = 0;

        foreach (var phrase in phrases)
        {
            phraseBonus += (CountOccurrences(entry.Text, phrase) * 5) + (CountOccurrences(entry.Title, phrase) * 15);
        }

        return (score + phraseBonus) * matchedTerms;
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
        var tokens = NonWordRegex()
            .Split(query.ToLowerInvariant())
            .Where(token => token.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var meaningful = tokens.Where(token => !_stopWords.Contains(token)).ToArray();

        // When the model passes a full sentence, common function words ("what", "the", "is") would
        // otherwise match nearly every page and drown out the relevant terms. Drop them, but fall back to
        // the raw tokens when a query is nothing but stop words so the search still runs.
        return meaningful.Length > 0 ? meaningful : tokens;
    }

    /// <summary>
    /// Builds the set of adjacent keyword pairs (bigrams) from the query, in the order the caller supplied
    /// them, skipping stop words. These are used to award a phrase/adjacency bonus during scoring so that a
    /// page containing the exact phrase ranks above one that merely scatters the same keywords.
    /// </summary>
    private static string[] ExtractPhrases(string query)
    {
        var ordered = NonWordRegex()
            .Split(query.ToLowerInvariant())
            .Where(token => token.Length >= 2 && !_stopWords.Contains(token))
            .ToArray();

        if (ordered.Length < 2)
        {
            return [];
        }

        var phrases = new List<string>(ordered.Length - 1);

        for (var i = 0; i < ordered.Length - 1; i++)
        {
            phrases.Add(ordered[i] + " " + ordered[i + 1]);
        }

        return phrases.Distinct(StringComparer.Ordinal).ToArray();
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWordRegex();

    /// <summary>
    /// A small set of English function words that carry no discriminating signal for keyword search. They
    /// are removed from a query so a verbose, sentence-style question still ranks on its meaningful terms.
    /// </summary>
    private static readonly HashSet<string> _stopWords = new(StringComparer.Ordinal)
    {
        "the", "an", "and", "or", "but", "of", "to", "in", "on", "for", "with", "is", "are", "was", "were",
        "be", "been", "being", "what", "which", "who", "whom", "how", "when", "where", "why", "do", "does",
        "did", "can", "could", "would", "should", "will", "this", "that", "these", "those", "it", "its",
        "as", "at", "by", "from", "about", "into", "over", "than", "then", "there", "here", "you", "your",
        "we", "our", "they", "their", "my", "me", "us", "if", "so", "not", "any", "all", "please", "tell",
    };

    /// <summary>
    /// Represents a single documentation entry that can be searched.
    /// </summary>
    /// <param name="Url">The canonical URL of the entry.</param>
    /// <param name="Title">The title of the entry.</param>
    /// <param name="Text">The plain-text body of the entry.</param>
    public sealed record Entry(string Url, string Title, string Text);
}
