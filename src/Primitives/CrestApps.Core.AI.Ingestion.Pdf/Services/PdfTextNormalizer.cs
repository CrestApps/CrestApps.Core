using System.Text;

namespace CrestApps.Core.AI.Ingestion.Pdf.Services;

/// <summary>
/// Repairs the damage PDF text extraction does to words, in any script.
/// </summary>
/// <remarks>
/// Four things routinely make extracted text unsearchable, and none of them is specific to one language.
/// A word broken across a line keeps its hyphen. A typographic ligature extracts as a single character no
/// search will match. Accents can arrive decomposed, so a word looks identical on screen and compares
/// unequal. And invisible formatting characters end up in the middle of words.
/// <para>
/// Normalization stays deliberately narrow about numbers. Composing accents with <c>FormC</c> is safe, but
/// running the whole string through <c>FormKC</c> would also rewrite superscripts, silently turning a
/// printed coefficient into a different number. Digits, decimal commas and decimal points are never touched.
/// </para>
/// </remarks>
public static class PdfTextNormalizer
{
    private const char SoftHyphen = '­';

    private const char LineSeparator = (char)0x2028;

    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>
    /// The presentation-form ranges expanded back to the letters they stand for. Each is a block of glyphs a
    /// typesetter uses in place of ordinary letters, so a reader sees no difference and a search sees a
    /// different word entirely.
    /// </summary>
    private static readonly (char Start, char End)[] _presentationForms =
    [
        ('ﬀ', 'ﬆ'), // Latin ligatures: ff, fi, fl, ffi, ffl, long st, st.
        ('ﬓ', 'ﬗ'), // Armenian ligatures.
        ('ﭐ', 'ﷇ'), // Arabic presentation forms A, stopping before the word ligatures.
        ('ﹰ', 'ﻼ'), // Arabic presentation forms B.
    ];

    /// <summary>
    /// Normalizes one piece of extracted text.
    /// </summary>
    /// <param name="text">The extracted text.</param>
    /// <returns>
    /// The normalized text, trimmed, with each run of whitespace collapsed to one space, or to one line break
    /// when the run contained one.
    /// </returns>
    /// <remarks>
    /// Line breaks survive because they carry structure the words alone do not: a table of contents is one
    /// block whose every line is a title and a page number, and it can only be read line by line.
    /// </remarks>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var repaired = RejoinBrokenWords(ExpandPresentationForms(RemoveInvisibleCharacters(text)));

        return CollapseWhitespace(Compose(repaired));
    }

    /// <summary>
    /// Strips formatting characters that carry no meaning but do break a search.
    /// </summary>
    /// <param name="text">The extracted text.</param>
    /// <returns>The text without invisible formatting characters.</returns>
    /// <remarks>
    /// The zero-width non-joiner and joiner are deliberately kept. They look invisible but they are letters
    /// of the word in Persian and in the Indic scripts, and removing them changes what the word says.
    /// </remarks>
    private static string RemoveInvisibleCharacters(string text)
    {
        if (!text.Any(IsInvisible))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (!IsInvisible(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool IsInvisible(char value)
    {
        return value switch
        {
            '​' => true,                 // Zero width space.
            '﻿' => true,                 // Byte order mark used as a zero width no-break space.
            '‎' or '‏' => true,     // Left-to-right and right-to-left marks.
            >= '‪' and <= '‮' => true, // Bidirectional embedding and override controls.
            >= '⁦' and <= '⁩' => true, // Bidirectional isolate controls.
            _ => false,
        };
    }

    /// <summary>
    /// Expands ligature and presentation glyphs back to the letters they stand for.
    /// </summary>
    /// <param name="text">The extracted text.</param>
    /// <returns>The text with presentation forms expanded.</returns>
    private static string ExpandPresentationForms(string text)
    {
        if (!text.Any(IsPresentationForm))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + 8);

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (!IsPresentationForm(character))
            {
                builder.Append(character);

                continue;
            }

            builder.Append(Expand(character));

            // Some producers emit a space straight after a ligature glyph, which splits one word in two.
            // That space is dropped, but only when the next character is a lower-case letter, which is the
            // only evidence available that a word continued rather than ended.
            //
            // The test is deliberately case-based rather than "lower-case or caseless". A caseless script
            // has no such evidence to offer, and accepting its letters here collapsed the space at nearly
            // every word boundary: Arabic extracted as presentation forms ends each word in a final form and
            // starts the next with a caseless letter, so a whole paragraph ran together into one token and
            // nothing in it could be searched for.
            if (index + 2 < text.Length &&
                text[index + 1] == ' ' &&
                char.IsLower(text[index + 2]))
            {
                index++;
            }
        }

        return builder.ToString();
    }

    private static bool IsPresentationForm(char value)
    {
        foreach (var (start, end) in _presentationForms)
        {
            if (value >= start && value <= end)
            {
                return true;
            }
        }

        return false;
    }

    private static string Expand(char value)
    {
        try
        {
            return value.ToString().Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return value.ToString();
        }
    }

    /// <summary>
    /// Rejoins a word that was hyphenated across a line break.
    /// </summary>
    /// <param name="text">The extracted text, with its line breaks still in place.</param>
    /// <returns>The text with broken words made whole.</returns>
    /// <remarks>
    /// This matters far more in some languages than in others: German, Hungarian, Finnish and Dutch break
    /// long words constantly. A hyphen is only treated as a break when a line break follows it, because a
    /// hyphen in the middle of a line is punctuation. When the word continues in capitals the hyphen is kept
    /// and only the break is removed, since that is a compound rather than a broken word.
    /// </remarks>
    private static string RejoinBrokenWords(string text)
    {
        var builder = new StringBuilder(text.Length);

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (!IsHyphen(character) || builder.Length == 0 || !char.IsLetter(builder[^1]))
            {
                builder.Append(character);

                continue;
            }

            var after = SkipLineBreak(text, index + 1);

            if (after < 0 || after >= text.Length || !char.IsLetter(text[after]))
            {
                builder.Append(character);

                continue;
            }

            // A soft hyphen is only ever a hint about where a word may be broken, so it never survives.
            if (character != SoftHyphen && char.IsUpper(text[after]))
            {
                builder.Append(character);
            }

            index = after - 1;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Finds the first character after a run of whitespace that contains a line break.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">Where to start looking.</param>
    /// <returns>The index of the next character, or <c>-1</c> when no line break follows.</returns>
    private static int SkipLineBreak(string text, int start)
    {
        var index = start;
        var sawLineBreak = false;

        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            if (IsLineBreak(text[index]))
            {
                sawLineBreak = true;
            }

            index++;
        }

        return sawLineBreak ? index : -1;
    }

    /// <summary>
    /// Determines whether the character ends a line.
    /// </summary>
    /// <param name="value">The character.</param>
    /// <returns><see langword="true"/> when the character is a line or paragraph separator.</returns>
    private static bool IsLineBreak(char value)
    {
        return value is '\n' or '\r' || value == LineSeparator || value == ParagraphSeparator;
    }

    private static bool IsHyphen(char value)
    {
        return value is '-' or '‐' or '‑' or SoftHyphen;
    }

    /// <summary>
    /// Composes accents onto the letters they belong to, so a word that looks right also compares right.
    /// </summary>
    /// <param name="text">The extracted text.</param>
    /// <returns>The composed text.</returns>
    private static string Compose(string text)
    {
        try
        {
            return text.IsNormalized(NormalizationForm.FormC)
                ? text
                : text.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            // Extraction can produce unpaired surrogates. Unnormalized text is better than no text.
            return text;
        }
    }

    /// <summary>
    /// Collapses each run of whitespace to a single space, or to a single line break when the run contained
    /// one, and trims the result.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The collapsed text.</returns>
    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingWhitespace = false;
        var pendingLineBreak = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingWhitespace = builder.Length > 0;
                pendingLineBreak |= pendingWhitespace && IsLineBreak(character);

                continue;
            }

            if (pendingWhitespace)
            {
                builder.Append(pendingLineBreak ? '\n' : ' ');
                pendingWhitespace = false;
                pendingLineBreak = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
