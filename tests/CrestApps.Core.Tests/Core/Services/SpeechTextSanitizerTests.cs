using System.Text.RegularExpressions;
using CrestApps.Core.AI.Services;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Tests the text-to-speech sanitization compatibility contract.
/// </summary>
public sealed partial class SpeechTextSanitizerTests
{
    /// <summary>
    /// Gets blank inputs whose original references must be returned.
    /// </summary>
    public static TheoryData<string> BlankInputs => new()
    {
        { null },
        { string.Empty },
        { new string(' ', 3) },
        { string.Concat("\t", "\r\n") },
        { string.Concat("\u00A0", "\u2003") },
    };

    /// <summary>
    /// Gets fenced code block inputs and their exact sanitized values.
    /// </summary>
    public static TheoryData<string, string> FencedCodeBlockCases => new()
    {
        { "before ```line 1\nline 2``` after", "before after" },
        { "```first```\nkeep\n```second```", "keep" },
        { "before ```unclosed\ncode", "before ```unclosed code" },
        { "start `````` end", "start end" },
        { "``````", string.Empty },
        { "before ```a ` b `` c``` after", "before after" },
        { "before ```outer ``` inner``` after", "before inner``` after" },
        { "```first``````second```", string.Empty },
        { "A\r\n```line 1\r\nline 2```\r\nB", "A B" },
    };

    /// <summary>
    /// Gets inline code inputs and their exact sanitized values.
    /// </summary>
    public static TheoryData<string, string> InlineCodeCases => new()
    {
        { "Use `code` now", "Use now" },
        { "A `one` and `two` B", "A and B" },
        { "A `line 1\nline 2` B", "A B" },
        { "A `` B", "A `` B" },
        { "A `unclosed B", "A `unclosed B" },
        { "A ``code`` B", "A ` ` B" },
        { "`a``b`", string.Empty },
    };

    /// <summary>
    /// Gets markdown image and link inputs and their exact sanitized values.
    /// </summary>
    public static TheoryData<string, string> ImageAndLinkCases => new()
    {
        { "Before ![alt](image.png) after", "Before after" },
        { "Before ![](image.png) after", "Before after" },
        { "See [label](https://example.com) now", "See label now" },
        { "See [](url) now", "See now" },
        { "![a](u) [one](1) and [two](2) ![](x)", "one and two" },
        { "See [outer [inner]](url) now", "See [outer [inner]](url) now" },
        { "See [a[b](url) now", "See a[b now" },
        { "See [label](a(b)c) now", "See labelc) now" },
        { "See ![alt](a(b)c) now", "See c) now" },
        { "See ![a[b]](url) now", "See ![a[b]](url) now" },
        { "![image](url)[link](url)", "link" },
        { "[**bold label**](url)", "bold label" },
    };

    /// <summary>
    /// Gets emphasis marker inputs and their exact sanitized values.
    /// </summary>
    public static TheoryData<string, string> EmphasisMarkerCases => new()
    {
        { "**bold** *italic* ___strong___ __under__ _em_", "bold italic strong under em" },
        { "****four**** _____five_____", "four five" },
        { "snake_case and a_b_c", "snakecase and abc" },
        { "* ** *** _ __ ___", string.Empty },
        { "a*_b_*c", "abc" },
        { "2 * 3 and 4 _ 5", "2 3 and 4 5" },
    };

    /// <summary>
    /// Gets heading and horizontal rule inputs and their exact sanitized values.
    /// </summary>
    public static TheoryData<string, string> HeadingAndHorizontalRuleCases => new()
    {
        { "# Heading", "Heading" },
        { "## Heading\nBody", "Heading Body" },
        { "Text # not heading", "Text # not heading" },
        { "   # indented", "# indented" },
        { "####### Too many", "####### Too many" },
        { "#NoSpace", "#NoSpace" },
        { "Intro\n### Three\nNot # heading\n###### Six", "Intro Three Not # heading Six" },
        { "One\r## Two", "One ## Two" },
        { "One\r\n## Two", "One Two" },
        { "# \nNext", "Next" },
        { "Before\n---\nAfter", "Before After" },
        { "Before\r\n---\r\nAfter", "Before After" },
        { "***", string.Empty },
        { "___", string.Empty },
        { "----", string.Empty },
        { "  ---  ", "---" },
        { "--- text", "--- text" },
        { "x ---\ny", "x --- y" },
        { "-*-", "--" },
    };

    /// <summary>
    /// Gets unordered and ordered list inputs and their exact sanitized values.
    /// </summary>
    public static TheoryData<string, string> ListMarkerCases => new()
    {
        { "- one\n* two\n+ three", "one two three" },
        { "  - one\r\n\t+ two\r\n    * three", "one two three" },
        { "-one\n+two\n*three", "-one +two three" },
        { "- - item", "- item" },
        { "- \nNext", "Next" },
        { "\n\n  - item", "item" },
        { "1. one\n22. two\n١. three", "one two three" },
        { "  001. padded", "padded" },
        { "1.one\n1) two", "1.one 1) two" },
        { "1. 2. nested", "2. nested" },
    };

    /// <summary>
    /// Gets supplementary and BMP Unicode inputs and their exact sanitized values.
    /// </summary>
    public static TheoryData<string, string> UnicodeCases => new()
    {
        { "A\U0001F600B", "AB" },
        { "A\U0001D11EB", "AB" },
        { "A\U00010437B", "AB" },
        { "\U0001D7D8. item", ". item" },
        { "A\U0001F600\U0001D11E\U00010437B", "AB" },
        { "A\uD83DB", "A\uD83DB" },
        { "A\uDE00B", "A\uDE00B" },
        { "A\uDE00\uD83DB", "A\uDE00\uD83DB" },
        { "A\uD83D\uD83D\uDE00B", "A\uD83DB" },
        { "A\u2600B\u27BFC", "ABC" },
        { "A\u25FFB\u27C0C", "A\u25FFB\u27C0C" },
        { "A\uFE00B\uFE0FC", "ABC" },
        { "A\u200DB", "AB" },
        { "©\uFE0F", "©" },
        { "1\uFE0F\u20E3", "1\u20E3" },
    };

    /// <summary>
    /// Gets whitespace and final trimming inputs and their exact sanitized values.
    /// </summary>
    public static TheoryData<string, string> WhitespaceAndTrimCases => new()
    {
        {
            "alpha\tbeta\r\ngamma\vdelta\fomega\u0085x\u00A0y\u1680z\u2000q\u2028r\u2029s\u3000t",
            "alpha beta gamma delta omega x y z q r s t"
        },
        { "alpha\rbravo\n\ncharlie\r\ndelta", "alpha bravo charlie delta" },
        { "  \talpha \r\n ", "alpha" },
        { "`code`", string.Empty },
        { "![image](url)", string.Empty },
        { " alpha\u00A0beta ", "alpha beta" },
        { "alpha\u200Bbeta", "alpha\u200Bbeta" },
    };

    /// <summary>
    /// Gets mixed markdown and ordered-pipeline inputs and their exact sanitized values.
    /// </summary>
    public static TheoryData<string, string> MixedMarkdownCases => new()
    {
        {
            "# Hello **world** [docs](url) ![img](x) \U0001F600\n- Item with `code`\n---\nDone",
            "Hello world docs Item with Done"
        },
        { "```![image](url) **bold** `code` ``` after", "after" },
        { "`![image](url) **bold**` after", "after" },
        { "# ![alt](url) [**label**](x)", "label" },
        { "## - item", "item" },
        { "1. # item", "# item" },
        { "- # item", "# item" },
        { "![alt](url)# heading", "# heading" },
        { "[](url)# heading", "heading" },
        { "```code```# heading", "# heading" },
        { "`code`# heading", "# heading" },
        { "#\U0001F600 heading", "# heading" },
        { "-\u200D item", "- item" },
    };

    /// <summary>
    /// Gets representative inputs whose sanitized values are stable across repeated calls.
    /// </summary>
    public static TheoryData<string> IdempotentCases => new()
    {
        { "Plain speech text." },
        { "# Heading\n\nA **bold** [link](url)." },
        { "Before ```code``` after `inline`." },
        { "- first\n+ second\n1. third" },
        { "Emoji \U0001F600 and symbols \u2600 are removed." },
        { "alpha\tbeta\r\ngamma" },
        { "# Hello **world** [docs](url) ![img](x) \U0001F600\n- Item with `code`\n---\nDone" },
    };

    /// <summary>
    /// Gets inputs where the ordered legacy pipeline intentionally exposes work for a later call.
    /// </summary>
    public static TheoryData<string, string, string> RepeatedCallOrderingCases => new()
    {
        { "- - item", "- item", "item" },
        { "1. 2. item", "2. item", "item" },
        { "## # heading", "# heading", "heading" },
        { "1. # heading", "# heading", "heading" },
        { "![alt](url)# heading", "# heading", "heading" },
        { "```code```# heading", "# heading", "heading" },
        { "#\U0001F600 heading", "# heading", "heading" },
        { "-\u200D item", "- item", "item" },
    };

    /// <summary>
    /// Verifies blank values are returned without changing their object identity.
    /// </summary>
    /// <param name="text">The blank input.</param>
    [Theory]
    [MemberData(nameof(BlankInputs))]
    public void Sanitize_ReturnsOriginalBlankInput(string text)
    {
        var result = SpeechTextSanitizer.Sanitize(text);

        Assert.Same(text, result);
    }

    /// <summary>
    /// Verifies exact fenced code block behavior.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="expected">The expected sanitized text.</param>
    [Theory]
    [MemberData(nameof(FencedCodeBlockCases))]
    public void Sanitize_PreservesFencedCodeBlockSemantics(string text, string expected)
    {
        AssertLegacyAndProduction(text, expected);
    }

    /// <summary>
    /// Verifies exact inline code behavior.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="expected">The expected sanitized text.</param>
    [Theory]
    [MemberData(nameof(InlineCodeCases))]
    public void Sanitize_PreservesInlineCodeSemantics(string text, string expected)
    {
        AssertLegacyAndProduction(text, expected);
    }

    /// <summary>
    /// Verifies exact markdown image and link behavior.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="expected">The expected sanitized text.</param>
    [Theory]
    [MemberData(nameof(ImageAndLinkCases))]
    public void Sanitize_PreservesImageAndLinkSemantics(string text, string expected)
    {
        AssertLegacyAndProduction(text, expected);
    }

    /// <summary>
    /// Verifies exact emphasis marker behavior.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="expected">The expected sanitized text.</param>
    [Theory]
    [MemberData(nameof(EmphasisMarkerCases))]
    public void Sanitize_PreservesEmphasisMarkerSemantics(string text, string expected)
    {
        AssertLegacyAndProduction(text, expected);
    }

    /// <summary>
    /// Verifies exact heading and horizontal rule behavior.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="expected">The expected sanitized text.</param>
    [Theory]
    [MemberData(nameof(HeadingAndHorizontalRuleCases))]
    public void Sanitize_PreservesHeadingAndHorizontalRuleSemantics(string text, string expected)
    {
        AssertLegacyAndProduction(text, expected);
    }

    /// <summary>
    /// Verifies exact list marker behavior.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="expected">The expected sanitized text.</param>
    [Theory]
    [MemberData(nameof(ListMarkerCases))]
    public void Sanitize_PreservesListMarkerSemantics(string text, string expected)
    {
        AssertLegacyAndProduction(text, expected);
    }

    /// <summary>
    /// Verifies exact supplementary and BMP Unicode removal behavior.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="expected">The expected sanitized text.</param>
    [Theory]
    [MemberData(nameof(UnicodeCases))]
    public void Sanitize_PreservesUnicodeRemovalSemantics(string text, string expected)
    {
        AssertLegacyAndProduction(text, expected);
    }

    /// <summary>
    /// Verifies .NET regular-expression whitespace collapsing and final trimming behavior.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="expected">The expected sanitized text.</param>
    [Theory]
    [MemberData(nameof(WhitespaceAndTrimCases))]
    public void Sanitize_PreservesWhitespaceAndTrimSemantics(string text, string expected)
    {
        AssertLegacyAndProduction(text, expected);
    }

    /// <summary>
    /// Verifies mixed markdown and cross-pass ordering behavior.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="expected">The expected sanitized text.</param>
    [Theory]
    [MemberData(nameof(MixedMarkdownCases))]
    public void Sanitize_PreservesMixedMarkdownAndOrderingSemantics(string text, string expected)
    {
        AssertLegacyAndProduction(text, expected);
    }

    /// <summary>
    /// Verifies representative sanitized outputs remain unchanged when sanitized again.
    /// </summary>
    /// <param name="text">The source text.</param>
    [Theory]
    [MemberData(nameof(IdempotentCases))]
    public void Sanitize_IsIdempotentForRepresentativeInputs(string text)
    {
        var once = SpeechTextSanitizer.Sanitize(text);
        var twice = SpeechTextSanitizer.Sanitize(once);

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// Verifies repeated calls preserve the legacy ordered pipeline, including exposed-marker edge cases.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="expectedOnce">The expected first-pass value.</param>
    /// <param name="expectedTwice">The expected second-pass value.</param>
    [Theory]
    [MemberData(nameof(RepeatedCallOrderingCases))]
    public void Sanitize_PreservesRepeatedCallOrderingSemantics(
        string text,
        string expectedOnce,
        string expectedTwice)
    {
        var legacyOnce = SanitizeLegacy(text);
        var productionOnce = SpeechTextSanitizer.Sanitize(text);
        var legacyTwice = SanitizeLegacy(legacyOnce);
        var productionTwice = SpeechTextSanitizer.Sanitize(productionOnce);

        Assert.Equal(expectedOnce, legacyOnce);
        Assert.Equal(expectedOnce, productionOnce);
        Assert.Equal(expectedTwice, legacyTwice);
        Assert.Equal(expectedTwice, productionTwice);
    }

    /// <summary>
    /// Differentially verifies interacting markdown, Unicode, whitespace, and line-ending combinations.
    /// </summary>
    [Fact]
    public void Sanitize_MatchesLegacyRegexPipelineAcrossCompatibilityMatrix()
    {
        string[] prefixes =
        [
            string.Empty,
            "plain ",
            "# ",
            "## ",
            "- ",
            "  + ",
            "1. ",
            "![prefix](url)",
            "`prefix`",
            "```prefix```",
        ];
        string[] leftFragments =
        [
            "text",
            "`code`",
            "``",
            "![alt](url)",
            "[label](url)",
            "**bold**",
            "_italic_",
            "---",
            "\U0001F600",
            "\U0001D11E",
        ];
        string[] separators =
        [
            string.Empty,
            " ",
            "  ",
            "\t",
            "\n",
            "\r\n",
            "\u00A0",
            "\u200D",
        ];
        string[] rightFragments =
        [
            "tail",
            "`code`",
            "![alt](url)",
            "[label](url)",
            "***",
            "# heading",
            "- item",
            "2. item",
            "\u2600",
            "\U0001F600",
        ];
        string[] suffixes =
        [
            string.Empty,
            ".",
            " ",
            "\nend",
            "\r\n---",
            "```",
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
                            var text = string.Concat(
                                prefix,
                                leftFragment,
                                separator,
                                rightFragment,
                                suffix);

                            Assert.Equal(
                                SanitizeLegacy(text),
                                SpeechTextSanitizer.Sanitize(text));
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Differentially verifies every BMP code unit both as prose and as a possible ordered-list marker.
    /// </summary>
    [Fact]
    public void Sanitize_MatchesLegacyRegexPipelineForEveryBmpCodeUnit()
    {
        for (var value = 0; value <= char.MaxValue; value++)
        {
            var character = (char)value;
            var prose = string.Concat("A", character, "B");
            var possibleOrderedMarker = string.Concat(character, ". item");

            Assert.Equal(
                SanitizeLegacy(prose),
                SpeechTextSanitizer.Sanitize(prose));
            Assert.Equal(
                SanitizeLegacy(possibleOrderedMarker),
                SpeechTextSanitizer.Sanitize(possibleOrderedMarker));
        }
    }

    /// <summary>
    /// Verifies the expected value against both the captured legacy pipeline and production.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="expected">The expected sanitized text.</param>
    private static void AssertLegacyAndProduction(string text, string expected)
    {
        var legacyResult = SanitizeLegacy(text);
        var productionResult = SpeechTextSanitizer.Sanitize(text);

        Assert.Equal(expected, legacyResult);
        Assert.Equal(legacyResult, productionResult);
    }

    /// <summary>
    /// Executes the original ordered source-generated regular-expression pipeline exactly.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <returns>The legacy sanitized text.</returns>
    private static string SanitizeLegacy(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        text = LegacyFencedCodeBlockPattern().Replace(text, " ");
        text = LegacyInlineCodePattern().Replace(text, " ");
        text = LegacyMarkdownImagePattern().Replace(text, " ");
        text = LegacyMarkdownLinkPattern().Replace(text, "$1");
        text = LegacyBoldItalicMarkerPattern().Replace(text, string.Empty);
        text = LegacyHeadingMarkerPattern().Replace(text, string.Empty);
        text = LegacyHorizontalRulePattern().Replace(text, string.Empty);
        text = LegacyUnorderedListMarkerPattern().Replace(text, string.Empty);
        text = LegacyOrderedListMarkerPattern().Replace(text, string.Empty);
        text = LegacyEmojiSurrogatePairPattern().Replace(text, string.Empty);
        text = LegacyBmpEmojiSymbolPattern().Replace(text, string.Empty);
        text = LegacyMultipleWhitespacePattern().Replace(text, " ");

        return text.Trim();
    }

    /// <summary>
    /// Gets the legacy fenced code block regular expression.
    /// </summary>
    [GeneratedRegex(@"```[\s\S]*?```")]
    private static partial Regex LegacyFencedCodeBlockPattern();

    /// <summary>
    /// Gets the legacy inline code regular expression.
    /// </summary>
    [GeneratedRegex(@"`[^`]+`")]
    private static partial Regex LegacyInlineCodePattern();

    /// <summary>
    /// Gets the legacy markdown image regular expression.
    /// </summary>
    [GeneratedRegex(@"!\[[^\]]*\]\([^\)]*\)")]
    private static partial Regex LegacyMarkdownImagePattern();

    /// <summary>
    /// Gets the legacy markdown link regular expression.
    /// </summary>
    [GeneratedRegex(@"\[([^\]]*)\]\([^\)]*\)")]
    private static partial Regex LegacyMarkdownLinkPattern();

    /// <summary>
    /// Gets the legacy bold and italic marker regular expression.
    /// </summary>
    [GeneratedRegex(@"\*{1,3}|_{1,3}")]
    private static partial Regex LegacyBoldItalicMarkerPattern();

    /// <summary>
    /// Gets the legacy heading marker regular expression.
    /// </summary>
    [GeneratedRegex(@"^#{1,6}\s+", RegexOptions.Multiline)]
    private static partial Regex LegacyHeadingMarkerPattern();

    /// <summary>
    /// Gets the legacy horizontal rule regular expression.
    /// </summary>
    [GeneratedRegex(@"^[-*_]{3,}\s*$", RegexOptions.Multiline)]
    private static partial Regex LegacyHorizontalRulePattern();

    /// <summary>
    /// Gets the legacy unordered list marker regular expression.
    /// </summary>
    [GeneratedRegex(@"^\s*[-*+]\s+", RegexOptions.Multiline)]
    private static partial Regex LegacyUnorderedListMarkerPattern();

    /// <summary>
    /// Gets the legacy ordered list marker regular expression.
    /// </summary>
    [GeneratedRegex(@"^\s*\d+\.\s+", RegexOptions.Multiline)]
    private static partial Regex LegacyOrderedListMarkerPattern();

    /// <summary>
    /// Gets the legacy supplementary surrogate-pair regular expression.
    /// </summary>
    [GeneratedRegex(@"[\uD800-\uDBFF][\uDC00-\uDFFF]")]
    private static partial Regex LegacyEmojiSurrogatePairPattern();

    /// <summary>
    /// Gets the legacy BMP symbol regular expression.
    /// </summary>
    [GeneratedRegex(@"[\u2600-\u27BF\uFE00-\uFE0F\u200D]")]
    private static partial Regex LegacyBmpEmojiSymbolPattern();

    /// <summary>
    /// Gets the legacy whitespace regular expression.
    /// </summary>
    [GeneratedRegex(@"\s+")]
    private static partial Regex LegacyMultipleWhitespacePattern();
}
