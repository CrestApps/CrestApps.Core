using System.Text;

namespace CrestApps.Core.AI.Documents.Pdf.Intelligence;

/// <summary>
/// Ranks passages against a question with Okapi BM25: a term counts for more the rarer it is across the
/// passages, and for less the longer the passage it appears in.
/// </summary>
/// <remarks>
/// Built for the handful of documents one conversation holds, entirely in memory. Terms are lower-cased
/// words and numbers with common English stop words removed and a light plural folding, so "invoices" finds
/// "invoice".
/// </remarks>
internal sealed class PdfBm25Index
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    private static readonly HashSet<string> _stopWords = new(StringComparer.Ordinal)
    {
        "a", "about", "above", "after", "again", "against", "all", "am", "an", "and", "any", "are", "as", "at",
        "be", "because", "been", "before", "being", "below", "between", "both", "but", "by", "can", "could",
        "did", "do", "does", "doing", "down", "during", "each", "few", "for", "from", "further", "had", "has",
        "have", "having", "he", "her", "here", "hers", "herself", "him", "himself", "his", "how", "i", "if",
        "in", "into", "is", "it", "its", "itself", "just", "me", "more", "most", "my", "myself", "no", "nor",
        "not", "now", "of", "off", "on", "once", "only", "or", "other", "our", "ours", "ourselves", "out",
        "over", "own", "same", "she", "should", "so", "some", "such", "than", "that", "the", "their", "theirs",
        "them", "themselves", "then", "there", "these", "they", "this", "those", "through", "to", "too",
        "under", "until", "up", "very", "was", "we", "were", "what", "when", "where", "which", "while", "who",
        "whom", "why", "will", "with", "would", "you", "your", "yours", "yourself", "yourselves", "also",
        "shall", "may", "might", "must", "per", "via", "etc", "tell", "please", "document", "pdf", "page",
    };

    private readonly List<Dictionary<string, int>> _termFrequencies = [];
    private readonly List<int> _lengths = [];
    private readonly Dictionary<string, int> _documentFrequencies = new(StringComparer.Ordinal);
    private readonly double _averageLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfBm25Index"/> class.
    /// </summary>
    /// <param name="passages">The passages to rank, in the order their indexes refer to.</param>
    public PdfBm25Index(IEnumerable<string> passages)
    {
        ArgumentNullException.ThrowIfNull(passages);

        foreach (var passage in passages)
        {
            var tokens = Tokenize(passage);
            var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var token in tokens)
            {
                frequencies[token] = frequencies.GetValueOrDefault(token) + 1;
            }

            foreach (var term in frequencies.Keys)
            {
                _documentFrequencies[term] = _documentFrequencies.GetValueOrDefault(term) + 1;
            }

            _termFrequencies.Add(frequencies);
            _lengths.Add(tokens.Count);
        }

        _averageLength = _lengths.Count == 0
            ? 0
            : _lengths.Average();
    }

    /// <summary>
    /// Gets the number of passages.
    /// </summary>
    public int Count => _lengths.Count;

    /// <summary>
    /// Ranks the passages against a query.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="top">The most passages returned.</param>
    /// <returns>The passages that share at least one term with the query, best first.</returns>
    public List<PdfBm25Hit> Search(string query, int top)
    {
        var terms = Tokenize(query).Distinct(StringComparer.Ordinal).ToList();
        var hits = new List<PdfBm25Hit>();

        if (terms.Count == 0 || top <= 0)
        {
            return hits;
        }

        for (var index = 0; index < _lengths.Count; index++)
        {
            var score = Score(terms, index);

            if (score > 0)
            {
                hits.Add(new PdfBm25Hit(index, score));
            }
        }

        return [.. hits
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Index)
            .Take(top)];
    }

    /// <summary>
    /// Scores one passage against a set of query terms.
    /// </summary>
    /// <param name="terms">The query terms, already tokenized.</param>
    /// <param name="index">The passage.</param>
    /// <returns>The BM25 score; zero when the passage shares no term.</returns>
    public double Score(IReadOnlyCollection<string> terms, int index)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var frequencies = _termFrequencies[index];
        var normalizer = K1 * (1 - B + (B * (_averageLength <= 0 ? 1 : _lengths[index] / _averageLength)));
        var score = 0d;

        foreach (var term in terms)
        {
            if (!frequencies.TryGetValue(term, out var frequency))
            {
                continue;
            }

            score += InverseDocumentFrequency(term) * (frequency * (K1 + 1)) / (frequency + normalizer);
        }

        return score;
    }

    /// <summary>
    /// Returns how rare a term is across the passages.
    /// </summary>
    /// <param name="term">The term, already tokenized.</param>
    /// <returns>The BM25 inverse document frequency; always positive.</returns>
    public double InverseDocumentFrequency(string term)
    {
        var frequency = _documentFrequencies.GetValueOrDefault(term);

        return Math.Log(1 + ((_lengths.Count - frequency + 0.5) / (frequency + 0.5)));
    }

    /// <summary>
    /// Splits a text into the terms the index matches on.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The terms, in order, stop words removed.</returns>
    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();

        if (string.IsNullOrEmpty(text))
        {
            return tokens;
        }

        var builder = new StringBuilder();

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));

                continue;
            }

            // "1,200.50" stays one number, and "don't" one word.
            var joinsDigits = character is '.' or ',' &&
                builder.Length > 0 &&
                char.IsDigit(builder[^1]) &&
                index + 1 < text.Length &&
                char.IsDigit(text[index + 1]);

            if (joinsDigits)
            {
                if (character == '.')
                {
                    builder.Append('.');
                }

                continue;
            }

            if (character is '\'' or '’' && builder.Length > 0 && index + 1 < text.Length && char.IsLetter(text[index + 1]))
            {
                continue;
            }

            Flush(builder, tokens);
        }

        Flush(builder, tokens);

        return tokens;
    }

    /// <summary>
    /// Returns whether a lower-cased word is too common to rank on.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <returns><see langword="true"/> for a stop word.</returns>
    public static bool IsStopWord(string word)
    {
        return word is not null && _stopWords.Contains(word);
    }

    private static void Flush(StringBuilder builder, List<string> tokens)
    {
        if (builder.Length == 0)
        {
            return;
        }

        var raw = builder.ToString();
        builder.Clear();

        if ((raw.Length < 2 && !char.IsDigit(raw[0])) || _stopWords.Contains(raw))
        {
            return;
        }

        var token = Fold(raw);

        if (!_stopWords.Contains(token))
        {
            tokens.Add(token);
        }
    }

    private static string Fold(string token)
    {
        if (token.Length > 4 && token.EndsWith("ies", StringComparison.Ordinal))
        {
            return token[..^3] + "y";
        }

        if (token.Length > 3 &&
            token[^1] == 's' &&
            !token.EndsWith("ss", StringComparison.Ordinal) &&
            !token.EndsWith("us", StringComparison.Ordinal) &&
            !token.EndsWith("is", StringComparison.Ordinal) &&
            char.IsLetter(token[^2]))
        {
            return token[..^1];
        }

        return token;
    }
}
