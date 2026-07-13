using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Documents.Tabular;
using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured CSV/TSV ingestion-document reconstruction and artifact materialization path
/// with the current production parser, while also measuring the isolated cost of reconstruction.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 6)]
public class DelimitedArtifactConstructionBenchmarks
{
    private readonly PlainTextIngestionDocumentReader _reader = new();

    private byte[] _contentBytes;
    private string _fileName;

    /// <summary>
    /// Gets or sets the number of synthetic data rows, excluding the header.
    /// </summary>
    [Params(1_000, 10_000, 100_000)]
    public int RowCount { get; set; }

    /// <summary>
    /// Gets or sets the synthetic row shape.
    /// </summary>
    [Params(
        DelimitedArtifactShape.Narrow,
        DelimitedArtifactShape.Wide,
        DelimitedArtifactShape.QuotedNewlineHeavy)]
    public DelimitedArtifactShape Shape { get; set; }

    /// <summary>
    /// Gets or sets the delimited file format.
    /// </summary>
    [Params(DelimitedArtifactFormat.Csv, DelimitedArtifactFormat.Tsv)]
    public DelimitedArtifactFormat Format { get; set; }

    /// <summary>
    /// Creates the in-memory delimited input and verifies exact legacy/current/direct equivalence.
    /// </summary>
    [GlobalSetup]
    public async Task Setup()
    {
        var delimiter = Format == DelimitedArtifactFormat.Csv ? ',' : '\t';
        _fileName = Format == DelimitedArtifactFormat.Csv ? "benchmark.csv" : "benchmark.tsv";
        var content = CreateContent(RowCount, Shape, delimiter);
        _contentBytes = Encoding.UTF8.GetBytes(content);

        EnsureEquivalent(await CreateLegacy(), await CreateCurrent());
        EnsureEquivalent(await CreateLegacy(), await CreateWithoutIngestionReconstruction());
    }

    /// <summary>
    /// Reproduces the complete pre-optimization reconstruction, parser, and artifact-copy path.
    /// </summary>
    /// <returns>The fully materialized artifact.</returns>
    [Benchmark(Baseline = true)]
    public async Task<TabularDocumentArtifact> CreateLegacy()
    {
        await using var stream = new MemoryStream(_contentBytes, writable: false);
        var ingestionDocument = await _reader.ReadAsync(
            stream,
            _fileName,
            GetMediaType(),
            CancellationToken.None);
        var content = ReconstructContent(ingestionDocument);

        return CreateLegacyArtifact(content, _fileName);
    }

    /// <summary>
    /// Reconstructs the ingestion document content and uses the current production artifact path.
    /// </summary>
    /// <returns>The fully materialized artifact.</returns>
    [Benchmark]
    public async Task<TabularDocumentArtifact> CreateCurrent()
    {
        await using var stream = new MemoryStream(_contentBytes, writable: false);
        var ingestionDocument = await _reader.ReadAsync(
            stream,
            _fileName,
            GetMediaType(),
            CancellationToken.None);
        var content = ReconstructContent(ingestionDocument);

        return TabularDocumentArtifact.FromDelimitedContent(content, _fileName);
    }

    /// <summary>
    /// Decodes the same local stream and uses the current production artifact path directly, omitting
    /// only the ingestion-document graph and subsequent content reconstruction.
    /// </summary>
    /// <returns>The fully materialized artifact.</returns>
    [Benchmark]
    public async Task<TabularDocumentArtifact> CreateWithoutIngestionReconstruction()
    {
        await using var stream = new MemoryStream(_contentBytes, writable: false);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        var content = await reader.ReadToEndAsync(CancellationToken.None);

        return TabularDocumentArtifact.FromDelimitedContent(content, _fileName);
    }

    /// <summary>
    /// Creates synthetic delimited content for the requested scale and shape.
    /// </summary>
    /// <param name="rowCount">The number of data rows to create.</param>
    /// <param name="shape">The row shape to create.</param>
    /// <param name="delimiter">The field delimiter.</param>
    /// <returns>The generated delimited content.</returns>
    private static string CreateContent(
        int rowCount,
        DelimitedArtifactShape shape,
        char delimiter)
    {
        var columnCount = shape switch
        {
            DelimitedArtifactShape.Narrow => 4,
            DelimitedArtifactShape.Wide => 16,
            _ => 8,
        };
        var estimatedRowLength = shape switch
        {
            DelimitedArtifactShape.Narrow => 48,
            DelimitedArtifactShape.Wide => 240,
            _ => 192,
        };
        var builder = new StringBuilder(capacity: 128 + (rowCount * estimatedRowLength));

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            if (columnIndex > 0)
            {
                builder.Append(delimiter);
            }

            builder.Append("column-");
            builder.Append(columnIndex);
        }

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            builder.Append('\n');

            switch (shape)
            {
                case DelimitedArtifactShape.Narrow:
                    AppendNarrowRow(builder, delimiter, rowIndex);
                    break;
                case DelimitedArtifactShape.Wide:
                    AppendWideRow(builder, delimiter, rowIndex, columnCount);
                    break;
                case DelimitedArtifactShape.QuotedNewlineHeavy:
                    AppendQuotedNewlineHeavyRow(builder, delimiter, rowIndex);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Appends one narrow row.
    /// </summary>
    /// <param name="builder">The destination builder.</param>
    /// <param name="delimiter">The field delimiter.</param>
    /// <param name="rowIndex">The zero-based row index.</param>
    private static void AppendNarrowRow(
        StringBuilder builder,
        char delimiter,
        int rowIndex)
    {
        builder.Append(rowIndex);
        builder.Append(delimiter);
        builder.Append("name-");
        builder.Append(rowIndex);
        builder.Append(delimiter);
        builder.Append(rowIndex * 17);
        builder.Append(delimiter);
        builder.Append((rowIndex & 1) == 0 ? "true" : "false");
    }

    /// <summary>
    /// Appends one wide row.
    /// </summary>
    /// <param name="builder">The destination builder.</param>
    /// <param name="delimiter">The field delimiter.</param>
    /// <param name="rowIndex">The zero-based row index.</param>
    /// <param name="columnCount">The number of fields to append.</param>
    private static void AppendWideRow(
        StringBuilder builder,
        char delimiter,
        int rowIndex,
        int columnCount)
    {
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            if (columnIndex > 0)
            {
                builder.Append(delimiter);
            }

            builder.Append("row-");
            builder.Append(rowIndex);
            builder.Append("-column-");
            builder.Append(columnIndex);
        }
    }

    /// <summary>
    /// Appends one row containing quoted delimiters, escaped quotes, and embedded line endings.
    /// </summary>
    /// <param name="builder">The destination builder.</param>
    /// <param name="delimiter">The field delimiter.</param>
    /// <param name="rowIndex">The zero-based row index.</param>
    private static void AppendQuotedNewlineHeavyRow(
        StringBuilder builder,
        char delimiter,
        int rowIndex)
    {
        AppendQuoted(builder, $"row-{rowIndex}{delimiter}value");
        builder.Append(delimiter);
        AppendQuoted(builder, $"said \"hello\" {rowIndex}");
        builder.Append(delimiter);
        AppendQuoted(builder, $"line-1\nline-2-{rowIndex}");
        builder.Append(delimiter);
        AppendQuoted(builder, $"line-1\rline-2-{rowIndex}");
        builder.Append(delimiter);
        AppendQuoted(builder, $"line-1\r\nline-2-{rowIndex}");
        builder.Append(delimiter);
        builder.Append("plain-");
        builder.Append(rowIndex);
        builder.Append(delimiter);
        AppendQuoted(builder, $" spaced {rowIndex} ");
        builder.Append(delimiter);
    }

    /// <summary>
    /// Appends one RFC-style quoted field and escapes embedded double quotes.
    /// </summary>
    /// <param name="builder">The destination builder.</param>
    /// <param name="value">The field value.</param>
    private static void AppendQuoted(StringBuilder builder, string value)
    {
        builder.Append('"');

        foreach (var c in value)
        {
            if (c == '"')
            {
                builder.Append("\"\"");
            }
            else
            {
                builder.Append(c);
            }
        }

        builder.Append('"');
    }

    /// <summary>
    /// Gets the media type for the active benchmark format.
    /// </summary>
    /// <returns>The CSV or TSV media type.</returns>
    private string GetMediaType()
    {
        return Format == DelimitedArtifactFormat.Csv
            ? "text/csv"
            : "text/tab-separated-values";
    }

    /// <summary>
    /// Reconstructs text from an ingestion document exactly as the artifact factory does.
    /// </summary>
    /// <param name="ingestionDocument">The ingestion document.</param>
    /// <returns>The reconstructed delimited content.</returns>
    private static string ReconstructContent(IngestionDocument ingestionDocument)
    {
        return string.Join('\n', ingestionDocument.EnumerateContent()
            .Select(element => element.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    /// <summary>
    /// Creates an artifact with the complete pre-optimization parser and copy path.
    /// </summary>
    /// <param name="content">The delimited content.</param>
    /// <param name="fileName">The source file name.</param>
    /// <returns>The legacy artifact.</returns>
    private static TabularDocumentArtifact CreateLegacyArtifact(string content, string fileName)
    {
        var (header, rows) = DelimitedDataParser.Parse(content, fileName);

        return new TabularDocumentArtifact
        {
            Header = header.ToList(),
            Rows = rows.Select(row => row.ToList()).ToList(),
        };
    }

    /// <summary>
    /// Verifies exact artifact header, row, field, and ordering equivalence.
    /// </summary>
    /// <param name="legacy">The legacy artifact.</param>
    /// <param name="current">The artifact to compare.</param>
    private static void EnsureEquivalent(
        TabularDocumentArtifact legacy,
        TabularDocumentArtifact current)
    {
        if (!legacy.Header.SequenceEqual(current.Header, StringComparer.Ordinal) ||
            legacy.Rows.Count != current.Rows.Count)
        {
            throw new InvalidOperationException("Legacy and current artifact shapes differ.");
        }

        for (var rowIndex = 0; rowIndex < legacy.Rows.Count; rowIndex++)
        {
            if (!legacy.Rows[rowIndex].SequenceEqual(current.Rows[rowIndex], StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"Legacy and current artifacts differ at row {rowIndex}.");
            }
        }
    }

    /// <summary>
    /// Defines synthetic delimited row shapes.
    /// </summary>
    public enum DelimitedArtifactShape
    {
        /// <summary>
        /// Four short unquoted fields.
        /// </summary>
        Narrow,

        /// <summary>
        /// Sixteen unquoted fields.
        /// </summary>
        Wide,

        /// <summary>
        /// Eight fields dominated by quoting, escaped quotes, embedded delimiters, and line endings.
        /// </summary>
        QuotedNewlineHeavy,
    }

    /// <summary>
    /// Defines the benchmark delimited formats.
    /// </summary>
    public enum DelimitedArtifactFormat
    {
        /// <summary>
        /// Comma-separated values.
        /// </summary>
        Csv,

        /// <summary>
        /// Tab-separated values.
        /// </summary>
        Tsv,
    }
}
