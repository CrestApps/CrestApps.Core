using System.Globalization;
using System.Reflection;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Documents.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Tools;

/// <summary>
/// Verifies the exact tabular query result formatting contract.
/// </summary>
public sealed class QueryTabularDataToolTests
{
    private static readonly MethodInfo _formatResult = typeof(QueryTabularDataTool)
        .GetMethod("FormatResult", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Unable to find the tabular query result formatter.");

    /// <summary>
    /// Verifies the exact empty-result marker.
    /// </summary>
    [Fact]
    public void FormatResult_WhenThereAreNoRows_ReturnsEmptyResultMarker()
    {
        var result = new TabularQueryResult
        {
            Columns = ["ignored"],
            Rows = [],
            Truncated = true,
        };

        Assert.Equal("The query returned no rows.", FormatResult(result));
    }

    /// <summary>
    /// Verifies singular row wording, separators, blank lines, and the trailing line ending.
    /// </summary>
    [Fact]
    public void FormatResult_WithOneRow_PreservesExactLayout()
    {
        var result = new TabularQueryResult
        {
            Columns = ["Name", "Value"],
            Rows =
            [
                ["Ada", "42"],
            ],
        };
        var newLine = Environment.NewLine;
        var expected = $"Returned 1 row:{newLine}{newLine}Name | Value{newLine}Ada | 42{newLine}";

        Assert.Equal(expected, FormatResult(result));
    }

    /// <summary>
    /// Verifies invariant formatting for formattable cells while the current culture uses decimal commas.
    /// </summary>
    [Fact]
    public void FormatResult_WithMixedCells_UsesInvariantCultureAndPreservesNulls()
    {
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

            var result = new TabularQueryResult
            {
                Columns = ["Amount", "Ratio", "Provider", "Plain", "Missing"],
                Rows =
                [
                    [1234.5m, 1.25d, new ProviderAwareCell(), new PlainCell(), null],
                ],
            };
            var newLine = Environment.NewLine;
            var expected = string.Concat(
                "Returned 1 row:", newLine, newLine,
                "Amount | Ratio | Provider | Plain | Missing", newLine,
                "1234.5 | 1.25 | invariant | plain | ", newLine);

            Assert.Equal(expected, FormatResult(result));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// Verifies plural and truncation wording while retaining each ragged row's actual cell count.
    /// </summary>
    [Fact]
    public void FormatResult_WithTruncatedRaggedRows_PreservesExactCellsAndLayout()
    {
        var result = new TabularQueryResult
        {
            Columns = ["A", "B", "C"],
            Rows =
            [
                [],
                ["one"],
                ["one", null, "three", "extra"],
            ],
            Truncated = true,
        };
        var newLine = Environment.NewLine;
        var expected = string.Concat(
            "Returned 3 rows (truncated to the row limit; refine the query with aggregation or LIMIT for the full picture):",
            newLine,
            newLine,
            "A | B | C",
            newLine,
            newLine,
            "one",
            newLine,
            "one |  | three | extra",
            newLine);

        Assert.Equal(expected, FormatResult(result));
    }

    /// <summary>
    /// Invokes the production tabular query result formatter.
    /// </summary>
    /// <param name="result">The query result to format.</param>
    /// <returns>The formatted query result.</returns>
    private static string FormatResult(TabularQueryResult result)
    {
        return (string)_formatResult.Invoke(null, [result]);
    }

    private sealed class ProviderAwareCell : IFormattable
    {
        /// <summary>
        /// Formats the value and reports whether the invariant provider was supplied.
        /// </summary>
        /// <param name="format">The requested format.</param>
        /// <param name="formatProvider">The requested format provider.</param>
        /// <returns>A marker describing the supplied provider.</returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            return ReferenceEquals(formatProvider, CultureInfo.InvariantCulture)
                ? "invariant"
                : "non-invariant";
        }
    }

    private sealed class PlainCell
    {
        /// <summary>
        /// Returns the plain non-formattable cell value.
        /// </summary>
        /// <returns>The cell text.</returns>
        public override string ToString()
        {
            return "plain";
        }
    }
}
