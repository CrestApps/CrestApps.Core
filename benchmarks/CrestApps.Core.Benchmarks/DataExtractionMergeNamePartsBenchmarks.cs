using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Chat.Services;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures exact-equivalent name-part merging implementations across representative name shapes.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class DataExtractionMergeNamePartsBenchmarks
{
    private const int OperationsPerInvocation = 1_000;
    private string _existingValue;
    private string _newValue;
    private DataExtractionService.ExtractionFieldKind _resultFieldKind;

    /// <summary>
    /// Gets or sets the name shape used by the benchmark.
    /// </summary>
    [Params(
        NameShape.ShortRealistic,
        NameShape.PunctuationHeavy,
        NameShape.Unicode,
        NameShape.LongMultiPart)]
    public NameShape Shape { get; set; }

    /// <summary>
    /// Configures representative inputs and verifies both experiments against the captured implementation.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        (_existingValue, _newValue, _resultFieldKind) = Shape switch
        {
            NameShape.ShortRealistic => (
                "Ada Lovelace",
                "Augusta",
                DataExtractionService.ExtractionFieldKind.FirstName),
            NameShape.PunctuationHeavy => (
                "Dr. Jean-Luc O'Neill, Jr.",
                "Smith-Jones (III)",
                DataExtractionService.ExtractionFieldKind.LastName),
            NameShape.Unicode => (
                "李 小龍",
                "김민수",
                DataExtractionService.ExtractionFieldKind.FirstName),
            NameShape.LongMultiPart => (
                "María de los Ángeles del Río y Fernández von Habsburg-Lothringen al-Saud bin Abdulaziz",
                "O'Connor-Smith",
                DataExtractionService.ExtractionFieldKind.LastName),
            _ => throw new InvalidOperationException($"Unsupported name shape '{Shape}'."),
        };

        var expected = MergeLegacy(_existingValue, _newValue, _resultFieldKind);

        if (!string.Equals(expected, MergeCurrent(_existingValue, _newValue, _resultFieldKind), StringComparison.Ordinal) ||
            !string.Equals(expected, MergeManualSpan(_existingValue, _newValue, _resultFieldKind), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Name merging experiment changed output for '{Shape}'.");
        }
    }

    /// <summary>
    /// Runs the captured Split, LINQ, collection-expression, and Join implementation repeatedly.
    /// </summary>
    /// <returns>The combined output length.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = OperationsPerInvocation)]
    public int MergeLegacyAtScale()
    {
        var length = 0;

        for (var index = 0; index < OperationsPerInvocation; index++)
        {
            length += MergeLegacy(_existingValue, _newValue, _resultFieldKind).Length;
        }

        return length;
    }

    /// <summary>
    /// Runs the simpler Split implementation that replaces the relevant array element before Join.
    /// </summary>
    /// <returns>The combined output length.</returns>
    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public int MergeCurrentAtScale()
    {
        var length = 0;

        for (var index = 0; index < OperationsPerInvocation; index++)
        {
            length += MergeCurrent(_existingValue, _newValue, _resultFieldKind).Length;
        }

        return length;
    }

    /// <summary>
    /// Runs the allocation-reducing manual literal-space scanner repeatedly.
    /// </summary>
    /// <returns>The combined output length.</returns>
    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public int MergeManualSpanExperimentAtScale()
    {
        var length = 0;

        for (var index = 0; index < OperationsPerInvocation; index++)
        {
            length += MergeManualSpan(_existingValue, _newValue, _resultFieldKind).Length;
        }

        return length;
    }

    /// <summary>
    /// Captures the production implementation before local name-merging optimization.
    /// </summary>
    /// <param name="existingValue">The existing extracted value.</param>
    /// <param name="newValue">The incoming name part.</param>
    /// <param name="resultFieldKind">The incoming field kind.</param>
    /// <returns>The merged name.</returns>
    private static string MergeLegacy(
        string existingValue,
        string newValue,
        DataExtractionService.ExtractionFieldKind resultFieldKind)
    {
        if (string.IsNullOrWhiteSpace(newValue))
        {
            return existingValue;
        }

        if (string.IsNullOrWhiteSpace(existingValue))
        {
            return newValue.Trim();
        }

        var existingParts = existingValue.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var incomingValue = newValue.Trim();

        if (resultFieldKind == DataExtractionService.ExtractionFieldKind.FirstName)
        {
            return existingParts.Length > 1
                ? string.Join(' ', [incomingValue, .. existingParts.Skip(1)])
                : incomingValue;
        }

        if (existingParts.Length > 1)
        {
            return string.Join(' ', [.. existingParts.Take(existingParts.Length - 1), incomingValue]);
        }

        return string.Concat(existingParts[0], " ", incomingValue);
    }

    /// <summary>
    /// Reuses the array produced by Split instead of projecting its elements into a second array.
    /// </summary>
    /// <param name="existingValue">The existing extracted value.</param>
    /// <param name="newValue">The incoming name part.</param>
    /// <param name="resultFieldKind">The incoming field kind.</param>
    /// <returns>The merged name.</returns>
    private static string MergeCurrent(
        string existingValue,
        string newValue,
        DataExtractionService.ExtractionFieldKind resultFieldKind)
    {
        if (string.IsNullOrWhiteSpace(newValue))
        {
            return existingValue;
        }

        if (string.IsNullOrWhiteSpace(existingValue))
        {
            return newValue.Trim();
        }

        var existingParts = existingValue.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var incomingValue = newValue.Trim();

        if (resultFieldKind == DataExtractionService.ExtractionFieldKind.FirstName)
        {
            if (existingParts.Length == 1)
            {
                return incomingValue;
            }

            existingParts[0] = incomingValue;

            return string.Join(' ', existingParts);
        }

        if (existingParts.Length > 1)
        {
            existingParts[^1] = incomingValue;

            return string.Join(' ', existingParts);
        }

        return string.Concat(existingParts[0], " ", incomingValue);
    }

    /// <summary>
    /// Scans literal-space-delimited parts without allocating an array or individual substrings.
    /// </summary>
    /// <param name="existingValue">The existing extracted value.</param>
    /// <param name="newValue">The incoming name part.</param>
    /// <param name="resultFieldKind">The incoming field kind.</param>
    /// <returns>The merged name.</returns>
    private static string MergeManualSpan(
        string existingValue,
        string newValue,
        DataExtractionService.ExtractionFieldKind resultFieldKind)
    {
        if (string.IsNullOrWhiteSpace(newValue))
        {
            return existingValue;
        }

        if (string.IsNullOrWhiteSpace(existingValue))
        {
            return newValue.Trim();
        }

        var incomingValue = newValue.Trim();
        var position = 0;
        _ = TryReadNextNamePart(existingValue, ref position, out var firstStart, out var firstLength);

        if (resultFieldKind == DataExtractionService.ExtractionFieldKind.FirstName)
        {
            if (!TryReadNextNamePart(existingValue, ref position, out var partStart, out var partLength))
            {
                return incomingValue;
            }

            var builder = new StringBuilder(existingValue.Length + incomingValue.Length);
            builder.Append(incomingValue);
            builder.Append(' ');
            builder.Append(existingValue, partStart, partLength);

            while (TryReadNextNamePart(existingValue, ref position, out partStart, out partLength))
            {
                builder.Append(' ');
                builder.Append(existingValue, partStart, partLength);
            }

            return builder.ToString();
        }

        if (!TryReadNextNamePart(existingValue, ref position, out var pendingStart, out var pendingLength))
        {
            return string.Concat(existingValue.AsSpan(firstStart, firstLength), " ", incomingValue);
        }

        var prefixBuilder = new StringBuilder(existingValue.Length + incomingValue.Length);
        prefixBuilder.Append(existingValue, firstStart, firstLength);

        while (TryReadNextNamePart(existingValue, ref position, out var partStart, out var partLength))
        {
            prefixBuilder.Append(' ');
            prefixBuilder.Append(existingValue, pendingStart, pendingLength);
            pendingStart = partStart;
            pendingLength = partLength;
        }

        prefixBuilder.Append(' ');
        prefixBuilder.Append(incomingValue);

        return prefixBuilder.ToString();
    }

    /// <summary>
    /// Reads the next non-empty part using the same literal-space split and Unicode trimming rules as String.Split.
    /// </summary>
    /// <param name="value">The value being scanned.</param>
    /// <param name="position">The next scan position.</param>
    /// <param name="partStart">The trimmed part start.</param>
    /// <param name="partLength">The trimmed part length.</param>
    /// <returns><see langword="true"/> when a non-empty part was found.</returns>
    private static bool TryReadNextNamePart(
        string value,
        ref int position,
        out int partStart,
        out int partLength)
    {
        while (position <= value.Length)
        {
            var segmentStart = position;
            var separatorOffset = value.AsSpan(position).IndexOf(' ');
            var segmentEnd = separatorOffset < 0
                ? value.Length
                : position + separatorOffset;
            position = separatorOffset < 0
                ? value.Length + 1
                : segmentEnd + 1;

            while (segmentStart < segmentEnd && char.IsWhiteSpace(value[segmentStart]))
            {
                segmentStart++;
            }

            while (segmentEnd > segmentStart && char.IsWhiteSpace(value[segmentEnd - 1]))
            {
                segmentEnd--;
            }

            if (segmentEnd > segmentStart)
            {
                partStart = segmentStart;
                partLength = segmentEnd - segmentStart;

                return true;
            }
        }

        partStart = 0;
        partLength = 0;

        return false;
    }

    /// <summary>
    /// Identifies the representative name input shape.
    /// </summary>
    public enum NameShape
    {
        /// <summary>
        /// A common two-part Latin name.
        /// </summary>
        ShortRealistic,

        /// <summary>
        /// A title and punctuation-heavy multi-part name.
        /// </summary>
        PunctuationHeavy,

        /// <summary>
        /// A non-Latin Unicode name.
        /// </summary>
        Unicode,

        /// <summary>
        /// A long name containing many literal-space-delimited parts.
        /// </summary>
        LongMultiPart,
    }
}
