using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Chat.Models;
using CrestApps.Core.AI.Documents.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the legacy LINQ-based tabular batching implementation with the current production path.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class TabularBatchProcessorBenchmarks
{
    private const string FileName = "benchmark.csv";

    private readonly ILogger<TabularBatchProcessor> _logger = NullLogger<TabularBatchProcessor>.Instance;
    private RowLevelTabularBatchOptions _options;
    private TabularBatchProcessor _processor;
    private string _content;

    /// <summary>
    /// Gets or sets the number of synthetic data rows, excluding the header.
    /// </summary>
    [Params(1_000, 10_000, 100_000)]
    public int RowCount { get; set; }

    /// <summary>
    /// Gets or sets the configured number of rows per batch.
    /// </summary>
    [Params(25, 100)]
    public int BatchSize { get; set; }

    /// <summary>
    /// Gets or sets the maximum row count. Zero disables truncation; 500 enables it.
    /// </summary>
    [Params(0, 500)]
    public int MaxRows { get; set; }

    /// <summary>
    /// Creates in-memory tabular content and verifies exact legacy/current equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _content = CreateContent(RowCount);
        _options = new RowLevelTabularBatchOptions
        {
            RowBatchSize = BatchSize,
            MaxRowsPerDocument = MaxRows,
        };
        _processor = new TabularBatchProcessor(
            completionService: null,
            deploymentManager: null,
            aiTemplateService: null,
            Options.Create(_options),
            _logger);

        EnsureEquivalent(SplitLegacy(), SplitCurrent());
    }

    /// <summary>
    /// Splits the synthetic content with the production implementation captured before optimization.
    /// </summary>
    /// <returns>The fully materialized tabular batches.</returns>
    [Benchmark(Baseline = true)]
    public IList<TabularBatch> SplitLegacy()
    {
        return SplitLegacyCore(_content, FileName, _options, _logger);
    }

    /// <summary>
    /// Splits the synthetic content with the current production implementation.
    /// </summary>
    /// <returns>The fully materialized tabular batches.</returns>
    [Benchmark]
    public IList<TabularBatch> SplitCurrent()
    {
        return _processor.SplitIntoBatches(_content, FileName);
    }

    /// <summary>
    /// Creates synthetic CSV-like content entirely in memory.
    /// </summary>
    /// <param name="rowCount">The number of data rows to append after the header.</param>
    /// <returns>The synthetic tabular content.</returns>
    private static string CreateContent(int rowCount)
    {
        var builder = new StringBuilder(capacity: 32 + (rowCount * 32));
        builder.Append("id,name,value");

        for (var index = 1; index <= rowCount; index++)
        {
            builder.Append('\n');
            builder.Append(index);
            builder.Append(",name-");
            builder.Append(index);
            builder.Append(",value-");
            builder.Append(index);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reproduces the complete pre-optimization production implementation.
    /// </summary>
    /// <param name="content">The tabular content.</param>
    /// <param name="fileName">The source file name.</param>
    /// <param name="options">The batching options.</param>
    /// <param name="logger">The processor logger.</param>
    /// <returns>The fully materialized tabular batches.</returns>
    private static List<TabularBatch> SplitLegacyCore(
        string content,
        string fileName,
        RowLevelTabularBatchOptions options,
        ILogger<TabularBatchProcessor> logger)
    {
        var batches = new List<TabularBatch>();

        if (string.IsNullOrWhiteSpace(content))
        {
            return batches;
        }

        var lines = content.Split('\n', StringSplitOptions.None);

        if (lines.Length == 0)
        {
            return batches;
        }

        var headerRow = lines[0];
        var dataLines = lines.Skip(1).ToList();
        var maxRows = options.MaxRowsPerDocument;

        if (maxRows > 0 && dataLines.Count > maxRows)
        {
            logger.LogWarning(
                "Document '{FileName}' has {ActualRows} rows, exceeding the maximum of {MaxRows}. Truncating.",
                fileName, dataLines.Count, maxRows);

            dataLines = dataLines.Take(maxRows).ToList();
        }

        if (dataLines.Count == 0)
        {
            return batches;
        }

        var batchSize = options.RowBatchSize;

        if (batchSize <= 0)
        {
            batchSize = 25;
        }

        var batchIndex = 0;

        for (var index = 0; index < dataLines.Count; index += batchSize)
        {
            var batchRows = dataLines.Skip(index).Take(batchSize).ToList();

            batches.Add(new TabularBatch
            {
                BatchIndex = batchIndex,
                FileName = fileName,
                HeaderRow = headerRow,
                DataRows = batchRows,
                RowStartIndex = index + 1,
                RowEndIndex = index + batchRows.Count,
            });

            batchIndex++;
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Split document '{FileName}' into {BatchCount} batches of up to {BatchSize} rows each.",
                fileName, batches.Count, batchSize);
        }

        return batches;
    }

    /// <summary>
    /// Verifies that legacy and current batches expose identical observable values.
    /// </summary>
    /// <param name="legacy">The legacy result.</param>
    /// <param name="current">The current result.</param>
    private static void EnsureEquivalent(
        IList<TabularBatch> legacy,
        IList<TabularBatch> current)
    {
        if (legacy.Count != current.Count)
        {
            throw new InvalidOperationException("Legacy and current batching returned different batch counts.");
        }

        for (var index = 0; index < legacy.Count; index++)
        {
            var legacyBatch = legacy[index];
            var currentBatch = current[index];

            if (legacyBatch.BatchIndex != currentBatch.BatchIndex ||
                legacyBatch.FileName != currentBatch.FileName ||
                legacyBatch.HeaderRow != currentBatch.HeaderRow ||
                legacyBatch.RowStartIndex != currentBatch.RowStartIndex ||
                legacyBatch.RowEndIndex != currentBatch.RowEndIndex ||
                legacyBatch.RowCount != currentBatch.RowCount ||
                !legacyBatch.DataRows.SequenceEqual(currentBatch.DataRows, StringComparer.Ordinal) ||
                legacyBatch.GetContent() != currentBatch.GetContent())
            {
                throw new InvalidOperationException($"Legacy and current batching differ at batch {index}.");
            }
        }
    }
}
