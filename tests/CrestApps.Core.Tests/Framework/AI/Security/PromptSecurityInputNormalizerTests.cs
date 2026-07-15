using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.Tests.Framework.AI.Security;

/// <summary>
/// Tests the prompt security input normalization compatibility contract.
/// </summary>
public sealed class PromptSecurityInputNormalizerTests
{
    private const int DefaultMaxPromptLength = 8000;
    private const PromptRiskLevel DefaultBlockingThreshold = PromptRiskLevel.High;

    private static readonly FrozenDictionary<char, char> LegacyHomoglyphMap = new Dictionary<char, char>
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
    /// Gets core normalization cases with their exact text and telemetry values.
    /// </summary>
    public static TheoryData<string, string, string, int, int, int, bool> CoreCases => new()
    {
        { null, string.Empty, string.Empty, 0, 0, 0, false },
        { string.Empty, string.Empty, string.Empty, 0, 0, 0, false },
        { "   ", string.Empty, string.Empty, 0, 0, 0, false },
        { "\t\r\n", string.Empty, string.Empty, 0, 0, 0, false },
        { "\t\u00A0\r\n", string.Empty, string.Empty, 0, 0, 0, true },
        { "alpha", "alpha", "alpha", 0, 0, 0, false },
        { " alpha", "alpha", "alpha", 0, 0, 0, false },
        { "alpha ", "alpha", "alpha", 0, 1, 0, false },
        { " alpha ", "alpha", "alpha", 0, 1, 0, false },
        { "alpha beta", "alpha beta", "alpha beta", 0, 1, 0, false },
        { "alpha  beta", "alpha beta", "alpha beta", 0, 1, 0, false },
        { "alpha\t\r\nbeta", "alpha beta", "alpha beta", 0, 1, 0, false },
        { "alpha\u00A0beta", "alpha beta", "alpha beta", 0, 1, 0, true },
        { "alpha\u3000beta", "alpha beta", "alpha beta", 0, 1, 0, true },
        { "a\u200Bb", "ab", "ab", 1, 0, 0, false },
        { "a \u200B b", "a b", "a b", 1, 1, 0, false },
        { " \u200B ", string.Empty, string.Empty, 1, 0, 0, false },
        { "\u200Balpha\u200B", "alpha", "alpha", 2, 0, 0, false },
        { "a\u034Fb", "ab", "ab", 1, 0, 0, false },
        { "a\u0000b", "a\u0000b", "a\u0000b", 0, 0, 0, false },
        { "\uFF21", "A", "A", 0, 0, 0, true },
        { "e\u0301", "\u00E9", "\u00E9", 0, 0, 0, true },
        { "\uFB01", "fi", "fi", 0, 0, 0, true },
        { "x\u0308", "\u1E8D", "\u1E8D", 0, 0, 0, true },
    };

    /// <summary>
    /// Gets every configured homoglyph mapping and its exact post-normalization behavior.
    /// </summary>
    public static TheoryData<string, string, string, int, bool> HomoglyphCases => new()
    {
        { "\u0410", "\u0410", "A", 1, false },
        { "\u0430", "\u0430", "a", 1, false },
        { "\u0392", "\u0392", "B", 1, false },
        { "\u0412", "\u0412", "B", 1, false },
        { "\u0432", "\u0432", "b", 1, false },
        { "\u03F2", "\u03C2", "\u03C2", 0, true },
        { "\u0421", "\u0421", "C", 1, false },
        { "\u0441", "\u0441", "c", 1, false },
        { "\u0501", "\u0501", "d", 1, false },
        { "\u0395", "\u0395", "E", 1, false },
        { "\u0415", "\u0415", "E", 1, false },
        { "\u0435", "\u0435", "e", 1, false },
        { "\u04BD", "\u04BD", "f", 1, false },
        { "\u0397", "\u0397", "H", 1, false },
        { "\u041D", "\u041D", "H", 1, false },
        { "\u04A2", "\u04A2", "H", 1, false },
        { "\u04BB", "\u04BB", "h", 1, false },
        { "\u0406", "\u0406", "I", 1, false },
        { "\u0399", "\u0399", "I", 1, false },
        { "\u0408", "\u0408", "J", 1, false },
        { "\u0458", "\u0458", "j", 1, false },
        { "\u039A", "\u039A", "K", 1, false },
        { "\u041A", "\u041A", "K", 1, false },
        { "\u043A", "\u043A", "k", 1, false },
        { "\u039C", "\u039C", "M", 1, false },
        { "\u041C", "\u041C", "M", 1, false },
        { "\u043C", "\u043C", "m", 1, false },
        { "\u039D", "\u039D", "N", 1, false },
        { "\u041E", "\u041E", "O", 1, false },
        { "\u043E", "\u043E", "o", 1, false },
        { "\u03A1", "\u03A1", "P", 1, false },
        { "\u0420", "\u0420", "P", 1, false },
        { "\u0440", "\u0440", "p", 1, false },
        { "\u051A", "\u051A", "Q", 1, false },
        { "\u03A4", "\u03A4", "T", 1, false },
        { "\u0422", "\u0422", "T", 1, false },
        { "\u0442", "\u0442", "t", 1, false },
        { "\u03A7", "\u03A7", "X", 1, false },
        { "\u0425", "\u0425", "X", 1, false },
        { "\u0445", "\u0445", "x", 1, false },
        { "\u03A5", "\u03A5", "Y", 1, false },
        { "\u04AE", "\u04AE", "Y", 1, false },
        { "\u0443", "\u0443", "y", 1, false },
    };

    /// <summary>
    /// Gets valid surrogate-pair inputs and their exact normalized values.
    /// </summary>
    public static TheoryData<string, string, bool> ValidSurrogateCases => new()
    {
        { "\U0001F600", "\U0001F600", false },
        { "\U00010437", "\U00010437", false },
        { "\U0001D400", "A", true },
        { "\U000E0001", "\U000E0001", false },
        { "A\U0001F600B", "A\U0001F600B", false },
        { "A\U000E0001B", "A\U000E0001B", false },
    };

    /// <summary>
    /// Gets malformed UTF-16 inputs rejected by compatibility normalization.
    /// </summary>
    public static TheoryData<string> MalformedSurrogateCases => new()
    {
        { "\uD800" },
        { "\uDBFF" },
        { "\uDC00" },
        { "\uDFFF" },
        { "A\uD800B" },
        { "A\uDC00B" },
        { "\uD800\uD800" },
        { "\uDC00\uD800" },
        { "\uD800A" },
        { "A\uDC00" },
    };

    /// <summary>
    /// Verifies exact normalization text and every telemetry field for representative inputs.
    /// </summary>
    /// <param name="input">The raw input.</param>
    /// <param name="expectedNormalized">The expected normalized input.</param>
    /// <param name="expectedFolded">The expected folded input.</param>
    /// <param name="expectedRemovedCount">The expected removed-character count.</param>
    /// <param name="expectedCollapsedCount">The expected collapsed-whitespace-run count.</param>
    /// <param name="expectedReplacementCount">The expected homoglyph replacement count.</param>
    /// <param name="expectedUnicodeNormalized">The expected Unicode-normalized flag.</param>
    [Theory]
    [MemberData(nameof(CoreCases))]
    public void Normalize_PreservesExactTextAndTelemetry(
        string input,
        string expectedNormalized,
        string expectedFolded,
        int expectedRemovedCount,
        int expectedCollapsedCount,
        int expectedReplacementCount,
        bool expectedUnicodeNormalized)
    {
        AssertExpected(
            input,
            expectedNormalized,
            expectedFolded,
            expectedRemovedCount,
            expectedCollapsedCount,
            expectedReplacementCount,
            expectedUnicodeNormalized);
    }

    /// <summary>
    /// Verifies every configured homoglyph mapping, including exact case and compatibility-normalization ordering.
    /// </summary>
    /// <param name="input">The mapped source character.</param>
    /// <param name="expectedNormalized">The expected normalized character.</param>
    /// <param name="expectedFolded">The expected folded character.</param>
    /// <param name="expectedReplacementCount">The expected replacement count.</param>
    /// <param name="expectedUnicodeNormalized">The expected Unicode-normalized flag.</param>
    [Theory]
    [MemberData(nameof(HomoglyphCases))]
    public void Normalize_PreservesEveryHomoglyphMappingAndCase(
        string input,
        string expectedNormalized,
        string expectedFolded,
        int expectedReplacementCount,
        bool expectedUnicodeNormalized)
    {
        AssertExpected(
            input,
            expectedNormalized,
            expectedFolded,
            0,
            0,
            expectedReplacementCount,
            expectedUnicodeNormalized);
    }

    /// <summary>
    /// Verifies every BMP whitespace code unit collapses through the legacy normalization order.
    /// </summary>
    [Fact]
    public void Normalize_CollapsesEveryBmpWhitespaceCodeUnit()
    {
        for (var value = 0; value <= char.MaxValue; value++)
        {
            var character = (char)value;

            if (!char.IsWhiteSpace(character))
            {
                continue;
            }

            var input = string.Concat("a", character, character, "b");
            var expectedUnicodeNormalized = !string.Equals(
                input,
                input.Normalize(NormalizationForm.FormKC),
                StringComparison.Ordinal);

            AssertExpected(
                input,
                "a b",
                "a b",
                0,
                1,
                0,
                expectedUnicodeNormalized);
        }
    }

    /// <summary>
    /// Verifies every BMP format code unit and the explicit combining grapheme joiner removal.
    /// </summary>
    [Fact]
    public void Normalize_RemovesEveryBmpFormatAndExplicitInvisibleCodeUnit()
    {
        for (var value = 0; value <= char.MaxValue; value++)
        {
            var character = (char)value;

            if (character != '\u034F'
                && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Format)
            {
                continue;
            }

            var input = string.Concat("A", character, "B");
            var expected = NormalizeLegacy(
                input,
                DefaultMaxPromptLength,
                DefaultBlockingThreshold);
            var actual = PromptSecurityInputNormalizer.Normalize(
                input,
                DefaultMaxPromptLength,
                DefaultBlockingThreshold);

            Assert.Equal("AB", expected.NormalizedInput);
            Assert.True(expected.Telemetry.RemovedZeroWidthCharacterCount > 0);
            AssertEquivalent(expected, actual);
        }
    }

    /// <summary>
    /// Verifies every BMP control code unit is retained or whitespace-collapsed exactly as before.
    /// </summary>
    [Fact]
    public void Normalize_PreservesEveryBmpControlCodeUnitBehavior()
    {
        for (var value = 0; value <= char.MaxValue; value++)
        {
            var character = (char)value;

            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Control)
            {
                continue;
            }

            AssertLegacyAndProductionEquivalent(string.Concat("A", character, "B"));
        }
    }

    /// <summary>
    /// Verifies valid surrogate pairs, supplementary compatibility characters, and supplementary format characters.
    /// </summary>
    /// <param name="input">The valid UTF-16 input.</param>
    /// <param name="expectedNormalized">The expected normalized value.</param>
    /// <param name="expectedUnicodeNormalized">The expected Unicode-normalized flag.</param>
    [Theory]
    [MemberData(nameof(ValidSurrogateCases))]
    public void Normalize_PreservesValidSurrogatePairBehavior(
        string input,
        string expectedNormalized,
        bool expectedUnicodeNormalized)
    {
        AssertExpected(
            input,
            expectedNormalized,
            expectedNormalized,
            0,
            0,
            0,
            expectedUnicodeNormalized);
    }

    /// <summary>
    /// Verifies malformed lone and misordered surrogates continue to fail during Unicode normalization.
    /// </summary>
    /// <param name="input">The malformed UTF-16 input.</param>
    [Theory]
    [MemberData(nameof(MalformedSurrogateCases))]
    public void Normalize_RejectsMalformedSurrogates(string input)
    {
        var legacyException = Assert.Throws<ArgumentException>(() => NormalizeLegacy(
            input,
            DefaultMaxPromptLength,
            DefaultBlockingThreshold));
        var productionException = Assert.Throws<ArgumentException>(() => PromptSecurityInputNormalizer.Normalize(
            input,
            DefaultMaxPromptLength,
            DefaultBlockingThreshold));

        Assert.Equal(legacyException.Message, productionException.Message);
        Assert.Equal(legacyException.ParamName, productionException.ParamName);
    }

    /// <summary>
    /// Verifies normalization does not enforce, truncate, or reinterpret the configured maximum length.
    /// </summary>
    /// <param name="length">The input length around the configured boundary.</param>
    [Theory]
    [InlineData(7999)]
    [InlineData(8000)]
    [InlineData(8001)]
    public void Normalize_PreservesLengthBoundaryInputs(int length)
    {
        var input = new string('x', length);
        var context = PromptSecurityInputNormalizer.Normalize(
            input,
            DefaultMaxPromptLength,
            PromptRiskLevel.Critical);

        Assert.Equal(input, context.NormalizedInput);
        Assert.Equal(input, context.FoldedInput);
        Assert.Equal(length, context.Telemetry.OriginalLength);
        Assert.Equal(length, context.Telemetry.NormalizedLength);
        Assert.Equal(length, context.Telemetry.FoldedLength);
        Assert.Equal(DefaultMaxPromptLength, context.MaxPromptLength);
        Assert.Equal(PromptRiskLevel.Critical, context.BlockingThreshold);
        AssertEquivalent(
            NormalizeLegacy(input, DefaultMaxPromptLength, PromptRiskLevel.Critical),
            context);
    }

    /// <summary>
    /// Verifies benign non-empty text preserves the observable legacy string-reference behavior.
    /// </summary>
    [Fact]
    public void Normalize_PreservesBenignTextReferenceBehavior()
    {
        var input = new string("A benign ASCII prompt.".ToCharArray());
        var context = PromptSecurityInputNormalizer.Normalize(
            input,
            DefaultMaxPromptLength,
            DefaultBlockingThreshold);

        Assert.Same(input, context.OriginalInput);
        Assert.NotSame(input, context.NormalizedInput);
        Assert.NotSame(input, context.FoldedInput);
        Assert.NotSame(context.NormalizedInput, context.FoldedInput);
    }

    /// <summary>
    /// Verifies empty normalized results retain the shared empty-string reference behavior.
    /// </summary>
    [Fact]
    public void Normalize_PreservesEmptyResultReferenceBehavior()
    {
        string[] inputs =
        [
            null,
            string.Empty,
            "   ",
            "\u200B",
            " \u034F ",
        ];

        foreach (var input in inputs)
        {
            var context = PromptSecurityInputNormalizer.Normalize(
                input,
                DefaultMaxPromptLength,
                DefaultBlockingThreshold);

            Assert.Same(input ?? string.Empty, context.OriginalInput);
            Assert.Same(string.Empty, context.NormalizedInput);
            Assert.Same(string.Empty, context.FoldedInput);
        }
    }

    /// <summary>
    /// Differentially verifies interacting normalization, removal, whitespace, homoglyph, and UTF-16 combinations.
    /// </summary>
    [Fact]
    public void Normalize_MatchesLegacyPipelineAcrossGeneratedCompatibilityMatrix()
    {
        string[] prefixes =
        [
            string.Empty,
            " ",
            "\t",
            "start",
            "start ",
            "\u200B",
        ];
        string[] leftFragments =
        [
            "alpha",
            "\u0410",
            "\uFF21",
            "e\u0301",
            "\u034F",
            "\U0001F600",
            "\u0001",
            "\u2060",
        ];
        string[] separators =
        [
            string.Empty,
            " ",
            "  ",
            "\t",
            "\r\n",
            "\u00A0",
            "\u3000",
            "\u200B",
            " \u200B ",
            "\u034F",
        ];
        string[] rightFragments =
        [
            "omega",
            "\u0430",
            "\u03F2",
            "x\u0308",
            "\U0001D400",
            "\U000E0001",
            "\u0000",
            "\uFEFF",
        ];
        string[] suffixes =
        [
            string.Empty,
            " ",
            "\t",
            "\u200B",
            "\u0410",
            "\u034F",
        ];

        foreach (var prefix in prefixes)
        {
            foreach (var leftFragment in leftFragments)
            {
                foreach (var separator in separators)
                {
                    foreach (var rightFragment in rightFragments)
                    {
                        foreach (var suffix in suffixes)
                        {
                            AssertLegacyAndProductionEquivalent(string.Concat(
                                prefix,
                                leftFragment,
                                separator,
                                rightFragment,
                                suffix));
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Differentially verifies every BMP code unit in both ordinary and whitespace-interaction contexts.
    /// </summary>
    [Fact]
    public void Normalize_MatchesLegacyPipelineForEveryBmpCodeUnit()
    {
        for (var value = 0; value <= char.MaxValue; value++)
        {
            var character = (char)value;

            AssertLegacyAndProductionEquivalent(string.Concat("A", character, "B"));
            AssertLegacyAndProductionEquivalent(string.Concat("A ", character, " B"));
        }
    }

    /// <summary>
    /// Verifies an exact expected result against both the captured legacy pipeline and production.
    /// </summary>
    /// <param name="input">The raw input.</param>
    /// <param name="expectedNormalized">The expected normalized input.</param>
    /// <param name="expectedFolded">The expected folded input.</param>
    /// <param name="expectedRemovedCount">The expected removed-character count.</param>
    /// <param name="expectedCollapsedCount">The expected collapsed-whitespace-run count.</param>
    /// <param name="expectedReplacementCount">The expected homoglyph replacement count.</param>
    /// <param name="expectedUnicodeNormalized">The expected Unicode-normalized flag.</param>
    private static void AssertExpected(
        string input,
        string expectedNormalized,
        string expectedFolded,
        int expectedRemovedCount,
        int expectedCollapsedCount,
        int expectedReplacementCount,
        bool expectedUnicodeNormalized)
    {
        var legacy = NormalizeLegacy(
            input,
            DefaultMaxPromptLength,
            DefaultBlockingThreshold);
        var production = PromptSecurityInputNormalizer.Normalize(
            input,
            DefaultMaxPromptLength,
            DefaultBlockingThreshold);

        Assert.Equal(input ?? string.Empty, legacy.OriginalInput);
        Assert.Equal(expectedNormalized, legacy.NormalizedInput);
        Assert.Equal(expectedFolded, legacy.FoldedInput);
        Assert.Equal(DefaultMaxPromptLength, legacy.MaxPromptLength);
        Assert.Equal(DefaultBlockingThreshold, legacy.BlockingThreshold);
        Assert.Equal((input ?? string.Empty).Length, legacy.Telemetry.OriginalLength);
        Assert.Equal(expectedNormalized.Length, legacy.Telemetry.NormalizedLength);
        Assert.Equal(expectedFolded.Length, legacy.Telemetry.FoldedLength);
        Assert.Equal(expectedRemovedCount, legacy.Telemetry.RemovedZeroWidthCharacterCount);
        Assert.Equal(expectedCollapsedCount, legacy.Telemetry.CollapsedWhitespaceRunCount);
        Assert.Equal(expectedReplacementCount, legacy.Telemetry.HomoglyphReplacementCount);
        Assert.Equal(expectedUnicodeNormalized, legacy.Telemetry.UnicodeNormalized);
        Assert.Equal(0, legacy.Telemetry.MatchedRuleCount);
        Assert.Equal(0, legacy.Telemetry.DistinctCategoryCount);
        Assert.Equal(0, legacy.Telemetry.EvaluationDurationMilliseconds);
        AssertEquivalent(legacy, production);
    }

    /// <summary>
    /// Verifies the production result is exactly equivalent to the captured legacy result.
    /// </summary>
    /// <param name="input">The raw input.</param>
    private static void AssertLegacyAndProductionEquivalent(string input)
    {
        PromptSecurityEvaluationContext expected;

        try
        {
            expected = NormalizeLegacy(
                input,
                DefaultMaxPromptLength,
                DefaultBlockingThreshold);
        }
        catch (Exception expectedException)
        {
            var actualException = Record.Exception(() => PromptSecurityInputNormalizer.Normalize(
                input,
                DefaultMaxPromptLength,
                DefaultBlockingThreshold));

            Assert.NotNull(actualException);
            Assert.Equal(expectedException.GetType(), actualException.GetType());
            Assert.Equal(expectedException.Message, actualException.Message);

            return;
        }

        var actual = PromptSecurityInputNormalizer.Normalize(
            input,
            DefaultMaxPromptLength,
            DefaultBlockingThreshold);

        AssertEquivalent(expected, actual);
    }

    /// <summary>
    /// Verifies every evaluation context and telemetry field is equal.
    /// </summary>
    /// <param name="expected">The expected legacy context.</param>
    /// <param name="actual">The actual production context.</param>
    private static void AssertEquivalent(
        PromptSecurityEvaluationContext expected,
        PromptSecurityEvaluationContext actual)
    {
        Assert.Equal(expected.OriginalInput, actual.OriginalInput);
        Assert.Equal(expected.NormalizedInput, actual.NormalizedInput);
        Assert.Equal(expected.FoldedInput, actual.FoldedInput);
        Assert.Equal(expected.MaxPromptLength, actual.MaxPromptLength);
        Assert.Equal(expected.BlockingThreshold, actual.BlockingThreshold);
        Assert.Equal(expected.Telemetry.OriginalLength, actual.Telemetry.OriginalLength);
        Assert.Equal(expected.Telemetry.NormalizedLength, actual.Telemetry.NormalizedLength);
        Assert.Equal(expected.Telemetry.FoldedLength, actual.Telemetry.FoldedLength);
        Assert.Equal(
            expected.Telemetry.RemovedZeroWidthCharacterCount,
            actual.Telemetry.RemovedZeroWidthCharacterCount);
        Assert.Equal(
            expected.Telemetry.CollapsedWhitespaceRunCount,
            actual.Telemetry.CollapsedWhitespaceRunCount);
        Assert.Equal(
            expected.Telemetry.HomoglyphReplacementCount,
            actual.Telemetry.HomoglyphReplacementCount);
        Assert.Equal(expected.Telemetry.UnicodeNormalized, actual.Telemetry.UnicodeNormalized);
        Assert.Equal(expected.Telemetry.MatchedRuleCount, actual.Telemetry.MatchedRuleCount);
        Assert.Equal(expected.Telemetry.DistinctCategoryCount, actual.Telemetry.DistinctCategoryCount);
        Assert.Equal(
            expected.Telemetry.EvaluationDurationMilliseconds,
            actual.Telemetry.EvaluationDurationMilliseconds);
    }

    /// <summary>
    /// Executes the original three-stage normalization pipeline exactly.
    /// </summary>
    /// <param name="input">The raw prompt input.</param>
    /// <param name="maxPromptLength">The effective maximum prompt length.</param>
    /// <param name="blockingThreshold">The effective blocking threshold.</param>
    /// <returns>The legacy detector evaluation context.</returns>
    private static PromptSecurityEvaluationContext NormalizeLegacy(
        string input,
        int maxPromptLength,
        PromptRiskLevel blockingThreshold)
    {
        var originalInput = input ?? string.Empty;
        var unicodeNormalized = originalInput.Normalize(NormalizationForm.FormKC);
        var unicodeChanged = !string.Equals(originalInput, unicodeNormalized, StringComparison.Ordinal);

        var zeroWidthRemoved = RemoveInvisibleCharactersLegacy(
            unicodeNormalized,
            out var removedZeroWidthCount);
        var collapsedWhitespace = CollapseWhitespaceLegacy(
            zeroWidthRemoved,
            out var collapsedWhitespaceRunCount);
        var foldedInput = FoldHomoglyphsLegacy(
            collapsedWhitespace,
            out var homoglyphReplacementCount);

        return new PromptSecurityEvaluationContext
        {
            OriginalInput = originalInput,
            NormalizedInput = collapsedWhitespace,
            FoldedInput = foldedInput,
            MaxPromptLength = maxPromptLength,
            BlockingThreshold = blockingThreshold,
            Telemetry = new PromptSecurityDetectionTelemetry
            {
                OriginalLength = originalInput.Length,
                NormalizedLength = collapsedWhitespace.Length,
                FoldedLength = foldedInput.Length,
                RemovedZeroWidthCharacterCount = removedZeroWidthCount,
                CollapsedWhitespaceRunCount = collapsedWhitespaceRunCount,
                HomoglyphReplacementCount = homoglyphReplacementCount,
                UnicodeNormalized = unicodeChanged,
            },
        };
    }

    /// <summary>
    /// Executes the original invisible-character removal pass exactly.
    /// </summary>
    /// <param name="input">The compatibility-normalized input.</param>
    /// <param name="removedCount">The number of removed characters.</param>
    /// <returns>The input without configured invisible characters.</returns>
    private static string RemoveInvisibleCharactersLegacy(string input, out int removedCount)
    {
        removedCount = 0;

        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);

        foreach (var character in input)
        {
            if (IsInvisibleCharacterLegacy(character))
            {
                removedCount++;
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Executes the original whitespace-collapsing pass exactly.
    /// </summary>
    /// <param name="input">The input without configured invisible characters.</param>
    /// <param name="collapsedRunCount">The number of whitespace runs encountered after non-whitespace text.</param>
    /// <returns>The collapsed and trimmed input.</returns>
    private static string CollapseWhitespaceLegacy(string input, out int collapsedRunCount)
    {
        collapsedRunCount = 0;

        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        var inWhitespace = false;

        foreach (var character in input)
        {
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

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Executes the original homoglyph-folding pass exactly.
    /// </summary>
    /// <param name="input">The collapsed input.</param>
    /// <param name="replacementCount">The number of applied homoglyph substitutions.</param>
    /// <returns>The folded input.</returns>
    private static string FoldHomoglyphsLegacy(string input, out int replacementCount)
    {
        replacementCount = 0;

        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);

        foreach (var character in input)
        {
            if (LegacyHomoglyphMap.TryGetValue(character, out var replacement))
            {
                builder.Append(replacement);
                replacementCount++;
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Determines whether the original pipeline removes the supplied UTF-16 code unit.
    /// </summary>
    /// <param name="character">The UTF-16 code unit.</param>
    /// <returns><see langword="true"/> when the character is removed; otherwise <see langword="false"/>.</returns>
    private static bool IsInvisibleCharacterLegacy(char character)
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
