using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.Support;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured replace-chain log sanitization with the production implementation.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class StringExtensionsSanitizeForLogBenchmarks
{
    private string _value;

    /// <summary>
    /// Gets or sets the log sanitization scenario.
    /// </summary>
    [Params(
        "CleanShort",
        "LineFeedOnly",
        "CarriageReturnOnly",
        "MixedShort",
        "Mixed1KB",
        "Mixed100KB")]
    public string Scenario { get; set; }

    /// <summary>
    /// Creates the scenario input and verifies exact legacy and production equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _value = CreateValue(Scenario);

        var legacy = SanitizeForLogLegacy(_value);
        var production = _value.SanitizeForLog();

        if (!string.Equals(legacy, production, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The production log sanitization changed legacy semantics.");
        }
    }

    /// <summary>
    /// Sanitizes log content with the captured replace-chain implementation.
    /// </summary>
    /// <returns>The sanitized log content.</returns>
    [Benchmark(Baseline = true)]
    public string SanitizeForLogLegacyBenchmark()
    {
        return SanitizeForLogLegacy(_value);
    }

    /// <summary>
    /// Sanitizes log content with the production implementation.
    /// </summary>
    /// <returns>The sanitized log content.</returns>
    [Benchmark]
    public string SanitizeForLogProductionBenchmark()
    {
        return _value.SanitizeForLog();
    }

    /// <summary>
    /// Creates log content for the requested benchmark scenario.
    /// </summary>
    /// <param name="scenario">The scenario name.</param>
    /// <returns>The benchmark log content.</returns>
    private static string CreateValue(string scenario)
    {
        return scenario switch
        {
            "CleanShort" => "SearchIndexProfile Provider-01 completed successfully.",
            "LineFeedOnly" => CreateRepeatedValue("provider\nindex-name\n", 64),
            "CarriageReturnOnly" => CreateRepeatedValue("provider\rindex-name\r", 64),
            "MixedShort" => "provider\r\nindex-name\ncomplete\r",
            "Mixed1KB" => CreateRepeatedValue("provider\r\nindex-name\ncomplete\r", 38),
            "Mixed100KB" => CreateRepeatedValue("provider\r\nindex-name\ncomplete\r", 3_795),
            _ => throw new InvalidOperationException($"Unknown scenario '{scenario}'."),
        };
    }

    /// <summary>
    /// Creates a repeated log value.
    /// </summary>
    /// <param name="value">The value to repeat.</param>
    /// <param name="count">The repeat count.</param>
    /// <returns>The repeated log value.</returns>
    private static string CreateRepeatedValue(string value, int count)
    {
        var builder = new StringBuilder(value.Length * count);

        for (var i = 0; i < count; i++)
        {
            builder.Append(value);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Preserves the original replace-chain log sanitization implementation.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The sanitized log content.</returns>
    private static string SanitizeForLogLegacy(string value)
    {
        return value?.Replace("\r", string.Empty).Replace("\n", string.Empty) ?? string.Empty;
    }
}
