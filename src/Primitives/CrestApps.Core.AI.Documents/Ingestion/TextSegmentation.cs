namespace CrestApps.Core.AI.Documents.Ingestion;

/// <summary>
/// Sentence segmentation that does not assume the text is written in a Latin script.
/// </summary>
/// <remarks>
/// Two assumptions are easy to make and wrong outside English. The first is that a sentence ends in
/// <c>.</c>, <c>!</c> or <c>?</c>; many scripts use their own terminator, and some of those are never
/// followed by a space. The second is that a new sentence starts with a capital; Arabic, Hebrew, CJK, Thai
/// and the Indic scripts have no case at all, so a rule keyed on capitalisation simply never fires there.
/// </remarks>
public static class TextSegmentation
{
    /// <summary>
    /// Determines whether the character ends a sentence.
    /// </summary>
    /// <param name="value">The character.</param>
    /// <returns><see langword="true"/> when the character is a sentence terminator.</returns>
    public static bool IsSentenceTerminator(char value)
    {
        return value switch
        {
            '.' or '!' or '?' or '…' => true,
            ';' => true,                 // Greek question mark.
            '։' => true,                 // Armenian full stop.
            '؟' or '۔' => true,     // Arabic question mark and full stop.
            '।' or '॥' => true,     // Devanagari danda and double danda.
            '།' => true,                 // Tibetan shad.
            '၊' or '။' => true,     // Myanmar little section and section.
            '።' => true,                 // Ethiopic full stop.
            '។' => true,                 // Khmer khan.
            '。' => true,                 // Ideographic full stop.
            '！' or '．' or '？' => true, // Fullwidth forms.
            _ => false,
        };
    }

    /// <summary>
    /// Determines whether a terminator has to be followed by whitespace to end a sentence.
    /// </summary>
    /// <param name="value">The terminator.</param>
    /// <returns><see langword="true"/> when whitespace is required.</returns>
    /// <remarks>
    /// A full stop also separates the parts of a number and follows an abbreviation, so it only ends a
    /// sentence when something separates it from what follows. The scripts that write without spaces have
    /// terminators that are unambiguous on their own.
    /// </remarks>
    public static bool RequiresFollowingWhitespace(char value)
    {
        return value is '.' or '!' or '?' or '…' or ';';
    }

    /// <summary>
    /// Determines whether the text reads as more than one sentence.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when a sentence ends and another begins inside the text.</returns>
    public static bool ContainsSentenceBoundary(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        for (var index = 0; index < text.Length - 1; index++)
        {
            if (!IsSentenceTerminator(text[index]))
            {
                continue;
            }

            var next = index + 1;

            if (RequiresFollowingWhitespace(text[index]))
            {
                if (!char.IsWhiteSpace(text[next]))
                {
                    continue;
                }

                next++;

                while (next < text.Length && char.IsWhiteSpace(text[next]))
                {
                    next++;
                }
            }
            else
            {
                while (next < text.Length && char.IsWhiteSpace(text[next]))
                {
                    next++;
                }
            }

            if (next >= text.Length || !char.IsLetter(text[next]))
            {
                continue;
            }

            // A capital starts the next sentence in a cased script. In a caseless one there is no such
            // signal, so any letter counts; requiring a capital there would mean the rule never fires.
            if (char.IsUpper(text[next]) || IsCaseless(text[next]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Splits the text into sentences.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The sentences, including any trailing fragment.</returns>
    public static IEnumerable<string> SplitSentences(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var start = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (!IsSentenceTerminator(text[index]))
            {
                continue;
            }

            if (RequiresFollowingWhitespace(text[index]) &&
                index + 1 < text.Length &&
                !char.IsWhiteSpace(text[index + 1]))
            {
                continue;
            }

            yield return text[start..(index + 1)];

            start = index + 1;
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }

    /// <summary>
    /// Determines whether a letter belongs to a script that has no upper and lower case.
    /// </summary>
    /// <param name="value">The letter.</param>
    /// <returns><see langword="true"/> when the letter has no case distinction.</returns>
    public static bool IsCaseless(char value)
    {
        return char.IsLetter(value) &&
            char.ToUpperInvariant(value) == value &&
            char.ToLowerInvariant(value) == value;
    }

    /// <summary>
    /// Determines whether a letter is lowercase, or belongs to a script with no case at all.
    /// </summary>
    /// <param name="value">The letter.</param>
    /// <returns><see langword="true"/> when the letter does not start a new sentence by its case.</returns>
    public static bool IsLowerOrCaseless(char value)
    {
        return char.IsLower(value) || IsCaseless(value);
    }
}
