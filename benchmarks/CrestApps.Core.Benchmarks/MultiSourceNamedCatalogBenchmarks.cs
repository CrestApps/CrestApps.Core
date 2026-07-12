using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures cached multi-source catalog filtering and paging.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class MultiSourceNamedCatalogBenchmarks
{
    private BenchmarkCatalog _catalog;
    private IReadOnlyCollection<BenchmarkEntry> _entries;

    /// <summary>
    /// Creates and warms a 10,000-entry catalog.
    /// </summary>
    [GlobalSetup]
    public async Task Setup()
    {
        var entries = Enumerable.Range(0, 10_000)
            .Select(index => new BenchmarkEntry($"entry-{index}", $"Entry {index:D5}"))
            .ToArray();
        _catalog = new BenchmarkCatalog([new BenchmarkSource(entries)]);
        _entries = await _catalog.GetAllAsync();
    }

    /// <summary>
    /// Pages by materializing every entry before slicing.
    /// </summary>
    /// <returns>The number of entries in the requested page.</returns>
    [Benchmark(Baseline = true)]
    public int PageBuffered()
    {
        var filtered = _entries.ToList();
        const int skip = (50 - 1) * 50;
        var entries = filtered.Skip(skip).Take(50).ToArray();

        return entries.Length;
    }

    /// <summary>
    /// Pages with the current catalog implementation.
    /// </summary>
    /// <returns>The number of entries in the requested page.</returns>
    [Benchmark]
    public async ValueTask<int> PageOptimizedAsync()
    {
        var page = await _catalog.PageAsync(50, 50, new QueryContext());

        return page.Entries.Count;
    }

    private sealed record BenchmarkEntry(string Id, string Name) : INameAwareModel;

    private sealed class BenchmarkCatalog : MultiSourceNamedCatalog<BenchmarkEntry>
    {
        public BenchmarkCatalog(IEnumerable<INamedCatalogSource<BenchmarkEntry>> sources)
            : base(sources)
        {
        }

        protected override string GetItemId(BenchmarkEntry entry)
        {
            return entry.Id;
        }
    }

    private sealed class BenchmarkSource : INamedCatalogSource<BenchmarkEntry>
    {
        private readonly IReadOnlyCollection<BenchmarkEntry> _entries;

        public BenchmarkSource(IReadOnlyCollection<BenchmarkEntry> entries)
        {
            _entries = entries;
        }

        public int Order => 0;

        public ValueTask<IReadOnlyCollection<BenchmarkEntry>> GetEntriesAsync(
            IReadOnlyCollection<BenchmarkEntry> knownEntries,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_entries);
        }
    }
}
