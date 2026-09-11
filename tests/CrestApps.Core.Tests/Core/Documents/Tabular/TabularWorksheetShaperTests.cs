using CrestApps.Core.AI.Documents.Tabular;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class TabularWorksheetShaperTests
{
    [Fact]
    public void DetectHeaderRowIndex_HeaderOnFirstRow_ReturnsZero()
    {
        List<IReadOnlyList<string>> rows =
        [
            ["Site", "Campaign", "Revenue"],
            ["Northside", "Contoso", "80000.50"],
            ["Northside", "Relecloud", "9000.00"],
        ];

        Assert.Equal(0, TabularWorksheetShaper.DetectHeaderRowIndex(rows));
    }

    [Fact]
    public void DetectHeaderRowIndex_TitleBannerAboveHeader_SkipsToRealHeader()
    {
        // Mirrors "Projections - By Client": a sparse date-band title row above the real header.
        List<IReadOnlyList<string>> rows =
        [
            ["", "", "46266", "", ""],
            ["AD", "Client Name", "Production", "Training", "Management"],
            ["Dana Reed", "Contoso", "80000.00", "0", "7000.00"],
        ];

        Assert.Equal(1, TabularWorksheetShaper.DetectHeaderRowIndex(rows));
    }

    [Fact]
    public void DetectHeaderRowIndex_AllTextTable_KeepsFirstRowAsHeader()
    {
        // A text-only table (like "AD List"): header and data both carry two labels, so the earliest
        // row must win rather than a later data row.
        List<IReadOnlyList<string>> rows =
        [
            ["Account Director", "Client Name"],
            ["Dana Reed", "Contoso - Unit A:"],
            ["Dana Reed", "Proseware"],
        ];

        Assert.Equal(0, TabularWorksheetShaper.DetectHeaderRowIndex(rows));
    }

    [Fact]
    public void DetectHeaderRowIndex_NoLabelsAnywhere_FallsBackToFirstRow()
    {
        List<IReadOnlyList<string>> rows =
        [
            ["1", "2", "3"],
            ["4", "5", "6"],
        ];

        Assert.Equal(0, TabularWorksheetShaper.DetectHeaderRowIndex(rows));
    }

    [Fact]
    public void ExpandHeader_DataWiderThanHeader_PadsWithBlanks()
    {
        List<string> header = ["Site", "Campaign"];
        List<IReadOnlyList<string>> data =
        [
            ["Northside", "Contoso", "Division A"],
        ];

        var expanded = TabularWorksheetShaper.ExpandHeader(header, data);

        Assert.Equal(3, expanded.Count);
        Assert.Equal(["Site", "Campaign", ""], expanded);
    }

    [Fact]
    public void ExpandHeader_HeaderWiderThanData_KeepsHeaderWidth()
    {
        List<string> header = ["A", "B", "C"];
        List<IReadOnlyList<string>> data = [["1", "2"]];

        var expanded = TabularWorksheetShaper.ExpandHeader(header, data);

        Assert.Equal(["A", "B", "C"], expanded);
    }

    [Theory]
    [InlineData("Totals:", true)]
    [InlineData("Rivertown Total", true)]
    [InlineData("Company Total", true)]
    [InlineData("Grand Total", true)]
    [InlineData("Subtotal", true)]
    public void IsSubtotalRow_TotalLabelWithNumericValue_ReturnsTrue(string label, bool expected)
    {
        List<string> row = [label, "1234.56"];

        Assert.Equal(expected, TabularWorksheetShaper.IsSubtotalRow(row));
    }

    [Fact]
    public void IsSubtotalRow_TotalLabelWithoutNumericValue_ReturnsFalse()
    {
        // A note row that merely mentions "total" but carries no figures is not a rollup.
        List<string> row = ["Totals to be confirmed", ""];

        Assert.False(TabularWorksheetShaper.IsSubtotalRow(row));
    }

    [Fact]
    public void IsSubtotalRow_OrdinaryDataRow_ReturnsFalse()
    {
        List<string> row = ["Northside", "Contoso", "80000.50"];

        Assert.False(TabularWorksheetShaper.IsSubtotalRow(row));
    }

    [Fact]
    public void IsSubtotalRow_NonTotalNameWithNumbers_ReturnsFalse()
    {
        List<string> row = ["Continental Widgets", "5000"];

        Assert.False(TabularWorksheetShaper.IsSubtotalRow(row));
    }

    /// <summary>
    /// Rollup rows are moved out of the data table, so a false positive does not merely mislabel a row:
    /// it removes real revenue from the figure the caller is asking for. A company whose name begins
    /// with "Total" is a record, and the label heuristic has to leave it alone.
    /// </summary>
    [Theory]
    [InlineData("Total Wine & More")]
    [InlineData("Total Quality Logistics")]
    [InlineData("Total System Services")]
    [InlineData("Totally Awesome Widgets")]
    public void IsSubtotalRow_CompanyNameBeginningWithTotal_ReturnsFalse(string name)
    {
        List<string> row = [name, "5000"];

        Assert.False(TabularWorksheetShaper.IsSubtotalRow(row));
    }

    /// <summary>
    /// The forms that genuinely are rollups: a label that is nothing but a total word, or one that ends
    /// in it. Both appear in the reported workbook.
    /// </summary>
    [Theory]
    [InlineData("Total")]
    [InlineData("Totals:")]
    [InlineData("Grand Total")]
    [InlineData("Sub-total")]
    [InlineData("Northside Total")]
    [InlineData("Extra Site 1 Total")]
    public void IsSubtotalRow_RollupLabel_ReturnsTrue(string label)
    {
        List<string> row = [label, "5000"];

        Assert.True(TabularWorksheetShaper.IsSubtotalRow(row));
    }

    /// <summary>
    /// Formula evidence outranks the label. A sheet that names its rollup rows after the group they
    /// cover -- "Region A" rather than "Region A Total" -- still imports correctly, because the row's
    /// own arithmetic says what it is.
    /// </summary>
    [Fact]
    public void IsSubtotalRow_VerticalAggregateFormulaWithoutTotalLabel_ReturnsTrue()
    {
        List<string> row = ["Region A", "300"];

        Assert.False(TabularWorksheetShaper.IsSubtotalRow(row));
        Assert.True(TabularWorksheetShaper.IsSubtotalRow(row, hasVerticalAggregateFormula: true));
    }

    /// <summary>
    /// Absent formula evidence the overload must not soften the label heuristic, so a delimited source
    /// -- which never has formulas -- behaves exactly as before.
    /// </summary>
    [Fact]
    public void IsSubtotalRow_NoFormulaEvidence_FallsBackToTheLabelHeuristic()
    {
        Assert.True(TabularWorksheetShaper.IsSubtotalRow(["Rivertown Total", "5000"], hasVerticalAggregateFormula: false));
        Assert.False(TabularWorksheetShaper.IsSubtotalRow(["Rivertown", "5000"], hasVerticalAggregateFormula: false));
    }

    [Fact]
    public void GetRollupTableName_AppendsTheRollupSuffix()
    {
        Assert.Equal("Client_Breakdown_rollups", TabularWorksheetShaper.GetRollupTableName("Client_Breakdown"));
    }
}
