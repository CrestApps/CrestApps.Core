using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.Azure.AISearch.Services;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the legacy and current Azure AI Search AI data source <c>ReadByIds</c> OData filter
/// construction. The identifiers represent the already whitespace-filtered and case-insensitively
/// de-duplicated array that <c>AzureAISearchAIDataSourceSourceHandler.ReadByIdsAsync</c> passes to
/// filter construction, so the measured region isolates the step that changed: replacing the
/// per-identifier LINQ projection with the shared filter builder.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class AzureAISearchAIDataSourceReadByIdsFilterBenchmarks
{
    private const string KeyFieldName = "documentId";

    private string[] _documentIds;

    /// <summary>
    /// Gets or sets the number of document identifiers included in each filter.
    /// </summary>
    [Params(1, 10, 100, 1000)]
    public int DocumentIdCount { get; set; }

    /// <summary>
    /// Creates the prepared document identifiers, where every tenth identifier includes apostrophes
    /// to exercise OData escaping.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _documentIds = new string[DocumentIdCount];

        for (var index = 0; index < DocumentIdCount; index++)
        {
            _documentIds[index] = index % 10 == 0
                ? $"document-'quoted'-{index}"
                : $"document-{index}";
        }
    }

    /// <summary>
    /// Builds the OData filter with the original per-identifier LINQ projection implementation.
    /// </summary>
    /// <returns>The OData filter.</returns>
    [Benchmark(Baseline = true)]
    public string PrepareLegacy()
    {
        return string.Join(
            " or ",
            _documentIds.Select(id => $"{KeyFieldName} eq '{id.Replace("'", "''", StringComparison.Ordinal)}'"));
    }

    /// <summary>
    /// Builds the OData filter with the shared builder implementation.
    /// </summary>
    /// <returns>The OData filter.</returns>
    [Benchmark]
    public string PrepareCurrent()
    {
        return DataSourceAzureAISearchDocumentIdFilterBuilder.BuildFilter(_documentIds, KeyFieldName);
    }
}
