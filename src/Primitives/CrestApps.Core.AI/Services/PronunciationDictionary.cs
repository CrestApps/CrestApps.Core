using System.Text.RegularExpressions;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Rewrites words and phrases into the alias a voice should say, before the text reaches any text-to-speech provider.
/// Whole words only: <c>SQL</c> is replaced in "SQL Server" but not inside "MySQL".
/// </summary>
public sealed class PronunciationDictionary
{
    private readonly (Regex Pattern, string Alias)[] _rules;

    private PronunciationDictionary((Regex Pattern, string Alias)[] rules)
    {
        _rules = rules;
    }

    /// <summary>
    /// Gets a dictionary that changes nothing.
    /// </summary>
    public static PronunciationDictionary Empty { get; } = new([]);

    /// <summary>
    /// Gets whether the dictionary has no rules.
    /// </summary>
    public bool IsEmpty => _rules.Length == 0;

    /// <summary>
    /// Builds a dictionary from the configured entries. Entries without text or alias are skipped, and longer phrases
    /// win over shorter ones that they contain.
    /// </summary>
    /// <param name="entries">The configured entries.</param>
    public static PronunciationDictionary Create(IEnumerable<PronunciationEntry> entries)
    {
        if (entries is null)
        {
            return Empty;
        }

        var rules = entries
            .Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Text) && entry.Alias is not null)
            .OrderByDescending(entry => entry.Text.Trim().Length)
            .Select(entry =>
            {
                var options = RegexOptions.CultureInvariant | (entry.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
                var pattern = new Regex(
                    @"(?<![\p{L}\p{N}_])" + Regex.Escape(entry.Text.Trim()) + @"(?![\p{L}\p{N}_])",
                    options,
                    TimeSpan.FromMilliseconds(250));

                return (pattern, entry.Alias);
            })
            .ToArray();

        return rules.Length == 0 ? Empty : new PronunciationDictionary(rules);
    }

    /// <summary>
    /// Applies the dictionary to the text.
    /// </summary>
    /// <param name="text">The text about to be synthesized.</param>
    public string Apply(string text)
    {
        if (_rules.Length == 0 || string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (var (pattern, alias) in _rules)
        {
            try
            {
                text = pattern.Replace(text, _ => alias);
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological input is spoken as written rather than blocking the reply.
            }
        }

        return text;
    }
}
