using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.Azure.AISearch.Services;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares legacy and current Azure AI Search document identifier filter preparation.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class DataSourceAzureAISearchDocumentIdFilterBuilderBenchmarks
{
    private const string KeyFieldName = "documentId";

    private string[] _documentIds;

    /// <summary>
    /// Gets or sets the number of valid document identifiers included in each filter.
    /// </summary>
    [Params(1, 10, 100, 1000)]
    public int DocumentIdCount { get; set; }

    /// <summary>
    /// Creates document identifiers that exercise filtering and apostrophe escaping.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _documentIds = new string[DocumentIdCount + 2];

        for (var index = 0; index < DocumentIdCount; index++)
        {
            _documentIds[index] = index % 10 == 0
                ? $"document-'quoted'-{index}"
                : $"document-{index}";
        }

        _documentIds[^2] = null;
        _documentIds[^1] = string.Empty;
    }

    /// <summary>
    /// Filters identifiers and builds the OData filter with the original LINQ implementation.
    /// </summary>
    /// <returns>The OData filter.</returns>
    [Benchmark(Baseline = true)]
    public string PrepareLegacy()
    {
        var idList = _documentIds.Where(id => !string.IsNullOrEmpty(id)).ToList();

        return string.Join(
            " or ",
            idList.Select(id => $"{KeyFieldName} eq '{id.Replace("'", "''")}'"));
    }

    /// <summary>
    /// Filters identifiers and builds the OData filter with the production implementation.
    /// </summary>
    /// <returns>The OData filter.</returns>
    [Benchmark]
    public string PrepareCurrent()
    {
        var idList = DataSourceAzureAISearchDocumentIdFilterBuilder.FilterDocumentIds(_documentIds);

        return DataSourceAzureAISearchDocumentIdFilterBuilder.BuildFilter(idList, KeyFieldName);
    }
}
