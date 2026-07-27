using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Defines the document identifier distributions used by the indexing queue benchmarks.
/// </summary>
public enum AIDataSourceDocumentIdDistribution
{
    /// <summary>
    /// Every identifier is valid and unique.
    /// </summary>
    Unique,

    /// <summary>
    /// Half of the identifiers repeat an earlier identifier with identical casing.
    /// </summary>
    HalfDuplicates,

    /// <summary>
    /// Ninety percent of the identifiers are null, empty, or whitespace.
    /// </summary>
    MostlyInvalid,

    /// <summary>
    /// Every second identifier repeats the preceding identifier with different casing.
    /// </summary>
    CaseOnlyDuplicates,
}

/// <summary>
/// Measures the current queue normalization pipeline against a simple one-pass candidate.
/// Each path writes one constructed work item to an available in-memory channel and performs
/// no store, consumer, or network work. This class must remain unsealed because BenchmarkDotNet
/// generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
[InvocationCount(InvocationsPerIteration)]
public class AIDataSourceIndexingQueueNormalizationBenchmarks
{
    private const int InvocationsPerIteration = 64;
    private const string SourceIndexProfileName = "benchmark-source";
    private OnePassAIDataSourceIndexingQueue _candidateQueue;
    private IAsyncEnumerator<AIDataSourceIndexingWorkItem> _candidateReader;
    private AIDataSourceIndexingQueue _currentQueue;
    private IAsyncEnumerator<AIDataSourceIndexingWorkItem> _currentReader;
    private string[] _documentIds;

    /// <summary>
    /// Gets or sets the number of input document identifiers.
    /// </summary>
    [Params(10, 100, 1_000, 10_000)]
    public int IdCount { get; set; }

    /// <summary>
    /// Gets or sets the document identifier distribution.
    /// </summary>
    [Params(
        AIDataSourceDocumentIdDistribution.Unique,
        AIDataSourceDocumentIdDistribution.HalfDuplicates,
        AIDataSourceDocumentIdDistribution.MostlyInvalid,
        AIDataSourceDocumentIdDistribution.CaseOnlyDuplicates)]
    public AIDataSourceDocumentIdDistribution Distribution { get; set; }

    /// <summary>
    /// Creates immutable inputs, independent channels, and verifies candidate equivalence.
    /// </summary>
    /// <returns>A task that completes after equivalence verification.</returns>
    [GlobalSetup]
    public async Task Setup()
    {
        _documentIds = CreateDocumentIds();
        _currentQueue = new AIDataSourceIndexingQueue(NullLogger<AIDataSourceIndexingQueue>.Instance);
        _candidateQueue = new OnePassAIDataSourceIndexingQueue(NullLogger<OnePassAIDataSourceIndexingQueue>.Instance);
        _currentReader = _currentQueue.ReadAllAsync().GetAsyncEnumerator();
        _candidateReader = _candidateQueue.ReadAllAsync().GetAsyncEnumerator();

        await _currentQueue.QueueSyncSourceDocumentsAsync(SourceIndexProfileName, _documentIds);
        await _candidateQueue.QueueSyncSourceDocumentsAsync(SourceIndexProfileName, _documentIds);

        EnsureEquivalent(
            await ReadNextAsync(_currentReader),
            await ReadNextAsync(_candidateReader));
    }

    /// <summary>
    /// Drains work items outside benchmark measurements so every write remains immediately available.
    /// </summary>
    [IterationCleanup]
    public async Task Cleanup()
    {
        for (var index = 0; index < InvocationsPerIteration; index++)
        {
            await ReadNextAsync(_currentReader);
            await ReadNextAsync(_candidateReader);
        }
    }

    /// <summary>
    /// Releases the queue readers after all benchmark cases complete.
    /// </summary>
    [GlobalCleanup]
    public async Task DisposeReaders()
    {
        await _currentReader.DisposeAsync();
        await _candidateReader.DisposeAsync();
    }

    /// <summary>
    /// Enqueues with the current production <c>Where</c>, <c>Distinct</c>, and <c>ToArray</c> pipeline.
    /// </summary>
    /// <returns>A value task that completes when the in-memory channel accepts the item.</returns>
    [Benchmark(Baseline = true)]
    public ValueTask EnqueueWithCurrentLinq()
    {
        return _currentQueue.QueueSyncSourceDocumentsAsync(SourceIndexProfileName, _documentIds);
    }

    /// <summary>
    /// Enqueues with a one-pass hash-set candidate that directly indexes array inputs.
    /// </summary>
    /// <returns>A value task that completes when the in-memory channel accepts the item.</returns>
    [Benchmark]
    public ValueTask EnqueueWithOnePassCandidate()
    {
        return _candidateQueue.QueueSyncSourceDocumentsAsync(SourceIndexProfileName, _documentIds);
    }

    /// <summary>
    /// Creates the requested document identifier distribution.
    /// </summary>
    /// <returns>The generated identifiers.</returns>
    private string[] CreateDocumentIds()
    {
        var documentIds = new string[IdCount];

        for (var index = 0; index < documentIds.Length; index++)
        {
            documentIds[index] = Distribution switch
            {
                AIDataSourceDocumentIdDistribution.Unique => $"document-{index:D5}",
                AIDataSourceDocumentIdDistribution.HalfDuplicates => $"document-{index % (IdCount / 2):D5}",
                AIDataSourceDocumentIdDistribution.MostlyInvalid => CreateMostlyInvalidId(index),
                AIDataSourceDocumentIdDistribution.CaseOnlyDuplicates => index % 2 == 0
                    ? $"Document-{index / 2:D5}"
                    : $"DOCUMENT-{index / 2:D5}",
                _ => throw new InvalidOperationException($"Unsupported distribution '{Distribution}'."),
            };
        }

        return documentIds;
    }

    /// <summary>
    /// Creates one valid identifier followed by nine null, empty, or whitespace values.
    /// </summary>
    /// <param name="index">The source position.</param>
    /// <returns>The identifier at the requested position.</returns>
    private static string CreateMostlyInvalidId(int index)
    {
        return (index % 10) switch
        {
            0 => $"document-{index:D5}",
            1 => null,
            2 => string.Empty,
            3 => " ",
            4 => "\t",
            5 => "\r\n",
            6 => "\u00a0",
            7 => " \t ",
            8 => "\n",
            _ => "\r",
        };
    }

    /// <summary>
    /// Verifies normalized identifiers, target, and work-item operation remain exactly equivalent.
    /// </summary>
    /// <param name="current">The current production work item.</param>
    /// <param name="candidate">The candidate work item.</param>
    private static void EnsureEquivalent(
        AIDataSourceIndexingWorkItem current,
        AIDataSourceIndexingWorkItem candidate)
    {
        if (current.Type != candidate.Type ||
            !string.Equals(current.SourceIndexProfileName, candidate.SourceIndexProfileName, StringComparison.Ordinal) ||
            !current.DocumentIds.SequenceEqual(candidate.DocumentIds, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Queue normalization candidates produced different work items.");
        }
    }

    /// <summary>
    /// Reads the next queued work item outside benchmark measurements.
    /// </summary>
    /// <param name="reader">The queue reader.</param>
    /// <returns>The queued work item.</returns>
    private static async ValueTask<AIDataSourceIndexingWorkItem> ReadNextAsync(
        IAsyncEnumerator<AIDataSourceIndexingWorkItem> reader)
    {
        if (!await reader.MoveNextAsync())
        {
            throw new InvalidOperationException("Expected a queued work item.");
        }

        return reader.Current;
    }

    /// <summary>
    /// Captures the rejected one-pass implementation with the production queue structure.
    /// </summary>
    private sealed class OnePassAIDataSourceIndexingQueue
    {
        private readonly Channel<AIDataSourceIndexingWorkItem> _channel = Channel.CreateUnbounded<AIDataSourceIndexingWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        private readonly ILogger<OnePassAIDataSourceIndexingQueue> _logger;

        /// <summary>
        /// Initializes a new candidate queue.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public OnePassAIDataSourceIndexingQueue(ILogger<OnePassAIDataSourceIndexingQueue> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Queues source documents with the rejected one-pass normalization candidate.
        /// </summary>
        /// <param name="sourceIndexProfileName">The source index profile name.</param>
        /// <param name="documentIds">The source document identifiers.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A value task that completes when the channel accepts the item.</returns>
        public ValueTask QueueSyncSourceDocumentsAsync(
            string sourceIndexProfileName,
            IReadOnlyCollection<string> documentIds,
            CancellationToken cancellationToken = default)
        {
            return QueueDocumentIdsAsync(
                sourceIndexProfileName,
                nameof(sourceIndexProfileName),
                documentIds,
                AIDataSourceIndexingWorkItem.ForSyncSourceDocuments,
                cancellationToken);
        }

        /// <summary>
        /// Normalizes document identifiers and queues one targeted work item.
        /// </summary>
        /// <param name="target">The source index profile name or data source identifier.</param>
        /// <param name="targetParameterName">The public target parameter name used for validation failures.</param>
        /// <param name="documentIds">The source document identifiers.</param>
        /// <param name="factory">The work-item factory for the requested operation.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A value task that completes when the work item is accepted by the channel.</returns>
        private ValueTask QueueDocumentIdsAsync(
            string target,
            string targetParameterName,
            IReadOnlyCollection<string> documentIds,
            Func<string, IReadOnlyCollection<string>, AIDataSourceIndexingWorkItem> factory,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(target, targetParameterName);
            ArgumentNullException.ThrowIfNull(documentIds);

            var ids = Normalize(documentIds);

            if (ids.Length == 0)
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Skipped queueing data-source work item because no document ids remained after normalization.");
                }

                return ValueTask.CompletedTask;
            }

            var workItem = factory(target, ids);

            LogQueuedWorkItem(workItem, target, ids.Length);

            return _channel.Writer.WriteAsync(workItem, cancellationToken);
        }

        /// <summary>
        /// Normalizes identifiers with one direct source pass and case-insensitive set semantics.
        /// </summary>
        /// <param name="documentIds">The source document identifiers.</param>
        /// <returns>The normalized identifier array.</returns>
        private static string[] Normalize(IReadOnlyCollection<string> documentIds)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (documentIds is string[] sourceIds)
            {
                for (var index = 0; index < sourceIds.Length; index++)
                {
                    var documentId = sourceIds[index];

                    if (!string.IsNullOrWhiteSpace(documentId))
                    {
                        ids.Add(documentId);
                    }
                }

                return [.. ids];
            }

            foreach (var documentId in documentIds)
            {
                if (!string.IsNullOrWhiteSpace(documentId))
                {
                    ids.Add(documentId);
                }
            }

            return [.. ids];
        }

        /// <summary>
        /// Logs a queued candidate work item when trace logging is enabled.
        /// </summary>
        /// <param name="workItem">The queued work item.</param>
        /// <param name="target">The work-item target.</param>
        /// <param name="documentCount">The normalized document count.</param>
        private void LogQueuedWorkItem(
            AIDataSourceIndexingWorkItem workItem,
            string target,
            int documentCount)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Queued data-source work item {WorkItemType}. Target={Target}, DocumentCount={DocumentCount}.", workItem.Type, target, documentCount);
            }
        }

        /// <summary>
        /// Reads queued work items.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The queued work items.</returns>
        public IAsyncEnumerable<AIDataSourceIndexingWorkItem> ReadAllAsync(
            CancellationToken cancellationToken = default)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }
    }
}
