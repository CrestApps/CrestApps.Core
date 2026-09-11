using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Documents.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Tools;

/// <summary>
/// Verifies <see cref="CompareTabularDataTool.BuildComparison"/> sorts by the size of the mismatch
/// rather than its sign — the fix for a live test where the largest variance sorted to the bottom of an
/// unsorted list and never reached the model's final answer, while a smaller one got called "the largest".
/// </summary>
public sealed class CompareTabularDataToolBuildComparisonTests
{
    /// <summary>
    /// Three mismatches of decreasing magnitude, two negative and one positive: -340,000 then -120,000
    /// then +70,000. Sorting by signed difference (the pre-fix behavior) would put the positive one
    /// first and the most negative one last — the opposite of what a reader scanning from the top needs.
    /// This asserts the full order, not just the first row, because a single-row assertion could pass
    /// under either sort by coincidence.
    /// </summary>
    [Fact]
    public void BuildComparison_SortsByVarianceMagnitude_NotBySign()
    {
        var left = new TabularQueryResult
        {
            Columns = ["Client", "Revenue"],
            Rows =
            [
                ["Fabrikam", 40000.00],
                ["Northwind", 1700000.00],
                ["Litware", 380000.00],
            ],
        };

        var right = new TabularQueryResult
        {
            Columns = ["Client", "Revenue"],
            Rows =
            [
                ["Fabrikam", 380000.00],
                ["Northwind", 1820000.00],
                ["Litware", 310000.00],
            ],
        };

        var comparison = CompareTabularDataTool.BuildComparison(left, right, "Left File", "Right File", allowUnmatched: true);

        var largestIndex = comparison.IndexOf("Fabrikam", StringComparison.Ordinal);
        var middleIndex = comparison.IndexOf("Northwind", StringComparison.Ordinal);
        var smallestIndex = comparison.IndexOf("Litware", StringComparison.Ordinal);

        // Under the pre-fix sort (descending by signed difference) this order is exactly reversed:
        // Litware's difference is positive so it would lead, and Fabrikam's is the most negative so it
        // would trail — this assertion fails against that code, not just this one.
        Assert.True(largestIndex < middleIndex, "Fabrikam (-340,000, the largest mismatch) must lead Northwind (-120,000).");
        Assert.True(middleIndex < smallestIndex, "Northwind (-120,000) must lead Litware (+70,000, the smallest mismatch).");
    }

    /// <summary>
    /// A key present on only one side must still be reported as unmatched, never defaulted to zero —
    /// pre-existing behavior this test protects while changing the sort above.
    /// </summary>
    [Fact]
    public void BuildComparison_KeyOnlyOnOneSide_IsReportedAsUnmatchedNotZero()
    {
        var left = new TabularQueryResult
        {
            Columns = ["Client", "Revenue"],
            Rows =
            [
                ["Northwind", 1700000.00],
                ["Tailspin Toys, Inc.", 90250.75],
            ],
        };

        var right = new TabularQueryResult
        {
            Columns = ["Client", "Revenue"],
            Rows =
            [
                ["Northwind", 1820000.00],
                ["Tailspin", 95000.00],
            ],
        };

        var comparison = CompareTabularDataTool.BuildComparison(left, right, "Left File", "Right File", allowUnmatched: true);

        Assert.Contains("Unmatched, only in \"Left File\"", comparison);
        Assert.Contains("Tailspin Toys, Inc.", comparison);

        // The bug this tool exists to prevent: reporting an unmatched key as a matched row against a
        // fabricated 0 on the other side, which reads as "this client has no revenue" instead of "these
        // two files never named this client the same way."
        Assert.DoesNotContain("Tailspin Toys, Inc. | 90250.75 | 0", comparison);
    }
}
