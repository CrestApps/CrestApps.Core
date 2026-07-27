using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace CrestApps.Core.AI.Security;

/// <summary>
/// Normalizes prompt input before regex evaluation to reduce common obfuscation bypasses.
/// </summary>
public static class PromptSecurityInputNormalizer
{
    private static readonly FrozenDictionary<char, char> HomoglyphMap = new Dictionary<char, char>
    {
        ['\u0410'] = 'A',
        ['\u0430'] = 'a',
        ['\u0392'] = 'B',
        ['\u0412'] = 'B',
        ['\u0432'] = 'b',
        ['\u03F2'] = 'c',
        ['\u0421'] = 'C',
        ['\u0441'] = 'c',
        ['\u0501'] = 'd',
        ['\u0395'] = 'E',
        ['\u0415'] = 'E',
        ['\u0435'] = 'e',
        ['\u04BD'] = 'f',
        ['\u0397'] = 'H',
        ['\u041D'] = 'H',
        ['\u04A2'] = 'H',
        ['\u04BB'] = 'h',
        ['\u0406'] = 'I',
        ['\u0399'] = 'I',
        ['\u0408'] = 'J',
        ['\u0458'] = 'j',
        ['\u039A'] = 'K',
        ['\u041A'] = 'K',
        ['\u043A'] = 'k',
        ['\u039C'] = 'M',
        ['\u041C'] = 'M',
        ['\u043C'] = 'm',
        ['\u039D'] = 'N',
        ['\u041E'] = 'O',
        ['\u043E'] = 'o',
        ['\u03A1'] = 'P',
        ['\u0420'] = 'P',
        ['\u0440'] = 'p',
        ['\u051A'] = 'Q',
        ['\u03A4'] = 'T',
        ['\u0422'] = 'T',
        ['\u0442'] = 't',
        ['\u03A7'] = 'X',
        ['\u0425'] = 'X',
        ['\u0445'] = 'x',
        ['\u03A5'] = 'Y',
        ['\u04AE'] = 'Y',
        ['\u0443'] = 'y',
    }.ToFrozenDictionary();

    /// <summary>
    /// Normalizes the supplied input and returns the detector evaluation context.
    /// </summary>
    /// <param name="input">The raw prompt input.</param>
    /// <param name="maxPromptLength">The effective maximum prompt length.</param>
    /// <param name="blockingThreshold">The effective blocking threshold.</param>
    public static PromptSecurityEvaluationContext Normalize(
        string input,
        int maxPromptLength,
        PromptRiskLevel blockingThreshold)
    {
        var originalInput = input ?? string.Empty;
        var unicodeNormalized = originalInput.Normalize(NormalizationForm.FormKC);
        var unicodeChanged = !string.Equals(originalInput, unicodeNormalized, StringComparison.Ordinal);

        var collapsedWhitespace = RemoveInvisibleCharactersAndCollapseWhitespace(
            unicodeNormalized,
            out var removedZeroWidthCount,
            out var collapsedWhitespaceRunCount);
        var telemetry = new PromptSecurityDetectionTelemetry
        {
            OriginalLength = originalInput.Length,
            NormalizedLength = collapsedWhitespace.Length,
            RemovedZeroWidthCharacterCount = removedZeroWidthCount,
            CollapsedWhitespaceRunCount = collapsedWhitespaceRunCount,
            UnicodeNormalized = unicodeChanged,
        };
        var foldedInput = FoldHomoglyphs(collapsedWhitespace, telemetry);
        telemetry.FoldedLength = foldedInput.Length;

        return new PromptSecurityEvaluationContext
        {
            OriginalInput = originalInput,
            NormalizedInput = collapsedWhitespace,
            FoldedInput = foldedInput,
            MaxPromptLength = maxPromptLength,
            BlockingThreshold = blockingThreshold,
            Telemetry = telemetry,
        };
    }

    /// <summary>
    /// Removes configured invisible characters and collapses whitespace in their resulting order.
    /// </summary>
    /// <param name="input">The compatibility-normalized input.</param>
    /// <param name="removedCount">The number of removed invisible characters.</param>
    /// <param name="collapsedRunCount">The number of whitespace runs encountered after non-whitespace text.</param>
    /// <returns>The normalized and trimmed input.</returns>
    private static string RemoveInvisibleCharactersAndCollapseWhitespace(
        string input,
        out int removedCount,
        out int collapsedRunCount)
    {
        removedCount = 0;
        collapsedRunCount = 0;

        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        var inWhitespace = false;

        foreach (var character in input)
        {
            if (IsInvisibleCharacter(character))
            {
                removedCount++;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (inWhitespace)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                    collapsedRunCount++;
                }

                inWhitespace = true;
                continue;
            }

            builder.Append(character);
            inWhitespace = false;
        }

        if (builder.Length > 0 && builder[builder.Length - 1] == ' ')
        {
            builder.Length--;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Folds configured homoglyph characters to their ASCII equivalents.
    /// </summary>
    /// <param name="input">The normalized input.</param>
    /// <param name="telemetry">The telemetry that receives the homoglyph replacement count.</param>
    /// <returns>The folded input.</returns>
    private static string FoldHomoglyphs(
        string input,
        PromptSecurityDetectionTelemetry telemetry)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return string.Create(
            input.Length,
            (Input: input, Telemetry: telemetry),
            static (destination, state) =>
            {
                for (var index = 0; index < state.Input.Length; index++)
                {
                    var character = state.Input[index];

                    if (HomoglyphMap.TryGetValue(character, out var replacement))
                    {
                        destination[index] = replacement;
                        state.Telemetry.HomoglyphReplacementCount++;
                        continue;
                    }

                    destination[index] = character;
                }
            });
    }

    /// <summary>
    /// Determines whether the supplied UTF-16 code unit is removed as invisible input.
    /// </summary>
    /// <param name="character">The UTF-16 code unit.</param>
    /// <returns><see langword="true"/> when the character is removed; otherwise <see langword="false"/>.</returns>
    private static bool IsInvisibleCharacter(char character)
    {
        return character is '\u200B'
            or '\u200C'
            or '\u200D'
            or '\u2060'
            or '\uFEFF'
            or '\u00AD'
            or '\u180E'
            or '\u034F'
            || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format;
    }
}
