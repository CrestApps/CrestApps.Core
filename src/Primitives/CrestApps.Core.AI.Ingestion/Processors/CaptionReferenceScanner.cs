using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// Finds where running prose refers to a numbered figure or table. The caption patterns are reused for this,
/// so a host that teaches the processor a new language gets in-text references in that language for free.
/// </summary>
internal static class CaptionReferenceScanner
{
    /// <summary>
    /// Finds the sentence in a paragraph that refers to the numbered figure.
    /// </summary>
    /// <param name="text">The paragraph text.</param>
    /// <param name="ordinal">The number to look for.</param>
    /// <param name="bucket">The caption family whose patterns are used.</param>
    /// <param name="options">The caption options.</param>
    /// <returns>The sentence, or <see langword="null"/> when the paragraph does not refer to it.</returns>
    public static string FindSentence(string text, int ordinal, string bucket, CaptionPatternOptions options)
    {
        if (string.IsNullOrWhiteSpace(text) || options == null)
        {
            return null;
        }

        foreach (var sentence in TextSegmentation.SplitSentences(text))
        {
            if (ReferencesOrdinal(sentence, ordinal, bucket, options))
            {
                return sentence.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether a sentence refers to the numbered figure.
    /// </summary>
    /// <param name="sentence">The sentence.</param>
    /// <param name="ordinal">The number to look for.</param>
    /// <param name="bucket">The caption family whose patterns are used.</param>
    /// <param name="options">The caption options.</param>
    /// <returns><see langword="true"/> when the sentence refers to it.</returns>
    public static bool ReferencesOrdinal(string sentence, int ordinal, string bucket, CaptionPatternOptions options)
    {
        foreach (var pattern in options.Patterns)
        {
            if (pattern?.Expression == null || !string.Equals(pattern.Bucket, bucket, StringComparison.Ordinal))
            {
                continue;
            }

            // Caption patterns are anchored at the start of a caption. In running prose the same label turns
            // up mid-sentence, so every word boundary is tested as though a caption started there. The
            // remainder is passed as its own string: a start offset would not move where the anchor matches.
            foreach (var start in EnumerateTokenStarts(sentence))
            {
                Match match;

                try
                {
                    match = pattern.Expression.Match(sentence[start..]);
                }
                catch (RegexMatchTimeoutException)
                {
                    continue;
                }

                if (match.Success && match.Index == 0 && ContainsOrdinal(match.Value, ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsOrdinal(string matched, int ordinal)
    {
        var digits = new StringBuilder();

        foreach (var character in matched)
        {
            if (char.IsDigit(character))
            {
                digits.Append(character);

                continue;
            }

            if (digits.Length > 0)
            {
                if (Equals(digits, ordinal))
                {
                    return true;
                }

                digits.Clear();
            }
        }

        return digits.Length > 0 && Equals(digits, ordinal);
    }

    private static bool Equals(StringBuilder digits, int ordinal)
    {
        return int.TryParse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
            value == ordinal;
    }

    private static IEnumerable<int> EnumerateTokenStarts(string text)
    {
        if (text.Length > 0)
        {
            yield return 0;
        }

        for (var index = 1; index < text.Length; index++)
        {
            if (char.IsWhiteSpace(text[index - 1]) && !char.IsWhiteSpace(text[index]))
            {
                yield return index;
            }
        }
    }
}
