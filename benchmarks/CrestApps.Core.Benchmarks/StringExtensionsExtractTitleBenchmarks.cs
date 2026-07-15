using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.Support;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured string-then-trim title extraction with the production implementation.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class StringExtensionsExtractTitleBenchmarks
{
    private string _content;

    /// <summary>
    /// Gets or sets the title extraction scenario.
    /// </summary>
    [Params(
        "ShortCleanTitle",
        "PaddedTitle",
        "EarlyNewline1KB",
        "EarlyNewline1MB",
        "SingleLine1MB",
        "Exactly200",
        "Over200",
        "UnicodeWhitespace")]
    public string Scenario { get; set; }

    /// <summary>
    /// Creates the scenario input and verifies exact legacy and production equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _content = CreateContent(Scenario);

        var legacy = ExtractTitleLegacy(_content);
        var production = _content.ExtractTitleFromContent();

        if (!string.Equals(legacy, production, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The production title extraction changed legacy semantics.");
        }
    }

    /// <summary>
    /// Extracts the title with the captured string-then-trim implementation.
    /// </summary>
    /// <returns>The extracted title.</returns>
    [Benchmark(Baseline = true)]
    public string ExtractTitleLegacyBenchmark()
    {
        return ExtractTitleLegacy(_content);
    }

    /// <summary>
    /// Extracts the title with the production implementation.
    /// </summary>
    /// <returns>The extracted title.</returns>
    [Benchmark]
    public string ExtractTitleProductionBenchmark()
    {
        return _content.ExtractTitleFromContent();
    }

    /// <summary>
    /// Creates content for the requested benchmark scenario.
    /// </summary>
    /// <param name="scenario">The scenario name.</param>
    /// <returns>The benchmark content.</returns>
    private static string CreateContent(string scenario)
    {
        return scenario switch
        {
            "ShortCleanTitle" => "Short clean title",
            "PaddedTitle" => " \t  Padded title  \u2003 ",
            "EarlyNewline1KB" => CreateEarlyNewlineContent(1_024),
            "EarlyNewline1MB" => CreateEarlyNewlineContent(1_048_576),
            "SingleLine1MB" => new string('s', 1_048_576),
            "Exactly200" => new string('e', 200),
            "Over200" => new string('o', 201),
            "UnicodeWhitespace" => "\u00A0\u2003Unicode title\u2028\u2029",
            _ => throw new InvalidOperationException($"Unknown scenario '{scenario}'."),
        };
    }

    /// <summary>
    /// Creates exact-length content with a title followed by an early line feed.
    /// </summary>
    /// <param name="length">The total content length.</param>
    /// <returns>The generated content.</returns>
    private static string CreateEarlyNewlineContent(int length)
    {
        const string Prefix = "Early title\n";

        return Prefix + new string('b', length - Prefix.Length);
    }

    /// <summary>
    /// Preserves the original string-then-trim title extraction implementation.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <returns>The extracted title.</returns>
    private static string ExtractTitleLegacy(string content)
    {
        var firstLine = content.AsSpan();
        var newlineIndex = firstLine.IndexOfAny('\r', '\n');

        if (newlineIndex > 0)
        {
            firstLine = firstLine[..newlineIndex];
        }

        if (firstLine.Length > 200)
        {
            firstLine = firstLine[..200];
        }

        return firstLine.ToString().Trim();
    }
}
