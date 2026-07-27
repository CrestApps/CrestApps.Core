using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.Support.Json;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the original regular-expression code-fence extraction with the production implementation.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class JsonExtractorBenchmarks
{
    private const string CodeFencePattern = @"```(?:json)?\s*\n?([\s\S]*?)\n?\s*```";

    private string _input;

    /// <summary>
    /// Gets or sets the input scenario.
    /// </summary>
    [Params(
        "ShortFencedJson",
        "Prose10KbWithFence",
        "Payload100Kb",
        "NoFence",
        "MultipleFences",
        "WhitespaceHeavy")]
    public string Scenario { get; set; }

    /// <summary>
    /// Creates the input for the selected benchmark scenario.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _input = Scenario switch
        {
            "ShortFencedJson" => "```json\n{\"name\":\"Ada\",\"active\":true}\n```",
            "Prose10KbWithFence" => CreateProseWithFence(10 * 1024),
            "Payload100Kb" => $"```json\n{{\"payload\":\"{new string('x', 100 * 1024)}\"}}\n```",
            "NoFence" => new string('n', 10 * 1024),
            "MultipleFences" => "prefix ```json\n{\"first\":1}\n``` middle ```json\n{\"second\":2}\n``` suffix",
            "WhitespaceHeavy" => CreateWhitespaceHeavyInput(),
            _ => throw new InvalidOperationException($"Unknown scenario '{Scenario}'."),
        };
    }

    /// <summary>
    /// Extracts code-fence content with the original regular-expression implementation.
    /// </summary>
    /// <returns>The extracted content, or <see langword="null"/> when no match exists.</returns>
    [Benchmark(Baseline = true)]
    public string ExtractLegacy()
    {
        if (string.IsNullOrWhiteSpace(_input))
        {
            return null;
        }

        var match = Regex.Match(
            _input,
            CodeFencePattern,
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Extracts code-fence content with the production implementation.
    /// </summary>
    /// <returns>The extracted content, or <see langword="null"/> when no match exists.</returns>
    [Benchmark]
    public string ExtractProduction()
    {
        return JsonExtractor.ExtractFromCodeFence(_input);
    }

    /// <summary>
    /// Creates an input of the requested size with prose surrounding one short JSON fence.
    /// </summary>
    /// <param name="length">The total input length.</param>
    /// <returns>The prose and code-fence input.</returns>
    private static string CreateProseWithFence(int length)
    {
        const string fence = "\n```json\n{\"value\":42}\n```\n";
        var proseLength = length - fence.Length;
        var prefixLength = proseLength / 2;

        return string.Concat(
            new string('p', prefixLength),
            fence,
            new string('s', proseLength - prefixLength));
    }

    /// <summary>
    /// Creates an input with large whitespace runs around the captured content.
    /// </summary>
    /// <returns>The whitespace-heavy code-fence input.</returns>
    private static string CreateWhitespaceHeavyInput()
    {
        var whitespace = string.Concat(Enumerable.Repeat(" \t\r\n", 1_280));

        return $"```json{whitespace}{{\"value\":42}}{whitespace}```";
    }
}
