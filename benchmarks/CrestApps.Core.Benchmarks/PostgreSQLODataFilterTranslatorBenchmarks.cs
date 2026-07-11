using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures PostgreSQL OData filter translation.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public partial class PostgreSQLODataFilterTranslatorBenchmarks
{
    private string _filter;

    /// <summary>
    /// Gets or sets the filter scenario.
    /// </summary>
    [Params("Short", "Nested", "Long")]
    public string Scenario { get; set; }

    /// <summary>
    /// Selects the representative filter expression.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _filter = Scenario switch
        {
            "Short" => "category eq 'news'",
            "Nested" => "(category eq 'news' or category eq 'blog') and not status eq 'draft'",
            _ => string.Join(
                " or ",
                Enumerable.Range(0, 20).Select(index => $"contains(filters.tags, 'tag-{index}')")),
        };
    }

    /// <summary>
    /// Tokenizes the representative filter with allocated regular-expression matches.
    /// </summary>
    /// <returns>The filter tokens.</returns>
    [Benchmark(Baseline = true)]
    public List<string> TokenizeMatches()
    {
        var tokens = new List<string>();

        foreach (Match match in TokenRegex().Matches(_filter))
        {
            tokens.Add(match.Value);
        }

        return tokens;
    }

    /// <summary>
    /// Tokenizes the representative filter with allocation-free match enumeration.
    /// </summary>
    /// <returns>The filter tokens.</returns>
    [Benchmark]
    public List<string> TokenizeEnumerateMatches()
    {
        var tokens = new List<string>();

        foreach (var match in TokenRegex().EnumerateMatches(_filter))
        {
            tokens.Add(_filter.Substring(match.Index, match.Length));
        }

        return tokens;
    }

    [GeneratedRegex(@"'[^']*'|[(),]|\w[\w.]*")]
    private static partial Regex TokenRegex();
}
