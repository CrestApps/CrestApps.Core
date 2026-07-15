using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures the extracted-data index mapping's legacy double sort against one shared ordered pass.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class AIChatSessionExtractedDataIndexBenchmarks
{
    private AIChatSessionExtractedDataRecord _record;

    /// <summary>
    /// Gets or sets the number of extracted fields.
    /// </summary>
    [Params(10, 100, 1000)]
    public int FieldCount { get; set; }

    /// <summary>
    /// Creates fields with multiple values, duplicate values, and case-only key variants.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var values = new Dictionary<string, List<string>>(FieldCount, StringComparer.Ordinal);

        for (var index = FieldCount - 1; index >= 0; index--)
        {
            var pairIndex = index / 2;
            var name = index % 2 == 0
                ? $"field-{pairIndex:D4}"
                : $"FIELD-{pairIndex:D4}";

            values.Add(
                name,
                [
                    $"value-{index:D4}",
                    string.Empty,
                    $"VALUE-{index:D4}",
                    $"value-{index:D4}",
                ]);
        }

        _record = new AIChatSessionExtractedDataRecord
        {
            SessionId = "session-1",
            ProfileId = "profile-1",
            SessionStartedUtc = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            SessionEndedUtc = new DateTime(2026, 5, 1, 12, 5, 0, DateTimeKind.Utc),
            Values = values,
            UpdatedUtc = new DateTime(2026, 5, 1, 12, 6, 0, DateTimeKind.Utc),
        };

        EnsureEquivalent(MapWithDoubleSort(), MapWithSingleSort());
    }

    /// <summary>
    /// Maps the record using the production implementation captured before the experiment.
    /// </summary>
    /// <returns>The mapped index.</returns>
    [Benchmark(Baseline = true)]
    public AIChatSessionExtractedDataIndex MapWithDoubleSort()
    {
        var fieldNames = _record.Values.Keys
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var valuesText = string.Join(
            '\n',
            _record.Values
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .SelectMany(pair => pair.Value.Select(value => $"{pair.Key}:{value}")));

        return CreateIndex(fieldNames, valuesText);
    }

    /// <summary>
    /// Maps the record by sorting field names once and sharing that order between outputs.
    /// </summary>
    /// <returns>The mapped index.</returns>
    [Benchmark]
    public AIChatSessionExtractedDataIndex MapWithSingleSort()
    {
        var fieldNames = _record.Values.Keys
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var valuesText = string.Join(
            '\n',
            fieldNames.SelectMany(
                name => _record.Values[name].Select(value => $"{name}:{value}")));

        return CreateIndex(fieldNames, valuesText);
    }

    /// <summary>
    /// Creates an index using the mapped field-name and value text sequences.
    /// </summary>
    /// <param name="fieldNames">The ordered field names.</param>
    /// <param name="valuesText">The flattened field values.</param>
    /// <returns>The mapped index.</returns>
    private AIChatSessionExtractedDataIndex CreateIndex(
        IEnumerable<string> fieldNames,
        string valuesText)
    {
        return new AIChatSessionExtractedDataIndex
        {
            SessionId = _record.SessionId,
            ProfileId = _record.ProfileId,
            SessionStartedUtc = _record.SessionStartedUtc,
            SessionEndedUtc = _record.SessionEndedUtc,
            FieldCount = _record.Values.Count,
            FieldNames = string.Join('|', fieldNames),
            ValuesText = valuesText,
            UpdatedUtc = _record.UpdatedUtc,
        };
    }

    /// <summary>
    /// Verifies that both mapping implementations produce identical index values.
    /// </summary>
    /// <param name="legacy">The legacy mapping result.</param>
    /// <param name="candidate">The single-sort mapping result.</param>
    private static void EnsureEquivalent(
        AIChatSessionExtractedDataIndex legacy,
        AIChatSessionExtractedDataIndex candidate)
    {
        if (legacy.SessionId != candidate.SessionId ||
            legacy.ProfileId != candidate.ProfileId ||
            legacy.SessionStartedUtc != candidate.SessionStartedUtc ||
            legacy.SessionEndedUtc != candidate.SessionEndedUtc ||
            legacy.FieldCount != candidate.FieldCount ||
            legacy.FieldNames != candidate.FieldNames ||
            legacy.ValuesText != candidate.ValuesText ||
            legacy.UpdatedUtc != candidate.UpdatedUtc)
        {
            throw new InvalidOperationException("Index mapping implementations returned different values.");
        }
    }
}
