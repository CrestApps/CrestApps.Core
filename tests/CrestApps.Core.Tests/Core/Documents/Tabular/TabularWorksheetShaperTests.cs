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
            ["True Blue", "BayCare", "71822.68"],
            ["True Blue", "CARE", "9137.29"],
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
            ["CSD", "Client Name", "Production", "Training", "Management"],
            ["Alicia Welage", "BayCare", "74612.15", "0", "6452.88"],
        ];

        Assert.Equal(1, TabularWorksheetShaper.DetectHeaderRowIndex(rows));
    }

    [Fact]
    public void DetectHeaderRowIndex_AllTextTable_KeepsFirstRowAsHeader()
    {
        // A text-only table (like "CSD List"): header and data both carry two labels, so the earliest
        // row must win rather than a later data row.
        List<IReadOnlyList<string>> rows =
        [
            ["Client Service Director", "Client Name"],
            ["Alicia Welage", "BayCare - Cardiac Access:"],
            ["Alicia Welage", "Hallmark"],
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
            ["True Blue", "BayCare", "Imaging"],
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
    [InlineData("Waco Total", true)]
    [InlineData("RDI Total", true)]
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
        List<string> row = ["True Blue", "BayCare", "71822.68"];

        Assert.False(TabularWorksheetShaper.IsSubtotalRow(row));
    }

    [Fact]
    public void IsSubtotalRow_NonTotalNameWithNumbers_ReturnsFalse()
    {
        List<string> row = ["Continental Widgets", "5000"];

        Assert.False(TabularWorksheetShaper.IsSubtotalRow(row));
    }
}
