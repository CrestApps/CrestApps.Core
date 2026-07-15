using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures the extracted-data store's detached value-dictionary copy, comparing the legacy
/// LINQ <c>ToDictionary</c> projection against a pre-sized ordinal-ignore-case loop.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class YesSqlAIChatSessionExtractedDataStoreValueCopyBenchmarks
{
    private Dictionary<string, List<string>> _values;

    /// <summary>
    /// Gets or sets the number of extracted field entries.
    /// </summary>
    [Params(10, 100, 1000)]
    public int FieldCount { get; set; }

    /// <summary>
    /// Builds a source dictionary with populated, empty, and null value lists and mixed-case keys.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _values = new Dictionary<string, List<string>>(FieldCount, StringComparer.Ordinal);

        for (var index = 0; index < FieldCount; index++)
        {
            var name = index % 2 == 0
                ? $"field-{index:D4}"
                : $"Field-{index:D4}";

            List<string> values;

            if (index % 7 == 0)
            {
                values = null;
            }
            else if (index % 5 == 0)
            {
                values = [];
            }
            else
            {
                values =
                [
                    $"value-{index:D4}-a",
                    $"value-{index:D4}-b",
                    $"value-{index:D4}-c",
                ];
            }

            _values.Add(name, values);
        }

        EnsureEquivalent(CopyWithToDictionary(), CopyWithPreSizedLoop());
    }

    /// <summary>
    /// Copies the values using the production LINQ projection captured before the experiment.
    /// </summary>
    /// <returns>The detached value dictionary.</returns>
    [Benchmark(Baseline = true)]
    public Dictionary<string, List<string>> CopyWithToDictionary()
    {
        return _values.ToDictionary(
            pair => pair.Key,
            pair => pair.Value?.ToList() ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Copies the values using a pre-sized ordinal-ignore-case loop over the struct enumerator.
    /// </summary>
    /// <returns>The detached value dictionary.</returns>
    [Benchmark]
    public Dictionary<string, List<string>> CopyWithPreSizedLoop()
    {
        var copy = new Dictionary<string, List<string>>(_values.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in _values)
        {
            var values = pair.Value;
            copy.Add(pair.Key, values is null ? [] : [.. values]);
        }

        return copy;
    }

    /// <summary>
    /// Verifies both copy implementations return identical keys, ordering, and per-key values.
    /// </summary>
    /// <param name="legacy">The LINQ projection result.</param>
    /// <param name="candidate">The pre-sized loop result.</param>
    private static void EnsureEquivalent(
        Dictionary<string, List<string>> legacy,
        Dictionary<string, List<string>> candidate)
    {
        if (legacy.Count != candidate.Count)
        {
            throw new InvalidOperationException("Value copy implementations returned different counts.");
        }

        using var legacyEnumerator = legacy.GetEnumerator();
        using var candidateEnumerator = candidate.GetEnumerator();

        while (legacyEnumerator.MoveNext() && candidateEnumerator.MoveNext())
        {
            var legacyPair = legacyEnumerator.Current;
            var candidatePair = candidateEnumerator.Current;

            if (!string.Equals(legacyPair.Key, candidatePair.Key, StringComparison.Ordinal) ||
                legacyPair.Value.Count != candidatePair.Value.Count)
            {
                throw new InvalidOperationException("Value copy implementations returned different entries.");
            }

            for (var index = 0; index < legacyPair.Value.Count; index++)
            {
                if (!string.Equals(legacyPair.Value[index], candidatePair.Value[index], StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Value copy implementations returned different values.");
                }
            }
        }
    }
}
