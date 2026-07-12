using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing.Models;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures local AI document chunk filtering and index-document materialization.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class AIDocumentIndexingMaterializationBenchmarks
{
    private AIDocumentChunk[] _chunks;
    private AIDocument _document;

    /// <summary>
    /// Gets or sets the number of chunks supplied to the materializer.
    /// </summary>
    [Params(10, 100, 1000, 10000)]
    public int ChunkCount { get; set; }

    /// <summary>
    /// Creates realistic immutable benchmark inputs and verifies candidate equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _document = new AIDocument
        {
            ItemId = "document-benchmark",
            ReferenceId = "profile-benchmark",
            ReferenceType = "profile",
            FileName = "quarterly-research-report.pdf",
        };
        _chunks = new AIDocumentChunk[ChunkCount];

        for (var i = 0; i < _chunks.Length; i++)
        {
            _chunks[i] = new AIDocumentChunk
            {
                ItemId = $"chunk-{i:D5}",
                AIDocumentId = _document.ItemId,
                ReferenceId = _document.ReferenceId,
                ReferenceType = _document.ReferenceType,
                Content = i % 20 == 0
                    ? " "
                    : $"Section {i}: revenue, customer activity, operational results, and cited source details.",
                Embedding = i % 20 == 10
                    ? []
                    : [0.125f, 0.25f, 0.5f, 0.75f, 0.875f, 1f],
                Index = i,
            };
        }

        var legacy = MaterializeLegacyCore();
        EnsureEquivalent(legacy, MaterializeDirectArrayCore());
        EnsureEquivalent(legacy, MaterializeCountAwareCore());
    }

    /// <summary>
    /// Materializes documents with the production implementation captured before optimization.
    /// </summary>
    /// <returns>The materialized index documents.</returns>
    [Benchmark(Baseline = true)]
    public IndexDocument[] MaterializeLegacy()
    {
        return MaterializeLegacyCore();
    }

    /// <summary>
    /// Materializes documents by filling the known-size output array directly.
    /// </summary>
    /// <returns>The materialized index documents.</returns>
    [Benchmark]
    public IndexDocument[] MaterializeDirectArray()
    {
        return MaterializeDirectArrayCore();
    }

    /// <summary>
    /// Materializes documents with a count-aware two-stage candidate.
    /// </summary>
    /// <returns>The materialized index documents.</returns>
    [Benchmark]
    public IndexDocument[] MaterializeCountAware()
    {
        return MaterializeCountAwareCore();
    }

    private IndexDocument[] MaterializeLegacyCore()
    {
        var indexedChunks = _chunks
            .Where(chunk => chunk.Embedding is { Length: > 0 } && !string.IsNullOrWhiteSpace(chunk.Content))
            .ToList();

        return indexedChunks
            .Select(chunk => new IndexDocument
            {
                Id = chunk.ItemId,
                Fields = new Dictionary<string, object>
                {
                    [DocumentIndexConstants.ColumnNames.ChunkId] = chunk.ItemId,
                    [DocumentIndexConstants.ColumnNames.DocumentId] = _document.ItemId,
                    [DocumentIndexConstants.ColumnNames.Content] = chunk.Content,
                    [DocumentIndexConstants.ColumnNames.FileName] = _document.FileName,
                    [DocumentIndexConstants.ColumnNames.ReferenceId] = chunk.ReferenceId,
                    [DocumentIndexConstants.ColumnNames.ReferenceType] = chunk.ReferenceType,
                    [DocumentIndexConstants.ColumnNames.Embedding] = chunk.Embedding,
                    [DocumentIndexConstants.ColumnNames.ChunkIndex] = chunk.Index,
                },
            })
            .ToArray();
    }

    private IndexDocument[] MaterializeDirectArrayCore()
    {
        var indexedChunks = _chunks
            .Where(chunk => chunk.Embedding is { Length: > 0 } && !string.IsNullOrWhiteSpace(chunk.Content))
            .ToList();
        var documents = new IndexDocument[indexedChunks.Count];

        for (var i = 0; i < indexedChunks.Count; i++)
        {
            var chunk = indexedChunks[i];
            documents[i] = new IndexDocument
            {
                Id = chunk.ItemId,
                Fields = new Dictionary<string, object>
                {
                    [DocumentIndexConstants.ColumnNames.ChunkId] = chunk.ItemId,
                    [DocumentIndexConstants.ColumnNames.DocumentId] = _document.ItemId,
                    [DocumentIndexConstants.ColumnNames.Content] = chunk.Content,
                    [DocumentIndexConstants.ColumnNames.FileName] = _document.FileName,
                    [DocumentIndexConstants.ColumnNames.ReferenceId] = chunk.ReferenceId,
                    [DocumentIndexConstants.ColumnNames.ReferenceType] = chunk.ReferenceType,
                    [DocumentIndexConstants.ColumnNames.Embedding] = chunk.Embedding,
                    [DocumentIndexConstants.ColumnNames.ChunkIndex] = chunk.Index,
                },
            };
        }

        return documents;
    }

    private IndexDocument[] MaterializeCountAwareCore()
    {
        var indexedChunks = new AIDocumentChunk[_chunks.Length];
        var indexedChunkCount = 0;

        foreach (var chunk in _chunks)
        {
            if (chunk.Embedding is not { Length: > 0 } ||
                string.IsNullOrWhiteSpace(chunk.Content))
            {
                continue;
            }

            indexedChunks[indexedChunkCount++] = chunk;
        }

        var documents = new IndexDocument[indexedChunkCount];

        for (var i = 0; i < indexedChunkCount; i++)
        {
            var chunk = indexedChunks[i];
            documents[i] = new IndexDocument
            {
                Id = chunk.ItemId,
                Fields = new Dictionary<string, object>
                {
                    [DocumentIndexConstants.ColumnNames.ChunkId] = chunk.ItemId,
                    [DocumentIndexConstants.ColumnNames.DocumentId] = _document.ItemId,
                    [DocumentIndexConstants.ColumnNames.Content] = chunk.Content,
                    [DocumentIndexConstants.ColumnNames.FileName] = _document.FileName,
                    [DocumentIndexConstants.ColumnNames.ReferenceId] = chunk.ReferenceId,
                    [DocumentIndexConstants.ColumnNames.ReferenceType] = chunk.ReferenceType,
                    [DocumentIndexConstants.ColumnNames.Embedding] = chunk.Embedding,
                    [DocumentIndexConstants.ColumnNames.ChunkIndex] = chunk.Index,
                },
            };
        }

        return documents;
    }

    private static void EnsureEquivalent(
        IndexDocument[] legacy,
        IndexDocument[] candidate)
    {
        if (legacy.Length != candidate.Length)
        {
            throw new InvalidOperationException("Legacy and candidate document counts differ.");
        }

        for (var i = 0; i < legacy.Length; i++)
        {
            var legacyDocument = legacy[i];
            var candidateDocument = candidate[i];

            if (!string.Equals(legacyDocument.Id, candidateDocument.Id, StringComparison.Ordinal) ||
                legacyDocument.Fields.Count != candidateDocument.Fields.Count)
            {
                throw new InvalidOperationException("Legacy and candidate documents differ.");
            }

            foreach (var (key, legacyValue) in legacyDocument.Fields)
            {
                if (!candidateDocument.Fields.TryGetValue(key, out var candidateValue) ||
                    !ValuesEqual(legacyValue, candidateValue))
                {
                    throw new InvalidOperationException($"Legacy and candidate field '{key}' differs.");
                }
            }
        }
    }

    private static bool ValuesEqual(object legacyValue, object candidateValue)
    {
        if (legacyValue is float[] legacyEmbedding &&
            candidateValue is float[] candidateEmbedding)
        {
            return legacyEmbedding.SequenceEqual(candidateEmbedding);
        }

        return Equals(legacyValue, candidateValue);
    }
}
