using CrestApps.Core.AI.Documents.Tabular;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

/// <summary>
/// Verifies <see cref="TabularColumnAmbiguityDetector"/> against the real column shapes it was built to
/// disambiguate, and against the real column shapes it must stay silent on.
/// </summary>
public sealed class TabularColumnAmbiguityDetectorTests
{
    /// <summary>
    /// The exact bug this detector exists to prevent: a live test picked "Projected_Revenue" instead of
    /// "Total_Revenue" (= Projected + Ancillary + AI_Bot), understating every affected row. The four
    /// columns share the "Revenue" suffix and exactly one of them reads as a total, so this must fire.
    /// </summary>
    [Fact]
    public void DescribeAmbiguousTotals_FlagsTheRealRevenueGroup()
    {
        var columns = new List<(string Name, bool IsNumeric)>
        {
            ("Site", false),
            ("Campaign", false),
            ("Billable_Hours", true),
            ("Projected_Hours", true),
            ("Projected_Revenue", true),
            ("Projected_RPH", true),
            ("Ancillary_Revenue", true),
            ("AI_Bot_Revenue", true),
            ("Total_Revenue", true),
        };

        var note = TabularColumnAmbiguityDetector.DescribeAmbiguousTotals(columns);

        Assert.NotNull(note);
        Assert.Contains("Total_Revenue", note);
        Assert.Contains("Projected_Revenue", note);
        Assert.Contains("Ancillary_Revenue", note);
        Assert.Contains("AI_Bot_Revenue", note);
    }

    /// <summary>
    /// The sibling "Hours" group in the same real table has no member that reads as a total, so nothing
    /// should be said about it even though the group has two or more members.
    /// </summary>
    [Fact]
    public void DescribeAmbiguousTotals_StaysSilentForAGroupWithNoTotalMember()
    {
        var columns = new List<(string Name, bool IsNumeric)>
        {
            ("Billable_Hours", true),
            ("Projected_Hours", true),
        };

        Assert.Null(TabularColumnAmbiguityDetector.DescribeAmbiguousTotals(columns));
    }

    /// <summary>
    /// Regression test for the real spanning-header-band shape: a workbook with three monthly bands
    /// (September, October, November), each repeating the same measure labels ("Production", "Total
    /// Proj. Revnue", etc.), gets its duplicate column names qualified with the band date
    /// (<see cref="TabularWorksheetShaper.FixDuplicateColumnNames"/>), producing names like
    /// <c>Total_Proj__Revnue_2026_09_01</c> for every month. The trailing date segment is not a shared
    /// topic, it is a disambiguator, so grouping on it must not treat "one total per month" as an
    /// ambiguous group. This table never tripped the model in testing, and this proves why: every
    /// numeric column here ends in an all-digit segment, so none of them form a group at all.
    /// </summary>
    [Fact]
    public void DescribeAmbiguousTotals_StaysSilentForRealDateSuffixedMonthlyBands()
    {
        var columns = new List<(string Name, bool IsNumeric)>
        {
            ("CSD", false),
            ("Client_Name", false),
            ("Production_2026_09_01", true),
            ("Training_2026_09_01", true),
            ("Management_2026_09_01", true),
            ("AI_2026_09_01", true),
            ("Ancillary_2026_09_01", true),
            ("Total_Proj__Revnue_2026_09_01", true),
            ("Act__Revenue_2026_09_01", true),
            ("Comp__to_Actual_2026_09_01", true),
            ("Production_2026_10_01", true),
            ("Training_2026_10_01", true),
            ("Management_2026_10_01", true),
            ("AI_2026_10_01", true),
            ("Ancillary_2026_10_01", true),
            ("Total_Proj__Revnue_2026_10_01", true),
            ("Act__Revenue_2026_10_01", true),
            ("Comp__to_Actual_2026_10_01", true),
        };

        Assert.Null(TabularColumnAmbiguityDetector.DescribeAmbiguousTotals(columns));
    }

    /// <summary>
    /// A group where more than one member reads as a total is itself ambiguous — there is no single
    /// column to point at, so this must also stay silent rather than guess.
    /// </summary>
    [Fact]
    public void DescribeAmbiguousTotals_StaysSilentWhenMultipleGroupMembersLookLikeTotals()
    {
        var columns = new List<(string Name, bool IsNumeric)>
        {
            ("Total_Revenue", true),
            ("Grand_Total_Revenue", true),
        };

        Assert.Null(TabularColumnAmbiguityDetector.DescribeAmbiguousTotals(columns));
    }

    /// <summary>
    /// A non-numeric column must never join a group, even if its name would otherwise match — a text
    /// column named "Total_Category" has nothing to do with a numeric revenue total.
    /// </summary>
    [Fact]
    public void DescribeAmbiguousTotals_IgnoresNonNumericColumnsEntirely()
    {
        var columns = new List<(string Name, bool IsNumeric)>
        {
            ("Projected_Revenue", true),
            ("Total_Revenue", true),
            ("Total_Category", false),
        };

        var note = TabularColumnAmbiguityDetector.DescribeAmbiguousTotals(columns);

        Assert.NotNull(note);
        Assert.DoesNotContain("Total_Category", note);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DescribeAmbiguousTotals_WithFewerThanTwoColumns_ReturnsNull(int columnCount)
    {
        var columns = Enumerable.Range(0, columnCount)
            .Select(index => ($"Total_Revenue_{index}", true))
            .ToList();

        Assert.Null(TabularColumnAmbiguityDetector.DescribeAmbiguousTotals(columns));
    }
}
