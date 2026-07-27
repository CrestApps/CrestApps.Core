using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Sanitizes text for text-to-speech (TTS) by stripping markdown formatting,
/// code blocks, emoji, and other non-speech elements.
/// </summary>
public static partial class SpeechTextSanitizer
{
    /// <summary>
    /// Removes markdown formatting, code blocks, emoji, and other non-speech
    /// elements from the specified text so it can be spoken naturally by a TTS engine.
    /// </summary>
    /// <param name="text">The text to sanitize.</param>
    /// <returns>The sanitized text suitable for speech synthesis, or the original value when blank.</returns>
    public static string Sanitize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (!ContainsMarkdownCandidate(text))
        {
            return NormalizeUnicodeAndWhitespace(text);
        }

        // Remove fenced code blocks (```...```).
        text = FencedCodeBlockPattern().Replace(text, " ");

        // Remove inline code (`code`).
        text = InlineCodePattern().Replace(text, " ");

        // Remove markdown images ![alt](url).
        text = MarkdownImagePattern().Replace(text, " ");

        // Convert markdown links [text](url) to just text.
        text = MarkdownLinkPattern().Replace(text, "$1");

        // Remove bold/italic markers (**, *, ___, __, _).
        text = RemoveBoldItalicMarkers(text);

        // Remove heading markers (# through ######).
        text = HeadingMarkerPattern().Replace(text, string.Empty);

        // Remove horizontal rules (---, ***, ___).
        text = HorizontalRulePattern().Replace(text, string.Empty);

        // Remove list markers (- item, * item, + item).
        text = UnorderedListMarkerPattern().Replace(text, string.Empty);

        // Remove numbered list markers (1. item, 2. item).
        text = OrderedListMarkerPattern().Replace(text, string.Empty);

        return NormalizeUnicodeAndWhitespace(text);
    }

    /// <summary>
    /// Determines whether any ordered markdown pass can match the text.
    /// </summary>
    /// <param name="text">The text to inspect.</param>
    /// <returns>Whether the text contains a possible markdown match.</returns>
    private static bool ContainsMarkdownCandidate(string text)
    {
        foreach (var character in text)
        {
            if (character is '`' or '!' or '[' or '*' or '_' or '#' or '-' or '+' ||
                char.IsDigit(character))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes every asterisk and underscore with the same semantics as the legacy marker expression.
    /// </summary>
    /// <param name="text">The text to process.</param>
    /// <returns>The text without bold or italic markers.</returns>
    private static string RemoveBoldItalicMarkers(string text)
    {
        var markerCount = 0;

        foreach (var character in text)
        {
            if (character is '*' or '_')
            {
                markerCount++;
            }
        }

        if (markerCount == 0)
        {
            return text;
        }

        return string.Create(text.Length - markerCount, text, static (destination, source) =>
        {
            var destinationIndex = 0;

            foreach (var character in source)
            {
                if (character is not ('*' or '_'))
                {
                    destination[destinationIndex++] = character;
                }
            }
        });
    }

    /// <summary>
    /// Removes supplementary pairs and configured BMP symbols, collapses .NET whitespace, and trims the result.
    /// </summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>The normalized text.</returns>
    private static string NormalizeUnicodeAndWhitespace(string text)
    {
        var changed = false;
        var hasContent = false;
        var pendingWhitespace = false;
        var normalizedLength = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (char.IsHighSurrogate(character) &&
                index + 1 < text.Length &&
                char.IsLowSurrogate(text[index + 1]))
            {
                changed = true;
                index++;

                continue;
            }

            if (IsRemovedBmpSymbol(character))
            {
                changed = true;

                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!hasContent || pendingWhitespace || character != ' ')
                {
                    changed = true;
                }

                pendingWhitespace = hasContent;

                continue;
            }

            if (pendingWhitespace)
            {
                normalizedLength++;
                pendingWhitespace = false;
            }

            normalizedLength++;
            hasContent = true;
        }

        if (pendingWhitespace)
        {
            changed = true;
        }

        if (!changed)
        {
            return text;
        }

        return string.Create(normalizedLength, text, static (destination, source) =>
        {
            var destinationIndex = 0;
            var pendingWhitespace = false;

            for (var index = 0; index < source.Length; index++)
            {
                var character = source[index];

                if (char.IsHighSurrogate(character) &&
                    index + 1 < source.Length &&
                    char.IsLowSurrogate(source[index + 1]))
                {
                    index++;

                    continue;
                }

                if (IsRemovedBmpSymbol(character))
                {
                    continue;
                }

                if (char.IsWhiteSpace(character))
                {
                    pendingWhitespace = destinationIndex > 0;

                    continue;
                }

                if (pendingWhitespace)
                {
                    destination[destinationIndex++] = ' ';
                    pendingWhitespace = false;
                }

                destination[destinationIndex++] = character;
            }
        });
    }

    /// <summary>
    /// Determines whether the character belongs to a BMP range removed by the legacy expression.
    /// </summary>
    /// <param name="character">The character to inspect.</param>
    /// <returns>Whether the character must be removed.</returns>
    private static bool IsRemovedBmpSymbol(char character)
    {
        return character == '\u200D' ||
            character is >= '\u2600' and <= '\u27BF' ||
            character is >= '\uFE00' and <= '\uFE0F';
    }

    /// <summary>
    /// Gets the fenced code block regular expression.
    /// </summary>
    [GeneratedRegex(@"```[\s\S]*?```")]
    private static partial Regex FencedCodeBlockPattern();

    /// <summary>
    /// Gets the inline code regular expression.
    /// </summary>
    [GeneratedRegex(@"`[^`]+`")]
    private static partial Regex InlineCodePattern();

    /// <summary>
    /// Gets the markdown image regular expression.
    /// </summary>
    [GeneratedRegex(@"!\[[^\]]*\]\([^\)]*\)")]
    private static partial Regex MarkdownImagePattern();

    /// <summary>
    /// Gets the markdown link regular expression.
    /// </summary>
    [GeneratedRegex(@"\[([^\]]*)\]\([^\)]*\)")]
    private static partial Regex MarkdownLinkPattern();

    /// <summary>
    /// Gets the heading marker regular expression.
    /// </summary>
    [GeneratedRegex(@"^#{1,6}\s+", RegexOptions.Multiline)]
    private static partial Regex HeadingMarkerPattern();

    /// <summary>
    /// Gets the horizontal rule regular expression.
    /// </summary>
    [GeneratedRegex(@"^[-*_]{3,}\s*$", RegexOptions.Multiline)]
    private static partial Regex HorizontalRulePattern();

    /// <summary>
    /// Gets the unordered list marker regular expression.
    /// </summary>
    [GeneratedRegex(@"^\s*[-*+]\s+", RegexOptions.Multiline)]
    private static partial Regex UnorderedListMarkerPattern();

    /// <summary>
    /// Gets the ordered list marker regular expression.
    /// </summary>
    [GeneratedRegex(@"^\s*\d+\.\s+", RegexOptions.Multiline)]
    private static partial Regex OrderedListMarkerPattern();
}
