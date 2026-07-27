using System.Globalization;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Documents.Tools;
using Cysharp.Text;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured <c>string.Join</c>/LINQ tabular query result formatter with a direct
/// row-formatting candidate that appends each cell straight into the shared string builder.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class TabularQueryResultFormattingBenchmarks
{
    private const int OperationsPerInvocation = 1_000;

    private static readonly MethodInfo _productionFormatResult = typeof(QueryTabularDataTool)
        .GetMethod("FormatResult", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Unable to find the production tabular query result formatter.");

    private TabularQueryResult _result;

    /// <summary>
    /// Gets or sets the query result shape used by the benchmark.
    /// </summary>
    [Params(
        QueryResultShape.CompactAggregation,
        QueryResultShape.MaxStringRows,
        QueryResultShape.MaxMixedRows)]
    public QueryResultShape Shape { get; set; }

    /// <summary>
    /// Builds the representative result and verifies exact production/legacy/direct equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _result = CreateResult(Shape);

        var production = (string)_productionFormatResult.Invoke(null, [_result]);
        var legacy = FormatLegacy(_result);
        var direct = FormatDirect(_result);

        if (!string.Equals(production, legacy, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Legacy reproduction diverged from production for '{Shape}'.");
        }

        if (!string.Equals(production, direct, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Direct candidate diverged from production for '{Shape}'.");
        }
    }

    /// <summary>
    /// Runs the captured <c>string.Join</c>/LINQ formatter repeatedly.
    /// </summary>
    /// <returns>The accumulated output length.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = OperationsPerInvocation)]
    public int FormatLegacyAtScale()
    {
        var length = 0;

        for (var index = 0; index < OperationsPerInvocation; index++)
        {
            length += FormatLegacy(_result).Length;
        }

        return length;
    }

    /// <summary>
    /// Runs the direct row-formatting candidate repeatedly.
    /// </summary>
    /// <returns>The accumulated output length.</returns>
    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public int FormatDirectAtScale()
    {
        var length = 0;

        for (var index = 0; index < OperationsPerInvocation; index++)
        {
            length += FormatDirect(_result).Length;
        }

        return length;
    }

    /// <summary>
    /// Reproduces the captured production formatter that joins each row with LINQ projection.
    /// </summary>
    /// <param name="result">The query result to format.</param>
    /// <returns>The formatted result.</returns>
    private static string FormatLegacy(TabularQueryResult result)
    {
        if (result.Rows.Count == 0)
        {
            return "The query returned no rows.";
        }

        using var builder = ZString.CreateStringBuilder();

        builder.Append("Returned ");
        builder.Append(result.Rows.Count);
        builder.Append(result.Rows.Count == 1 ? " row" : " rows");

        if (result.Truncated)
        {
            builder.Append(" (truncated to the row limit; refine the query with aggregation or LIMIT for the full picture)");
        }

        builder.AppendLine(":");
        builder.AppendLine();
        builder.AppendLine(string.Join(" | ", result.Columns));

        foreach (var row in result.Rows)
        {
            builder.AppendLine(string.Join(" | ", row.Select(FormatCell)));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats the result by appending each cell directly into the shared builder.
    /// </summary>
    /// <param name="result">The query result to format.</param>
    /// <returns>The formatted result.</returns>
    private static string FormatDirect(TabularQueryResult result)
    {
        if (result.Rows.Count == 0)
        {
            return "The query returned no rows.";
        }

        using var builder = ZString.CreateStringBuilder();

        builder.Append("Returned ");
        builder.Append(result.Rows.Count);
        builder.Append(result.Rows.Count == 1 ? " row" : " rows");

        if (result.Truncated)
        {
            builder.Append(" (truncated to the row limit; refine the query with aggregation or LIMIT for the full picture)");
        }

        builder.AppendLine(":");
        builder.AppendLine();

        var columns = result.Columns;

        for (var index = 0; index < columns.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(columns[index]);
        }

        builder.AppendLine();

        foreach (var row in result.Rows)
        {
            for (var index = 0; index < row.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(" | ");
                }

                switch (row[index])
                {
                    case null:
                        break;
                    case string text:
                        builder.Append(text);
                        break;
                    case IFormattable formattable:
                        builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                        break;
                    default:
                        builder.Append(row[index].ToString());
                        break;
                }
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats a single cell exactly as the captured production formatter does.
    /// </summary>
    /// <param name="cell">The cell value.</param>
    /// <returns>The formatted cell text.</returns>
    private static string FormatCell(object cell)
    {
        return cell switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => cell.ToString(),
        };
    }

    /// <summary>
    /// Builds a representative query result for the requested shape.
    /// </summary>
    /// <param name="shape">The result shape.</param>
    /// <returns>The synthetic query result.</returns>
    private static TabularQueryResult CreateResult(QueryResultShape shape)
    {
        return shape switch
        {
            QueryResultShape.CompactAggregation => new TabularQueryResult
            {
                Columns = ["Region", "Orders", "Revenue"],
                Rows = BuildRows(5, ["North America", 128L, 4820.75m]),
                Truncated = false,
            },
            QueryResultShape.MaxStringRows => new TabularQueryResult
            {
                Columns = ["First Name", "Last Name", "City", "Country", "Segment", "Status"],
                Rows = BuildRows(100, ["Alexandra", "Featherstone", "San Francisco", "United States", "Enterprise", "Active"]),
                Truncated = true,
            },
            QueryResultShape.MaxMixedRows => new TabularQueryResult
            {
                Columns = ["Order Id", "Customer", "Amount", "Ratio", "Notes", "Cancelled"],
                Rows = BuildRows(100, [10_482L, "Northwind Traders", 1284.55m, 0.184623d, null, "Fulfilled"]),
                Truncated = true,
            },
            _ => throw new InvalidOperationException($"Unsupported query result shape '{shape}'."),
        };
    }

    /// <summary>
    /// Replicates the supplied template row into the requested number of rows.
    /// </summary>
    /// <param name="rowCount">The number of rows.</param>
    /// <param name="template">The per-column template values.</param>
    /// <returns>The synthetic rows.</returns>
    private static object[][] BuildRows(int rowCount, object[] template)
    {
        var rows = new object[rowCount][];

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            rows[rowIndex] = (object[])template.Clone();
        }

        return rows;
    }

    /// <summary>
    /// Identifies the representative query result shape.
    /// </summary>
    public enum QueryResultShape
    {
        /// <summary>
        /// A small aggregation result with mixed cell types.
        /// </summary>
        CompactAggregation,

        /// <summary>
        /// A result at the default row cap containing only string cells.
        /// </summary>
        MaxStringRows,

        /// <summary>
        /// A result at the default row cap containing mixed string, numeric, and null cells.
        /// </summary>
        MaxMixedRows,
    }
}
