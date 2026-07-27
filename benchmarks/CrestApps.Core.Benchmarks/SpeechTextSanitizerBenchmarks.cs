using System.Text;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Services;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the original ordered regular-expression speech sanitizer with the production implementation.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public partial class SpeechTextSanitizerBenchmarks
{
    private string _input;

    /// <summary>
    /// Gets or sets the sanitizer input scenario.
    /// </summary>
    [Params(
        "PlainText",
        "ChatChunk200Bytes",
        "MixedMarkdown2Kb",
        "Transcript20Kb",
        "CodeHeavy",
        "EmojiHeavy",
        "WhitespaceHeavy")]
    public string Scenario { get; set; }

    /// <summary>
    /// Creates and verifies the selected benchmark input.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _input = Scenario switch
        {
            "PlainText" => "The customer asked for a concise deployment update, and the assistant answered clearly.",
            "ChatChunk200Bytes" => CreateChatChunk(),
            "MixedMarkdown2Kb" => RepeatAsciiToLength(
                "# Release update\n- Read the [deployment guide](https://example.test/docs).\n" +
                "- Ignore `internal_token` and ![status](status.png).\n" +
                "```csharp\nservices.AddFeature();\n```\n" +
                "**Result:** the rollout is healthy.\n\n",
                2 * 1024),
            "Transcript20Kb" => RepeatAsciiToLength(
                "Customer: Can you summarize the rollout status?\r\n" +
                "Assistant: The deployment is healthy, latency is stable, and no action is required.\r\n\r\n",
                20 * 1024),
            "CodeHeavy" => RepeatAsciiToLength(
                "Before the example. ```csharp\npublic static void Run()\n{\n    Console.WriteLine(\"ready\");\n}\n``` " +
                "Use `dotnet test` and then review ``literal ticks``. After the example.\n",
                20 * 1024),
            "EmojiHeavy" => string.Concat(Enumerable.Repeat(
                "Status \U0001F600\U0001F680\U0001F9E0 \u2600\uFE0F ©\uFE0F " +
                "music \U0001D11E letter \U00010437 joined \U0001F469\u200D\U0001F4BB. ",
                640)),
            "WhitespaceHeavy" => string.Concat(Enumerable.Repeat(
                "alpha \t\r\n\v\f\u0085\u00A0\u1680\u2000\u2028\u2029\u3000 beta\r\n\r\n",
                640)),
            _ => throw new InvalidOperationException($"Unknown scenario '{Scenario}'."),
        };

        if (Scenario == "ChatChunk200Bytes" && Encoding.UTF8.GetByteCount(_input) != 200)
        {
            throw new InvalidOperationException("The chat chunk benchmark input must be exactly 200 UTF-8 bytes.");
        }

        var legacyResult = SanitizeLegacy(_input);
        var productionResult = SpeechTextSanitizer.Sanitize(_input);

        if (!string.Equals(legacyResult, productionResult, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The legacy and production sanitizers produced different output.");
        }
    }

    /// <summary>
    /// Sanitizes the selected input with the captured legacy regular-expression pipeline.
    /// </summary>
    /// <returns>The sanitized text.</returns>
    [Benchmark(Baseline = true)]
    public string SanitizeLegacy()
    {
        return SanitizeLegacy(_input);
    }

    /// <summary>
    /// Sanitizes the selected input with the production implementation.
    /// </summary>
    /// <returns>The sanitized text.</returns>
    [Benchmark]
    public string SanitizeProduction()
    {
        return SpeechTextSanitizer.Sanitize(_input);
    }

    /// <summary>
    /// Creates a realistic ASCII chat chunk that is exactly 200 UTF-8 bytes.
    /// </summary>
    /// <returns>The chat chunk.</returns>
    private static string CreateChatChunk()
    {
        return RepeatAsciiToLength(
            "The rollout is healthy. See [details](https://example.test), skip `trace-id`, and continue. ",
            200);
    }

    /// <summary>
    /// Repeats and truncates an ASCII block to the requested length.
    /// </summary>
    /// <param name="block">The ASCII source block.</param>
    /// <param name="length">The required character and byte length.</param>
    /// <returns>The repeated input.</returns>
    private static string RepeatAsciiToLength(string block, int length)
    {
        var builder = new StringBuilder(length);

        while (builder.Length + block.Length <= length)
        {
            builder.Append(block);
        }

        if (builder.Length < length)
        {
            builder.Append(block.AsSpan(0, length - builder.Length));
        }

        return builder.ToString();
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
