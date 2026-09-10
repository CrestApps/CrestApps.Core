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
    /// Reproduces the exact real numbers from testing: Learning Care Group's mismatch (-338,020.42) is
    /// larger in magnitude than Eli Lilly's (-116,590.27), which is larger than Track Group's
    /// (+70,111.78). Sorting by signed difference (the pre-fix behavior) would put the positive Track
    /// Group first and the deeply negative Learning Care Group last — the opposite of what a reader
    /// scanning from the top needs to see. This asserts the full order, not just the first row, because a
    /// single-row assertion could pass under either sort by coincidence.
    /// </summary>
    [Fact]
    public void BuildComparison_SortsByVarianceMagnitude_NotBySign()
    {
        var left = new TabularQueryResult
        {
            Columns = ["Client", "Revenue"],
            Rows =
            [
                ["Learning Care Group", 37297.68],
                ["Eli Lilly", 1690423.00],
                ["Track Group", 379974.00],
            ],
        };

        var right = new TabularQueryResult
        {
            Columns = ["Client", "Revenue"],
            Rows =
            [
                ["Learning Care Group", 375318.10],
                ["Eli Lilly", 1807013.27],
                ["Track Group", 309862.22],
            ],
        };

        var comparison = CompareTabularDataTool.BuildComparison(left, right, "Client Services", "Site Director", allowUnmatched: true);

        var learningCareGroupIndex = comparison.IndexOf("Learning Care Group", StringComparison.Ordinal);
        var eliLillyIndex = comparison.IndexOf("Eli Lilly", StringComparison.Ordinal);
        var trackGroupIndex = comparison.IndexOf("Track Group", StringComparison.Ordinal);

        // Under the pre-fix sort (descending by signed difference), this order is exactly reversed:
        // Track Group's difference is positive so it would lead, and Learning Care Group's is the most
        // negative so it would trail — this assertion fails against that code, not just this one.
        Assert.True(learningCareGroupIndex < eliLillyIndex, "Learning Care Group (-338,020, the largest mismatch) must lead Eli Lilly (-116,590).");
        Assert.True(eliLillyIndex < trackGroupIndex, "Eli Lilly (-116,590) must lead Track Group (+70,112, the smallest mismatch).");
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
                ["Eli Lilly", 1690423.00],
                ["LendKey Technologies, Inc.", 84407.76],
            ],
        };

        var right = new TabularQueryResult
        {
            Columns = ["Client", "Revenue"],
            Rows =
            [
                ["Eli Lilly", 1807013.27],
                ["LendKey", 85047.53],
            ],
        };

        var comparison = CompareTabularDataTool.BuildComparison(left, right, "Client Services", "Site Director", allowUnmatched: true);

        Assert.Contains("Unmatched, only in \"Client Services\"", comparison);
        Assert.Contains("LendKey Technologies, Inc.", comparison);

        // The bug this tool exists to prevent: reporting an unmatched key as a matched row against a
        // fabricated 0 on the other side, which reads as "this client has no revenue" instead of "these
        // two files never named this client the same way."
        Assert.DoesNotContain("LendKey Technologies, Inc. | 84407.76 | 0", comparison);
    }
}
