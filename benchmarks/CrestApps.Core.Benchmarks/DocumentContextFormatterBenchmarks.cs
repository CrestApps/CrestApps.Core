using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures formatting document chunks when the requested context is much shorter than the document.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class DocumentContextFormatterBenchmarks
{
    private AIDocument _document;
    private BenchmarkDocumentChunkStore _store;
    private IServiceProvider _services;

    /// <summary>
    /// Creates a one-megabyte document split across unordered chunks.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var chunks = Enumerable.Range(0, 100)
            .Select(index => new AIDocumentChunk
            {
                AIDocumentId = "document",
                Index = 99 - index,
                Content = new string((char)('a' + index % 26), 10_000),
            })
            .ToArray();

        _document = new AIDocument
        {
            ItemId = "document",
            FileName = "document.txt",
        };
        _store = new BenchmarkDocumentChunkStore(chunks);
        _services = new ServiceCollection()
            .AddSingleton<IAIDocumentChunkStore>(_store)
            .BuildServiceProvider();
    }

    /// <summary>
    /// Formats the document by joining every chunk before truncation.
    /// </summary>
    /// <returns>The formatted document context.</returns>
    [Benchmark(Baseline = true)]
    public async Task<string> FormatBufferedAsync()
    {
        var chunks = await _store.GetChunksByAIDocumentIdAsync(_document.ItemId);
        var text = string.Join(Environment.NewLine, chunks.OrderBy(chunk => chunk.Index).Select(chunk => chunk.Content));

        return DocumentContextFormatter.FormatDocumentText(_document.FileName, text, 50_000);
    }

    /// <summary>
    /// Formats the document without joining content beyond the requested maximum length.
    /// </summary>
    /// <returns>The formatted document context.</returns>
    [Benchmark]
    public Task<string> FormatOptimizedAsync()
    {
        return DocumentContextFormatter.FormatDocumentTextFromChunksAsync(_services, _document, 50_000);
    }

    private sealed class BenchmarkDocumentChunkStore : IAIDocumentChunkStore
    {
        private readonly IReadOnlyCollection<AIDocumentChunk> _chunks;

        public BenchmarkDocumentChunkStore(IReadOnlyCollection<AIDocumentChunk> chunks)
        {
            _chunks = chunks;
        }

        public Task<IReadOnlyCollection<AIDocumentChunk>> GetChunksByAIDocumentIdAsync(string documentId)
        {
            return Task.FromResult(_chunks);
        }

        public Task<IReadOnlyCollection<AIDocumentChunk>> GetChunksByReferenceAsync(string referenceId, string referenceType)
        {
            throw new NotSupportedException();
        }

        public Task DeleteByDocumentIdAsync(string documentId)
        {
            throw new NotSupportedException();
        }

        public ValueTask<AIDocumentChunk> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<IReadOnlyCollection<AIDocumentChunk>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<IReadOnlyCollection<AIDocumentChunk>> GetAsync(
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<PageResult<AIDocumentChunk>> PageAsync<TQuery>(
            int page,
            int pageSize,
            TQuery context,
            CancellationToken cancellationToken = default)
            where TQuery : QueryContext
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> DeleteAsync(AIDocumentChunk entry, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask CreateAsync(AIDocumentChunk entry, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask UpdateAsync(AIDocumentChunk entry, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
