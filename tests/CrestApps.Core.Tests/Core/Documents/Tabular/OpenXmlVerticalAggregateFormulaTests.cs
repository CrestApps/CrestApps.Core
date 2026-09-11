using CrestApps.Core.AI.Documents.OpenXml.Services;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

/// <summary>
/// Unit tests for the signal that separates a rollup row from a record: whether a cell's formula
/// aggregates rows other than its own.
/// </summary>
/// <remarks>
/// A label is weak evidence -- sheets label rollups inconsistently, and real companies are named
/// "Total Wine &amp; More" -- but a formula states the row's arithmetic outright. The distinction that
/// matters is direction. A vertical aggregate (<c>=SUM(C2:C27)</c> on row 28) restates rows that are
/// already in the table, so counting it again inflates the answer. A horizontal one
/// (<c>=SUM(G27,E27,H27)</c> on row 27) is a line total across that row's own columns and sums
/// correctly down the column, so it has to be kept.
/// </remarks>
public sealed class OpenXmlVerticalAggregateFormulaTests
{
    [Theory]
    // Range over earlier rows -- the classic subtotal beneath the rows it covers.
    [InlineData("SUM(C2:C27)", 28)]
    // Enumerated references to other rows -- a grand total over the site subtotals.
    [InlineData("SUM(C70,C57,C47,C28,C78,C84)", 85)]
    // Excel's own outline/table-total function.
    [InlineData("SUBTOTAL(109,C2:C27)", 28)]
    [InlineData("AGGREGATE(9,1,C2:C27)", 28)]
    // Absolute and sheet-qualified references still resolve to other rows.
    [InlineData("SUM($C$2:$C$27)", 28)]
    // Case is not significant in a formula.
    [InlineData("sum(C2:C27)", 28)]
    public void IsVerticalAggregateFormula_AggregateOverOtherRows_ReturnsTrue(string formula, int rowNumber)
    {
        Assert.True(OpenXmlTabularWorksheetReader.IsVerticalAggregateFormula(formula, rowNumber));
    }

    [Theory]
    // A line total across this row's own columns: an ordinary record.
    [InlineData("SUM(G27,E27,H27)", 27)]
    [InlineData("SUM(B2:F2)", 2)]
    // A ratio, not an aggregate at all.
    [InlineData("E28/D28", 28)]
    // References another row but performs no aggregation -- a growth or variance cell, not a rollup.
    [InlineData("B27*1.1", 28)]
    [InlineData("C28-C27", 28)]
    // No references at all.
    [InlineData("SUM(1,2,3)", 5)]
    [InlineData("", 5)]
    public void IsVerticalAggregateFormula_NotAnAggregateOverOtherRows_ReturnsFalse(string formula, int rowNumber)
    {
        Assert.False(OpenXmlTabularWorksheetReader.IsVerticalAggregateFormula(formula, rowNumber));
    }

    /// <summary>
    /// The two forms taken straight from the reported workbook, on adjacent rows: row 27 is a client
    /// line whose Total column adds up that client's own figures, and row 28 is the site subtotal
    /// underneath it. Only the second may leave the data table.
    /// </summary>
    [Fact]
    public void IsVerticalAggregateFormula_AdjacentRowTotalAndSubtotal_AreClassifiedDifferently()
    {
        Assert.False(OpenXmlTabularWorksheetReader.IsVerticalAggregateFormula("SUM(G27,E27,H27)", 27));
        Assert.True(OpenXmlTabularWorksheetReader.IsVerticalAggregateFormula("SUM(E2:E27)", 28));
    }

    /// <summary>
    /// A criteria-based lookup that pulls a figure in from another worksheet is not a rollup, however
    /// many rows its arguments name.
    /// </summary>
    /// <remarks>
    /// This is the shape that appears on every single record of the reported master workbook -- actuals
    /// pulled alongside projections -- and getting it wrong is far worse than missing a rollup: it
    /// classifies an entire sheet of genuine client rows as rollups and empties the data table. Two
    /// independent guards cover it. The function is not in the aggregate set, and the references it
    /// names are dropped before the row comparison because they are worksheet-qualified: including the
    /// criteria cell on the header row, which is otherwise "another row" for every record in the sheet.
    /// </remarks>
    [Theory]
    [InlineData("SUMIFS(Revenue!$K:$K,Revenue!$G:$G,'Projections - By Client'!$B3,Revenue!$J:$J,'Projections - By Client'!C$2)", 3)]
    [InlineData("SUMIF(Revenue!$G:$G,$B12,Revenue!$K:$K)", 12)]
    [InlineData("SUMPRODUCT(Revenue!$K$2:$K$500,Revenue!$G$2:$G$500)", 7)]
    public void IsVerticalAggregateFormula_CrossSheetLookup_ReturnsFalse(string formula, int rowNumber)
    {
        Assert.False(OpenXmlTabularWorksheetReader.IsVerticalAggregateFormula(formula, rowNumber));
    }

    /// <summary>
    /// A plain SUM whose only cross-row reference lives on another worksheet is reading a different
    /// table, so it says nothing about this row restating its neighbours.
    /// </summary>
    [Fact]
    public void IsVerticalAggregateFormula_SumOverAnotherWorksheet_ReturnsFalse()
    {
        Assert.False(OpenXmlTabularWorksheetReader.IsVerticalAggregateFormula("SUM(Revenue!$K$2:$K$500)", 7));
        Assert.False(OpenXmlTabularWorksheetReader.IsVerticalAggregateFormula("SUM('Client Breakdown'!I2:I27)", 7));
    }
}
