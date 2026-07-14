using System.Globalization;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Documents.Tools;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured column-type inference that classifies every sampled value with a candidate
/// that stops sampling a column once it becomes text, since text absorbs every later value.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class TabularColumnTypeInferenceBenchmarks
{
    private const int OperationsPerInvocation = 1_000;
    private const int TargetSamplesPerColumn = 32;

    private static readonly MethodInfo _productionInferColumnTypes = typeof(GetDocumentMetadataTool)
        .GetMethod("InferColumnTypes", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Unable to find the production column-type inference helper.");

    private TabularDocumentArtifact _artifact;

    /// <summary>
    /// Gets or sets the artifact column-type shape used by the benchmark.
    /// </summary>
    [Params(
        InferenceShape.AllText,
        InferenceShape.AllTyped,
        InferenceShape.HalfTextHalfTyped)]
    public InferenceShape Shape { get; set; }

    /// <summary>
    /// Gets or sets the number of columns in the synthetic artifact.
    /// </summary>
    [Params(8, 40)]
    public int ColumnCount { get; set; }

    /// <summary>
    /// Builds the representative artifact and verifies exact production/legacy/absorbing equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _artifact = CreateArtifact(Shape, ColumnCount);

        var production = (string[])_productionInferColumnTypes.Invoke(null, [_artifact]);
        var legacy = InferLegacy(_artifact);
        var absorbing = InferAbsorbing(_artifact);

        if (!production.SequenceEqual(legacy, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Legacy reproduction diverged from production for '{Shape}'/{ColumnCount}.");
        }

        if (!production.SequenceEqual(absorbing, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Absorbing candidate diverged from production for '{Shape}'/{ColumnCount}.");
        }
    }

    /// <summary>
    /// Runs the captured formatter that classifies every sampled value repeatedly.
    /// </summary>
    /// <returns>The accumulated inferred-type count.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = OperationsPerInvocation)]
    public int InferLegacyAtScale()
    {
        var count = 0;

        for (var index = 0; index < OperationsPerInvocation; index++)
        {
            count += InferLegacy(_artifact).Length;
        }

        return count;
    }

    /// <summary>
    /// Runs the candidate that stops sampling a column once it becomes text repeatedly.
    /// </summary>
    /// <returns>The accumulated inferred-type count.</returns>
    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public int InferAbsorbingAtScale()
    {
        var count = 0;

        for (var index = 0; index < OperationsPerInvocation; index++)
        {
            count += InferAbsorbing(_artifact).Length;
        }

        return count;
    }

    /// <summary>
    /// Reproduces the captured production inference that classifies every sampled value.
    /// </summary>
    /// <param name="artifact">The artifact to inspect.</param>
    /// <returns>The inferred type name for each column.</returns>
    private static string[] InferLegacy(TabularDocumentArtifact artifact)
    {
        var headerCount = artifact?.Header?.Count ?? 0;

        if (headerCount == 0)
        {
            return [];
        }

        var states = new InferredColumnType[headerCount];
        var sampleCounts = new int[headerCount];
        var rows = artifact?.Rows ?? [];

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var hasPendingColumns = false;

            for (var columnIndex = 0; columnIndex < headerCount; columnIndex++)
            {
                if (sampleCounts[columnIndex] >= TargetSamplesPerColumn)
                {
                    continue;
                }

                hasPendingColumns = true;
                var value = columnIndex < row.Count ? row[columnIndex] : null;

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                states[columnIndex] = CombineTypes(states[columnIndex], ClassifyValue(value));
                sampleCounts[columnIndex]++;
            }

            if (!hasPendingColumns)
            {
                break;
            }
        }

        return FormatStates(states);
    }

    /// <summary>
    /// Classifies each column but stops sampling once a column becomes text.
    /// </summary>
    /// <param name="artifact">The artifact to inspect.</param>
    /// <returns>The inferred type name for each column.</returns>
    private static string[] InferAbsorbing(TabularDocumentArtifact artifact)
    {
        var headerCount = artifact?.Header?.Count ?? 0;

        if (headerCount == 0)
        {
            return [];
        }

        var states = new InferredColumnType[headerCount];
        var sampleCounts = new int[headerCount];
        var rows = artifact?.Rows ?? [];

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var hasPendingColumns = false;

            for (var columnIndex = 0; columnIndex < headerCount; columnIndex++)
            {
                if (states[columnIndex] == InferredColumnType.Text ||
                    sampleCounts[columnIndex] >= TargetSamplesPerColumn)
                {
                    continue;
                }

                hasPendingColumns = true;
                var value = columnIndex < row.Count ? row[columnIndex] : null;

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                states[columnIndex] = CombineTypes(states[columnIndex], ClassifyValue(value));
                sampleCounts[columnIndex]++;
            }

            if (!hasPendingColumns)
            {
                break;
            }
        }

        return FormatStates(states);
    }

    /// <summary>
    /// Formats each captured column state into its inferred type name.
    /// </summary>
    /// <param name="states">The captured column states.</param>
    /// <returns>The inferred type name for each column.</returns>
    private static string[] FormatStates(InferredColumnType[] states)
    {
        var inferredTypes = new string[states.Length];

        for (var index = 0; index < states.Length; index++)
        {
            inferredTypes[index] = FormatType(states[index]);
        }

        return inferredTypes;
    }

    /// <summary>
    /// Classifies a single value using the same rules as production.
    /// </summary>
    /// <param name="value">The value to classify.</param>
    /// <returns>The classified column type.</returns>
    private static InferredColumnType ClassifyValue(string value)
    {
        if (bool.TryParse(value, out _))
        {
            return InferredColumnType.Boolean;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return InferredColumnType.Integer;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            return InferredColumnType.Decimal;
        }

        if (LooksLikeDate(value) &&
            DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
        {
            return InferredColumnType.Date;
        }

        if (LooksLikeDateTime(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
        {
            return InferredColumnType.DateTime;
        }

        return InferredColumnType.Text;
    }

    /// <summary>
    /// Combines two classified types using the same promotion rules as production.
    /// </summary>
    /// <param name="current">The current column type.</param>
    /// <param name="next">The newly classified type.</param>
    /// <returns>The combined column type.</returns>
    private static InferredColumnType CombineTypes(InferredColumnType current, InferredColumnType next)
    {
        if (current == InferredColumnType.Unknown)
        {
            return next;
        }

        if (current == next)
        {
            return current;
        }

        if ((current == InferredColumnType.Integer && next == InferredColumnType.Decimal) ||
            (current == InferredColumnType.Decimal && next == InferredColumnType.Integer))
        {
            return InferredColumnType.Decimal;
        }

        if ((current == InferredColumnType.Date && next == InferredColumnType.DateTime) ||
            (current == InferredColumnType.DateTime && next == InferredColumnType.Date))
        {
            return InferredColumnType.DateTime;
        }

        return InferredColumnType.Text;
    }

    /// <summary>
    /// Formats a classified type using the same names as production.
    /// </summary>
    /// <param name="value">The classified type.</param>
    /// <returns>The inferred type name.</returns>
    private static string FormatType(InferredColumnType value)
    {
        return value switch
        {
            InferredColumnType.Boolean => "boolean",
            InferredColumnType.Integer => "integer",
            InferredColumnType.Decimal => "decimal",
            InferredColumnType.Date => "date",
            InferredColumnType.DateTime => "datetime",
            InferredColumnType.Text => "text",
            _ => "empty",
        };
    }

    /// <summary>
    /// Determines whether the value looks like a date, matching production.
    /// </summary>
    /// <param name="value">The value to inspect.</param>
    /// <returns><see langword="true"/> when the value contains a date separator.</returns>
    private static bool LooksLikeDate(string value)
    {
        return value.Contains('-', StringComparison.Ordinal) ||
            value.Contains('/', StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether the value looks like a date-time, matching production.
    /// </summary>
    /// <param name="value">The value to inspect.</param>
    /// <returns><see langword="true"/> when the value contains a date or time separator.</returns>
    private static bool LooksLikeDateTime(string value)
    {
        return LooksLikeDate(value) ||
            value.Contains(':', StringComparison.Ordinal) ||
            value.Contains('T', StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds a representative artifact with the requested column-type shape and column count.
    /// </summary>
    /// <param name="shape">The column-type shape.</param>
    /// <param name="columnCount">The number of columns.</param>
    /// <returns>The synthetic artifact.</returns>
    private static TabularDocumentArtifact CreateArtifact(InferenceShape shape, int columnCount)
    {
        var header = new List<string>(columnCount);

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            header.Add($"Column {columnIndex:D2}");
        }

        const int rowCount = 64;
        var rows = new List<List<string>>(rowCount);

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = new List<string>(columnCount);

            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                row.Add(CreateCell(shape, columnIndex, rowIndex));
            }

            rows.Add(row);
        }

        return new TabularDocumentArtifact
        {
            Header = header,
            Rows = rows,
        };
    }

    /// <summary>
    /// Creates a representative cell value for the requested shape and position.
    /// </summary>
    /// <param name="shape">The column-type shape.</param>
    /// <param name="columnIndex">The column index.</param>
    /// <param name="rowIndex">The row index.</param>
    /// <returns>The synthetic cell value.</returns>
    private static string CreateCell(InferenceShape shape, int columnIndex, int rowIndex)
    {
        var isTextColumn = shape switch
        {
            InferenceShape.AllText => true,
            InferenceShape.AllTyped => false,
            _ => columnIndex % 2 == 0,
        };

        if (isTextColumn)
        {
            return $"Value {columnIndex:D2}-{rowIndex:D2}";
        }

        return (columnIndex % 3) switch
        {
            0 => (rowIndex * 7 + columnIndex).ToString(CultureInfo.InvariantCulture),
            1 => (rowIndex + 0.5m + columnIndex).ToString(CultureInfo.InvariantCulture),
            _ => new DateOnly(2026, 1, 1).AddDays(rowIndex).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Identifies the representative artifact column-type shape.
    /// </summary>
    public enum InferenceShape
    {
        /// <summary>
        /// Every column resolves to text on its first sampled value.
        /// </summary>
        AllText,

        /// <summary>
        /// Every column resolves to a typed value that keeps sampling.
        /// </summary>
        AllTyped,

        /// <summary>
        /// Alternating text and typed columns.
        /// </summary>
        HalfTextHalfTyped,
    }

    /// <summary>
    /// Mirrors the production inferred column type states.
    /// </summary>
    private enum InferredColumnType
    {
        Unknown,
        Boolean,
        Integer,
        Decimal,
        Date,
        DateTime,
        Text,
    }
}
