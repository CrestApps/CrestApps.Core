using CrestApps.Core.AI.Documents.Tabular;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

/// <summary>
/// Verifies the helpers that let a tabular query result be trusted without hand arithmetic: number
/// coercion, computed column totals, key/measure shape detection, and table-reference scanning.
/// </summary>
public sealed class TabularResultAnalyzerTests
{
    [Theory]
    [InlineData(null, 0d)]
    [InlineData(42L, 42d)]
    [InlineData(1234.5d, 1234.5d)]
    [InlineData("3.5", 3.5d)]
    [InlineData("", 0d)]
    [InlineData("   ", 0d)]
    public void TryGetNumber_WithSupportedValues_ReturnsExpectedNumber(object value, double expected)
    {
        Assert.True(TabularResultAnalyzer.TryGetNumber(value, out var number));
        Assert.Equal(expected, number);
    }

    [Fact]
    public void TryGetNumber_WithDecimal_ConvertsToDouble()
    {
        Assert.True(TabularResultAnalyzer.TryGetNumber(1690423.00m, out var number));
        Assert.Equal(1690423.00d, number);
    }

    [Fact]
    public void TryGetNumber_WithNonNumericText_ReturnsFalse()
    {
        Assert.False(TabularResultAnalyzer.TryGetNumber("Eli Lilly", out var number));
        Assert.Equal(0d, number);
    }

    [Fact]
    public void TryGetNumber_WithUnconvertibleObject_ReturnsFalse()
    {
        Assert.False(TabularResultAnalyzer.TryGetNumber(new object(), out var number));
        Assert.Equal(0d, number);
    }

    [Fact]
    public void FormatNumber_UsesInvariantCultureAndTrimsTrailingZeros()
    {
        Assert.Equal("1690423", TabularResultAnalyzer.FormatNumber(1690423.00d));
        Assert.Equal("1690423.5", TabularResultAnalyzer.FormatNumber(1690423.5d));
        Assert.Equal("-116590.27", TabularResultAnalyzer.FormatNumber(-116590.27d));
    }

    /// <summary>
    /// A key/measure result (the shape the cross-file merge guard watches for) is exactly two columns
    /// with a numeric second column and at least one row.
    /// </summary>
    [Fact]
    public void IsKeyMeasureShape_WithTwoColumnsAndNumericSecondColumn_ReturnsTrue()
    {
        var result = new TabularQueryResult
        {
            Columns = ["Client", "Sept_Revenue"],
            Rows =
            [
                ["Eli Lilly", 1690423.00],
                ["LendKey", 84407.76],
            ],
        };

        Assert.True(TabularResultAnalyzer.IsKeyMeasureShape(result));
    }

    [Fact]
    public void IsKeyMeasureShape_WithThreeColumns_ReturnsFalse()
    {
        var result = new TabularQueryResult
        {
            Columns = ["Client", "Site", "Sept_Revenue"],
            Rows = [["Eli Lilly", "Henderson", 1690423.00]],
        };

        Assert.False(TabularResultAnalyzer.IsKeyMeasureShape(result));
    }

    [Fact]
    public void IsKeyMeasureShape_WithNonNumericSecondColumn_ReturnsFalse()
    {
        var result = new TabularQueryResult
        {
            Columns = ["Client", "Region"],
            Rows = [["Eli Lilly", "Midwest"]],
        };

        Assert.False(TabularResultAnalyzer.IsKeyMeasureShape(result));
    }

    [Fact]
    public void IsKeyMeasureShape_WithNoRows_ReturnsFalse()
    {
        var result = new TabularQueryResult
        {
            Columns = ["Client", "Sept_Revenue"],
            Rows = [],
        };

        Assert.False(TabularResultAnalyzer.IsKeyMeasureShape(result));
    }

    /// <summary>
    /// The exact scenario that motivated this helper: five Eli Lilly sub-account rows that a model had
    /// previously added up by hand and gotten wrong by $295,000. The computed total must land on the
    /// true sum instead.
    /// </summary>
    [Fact]
    public void FormatColumnTotals_SumsNumericColumn_ProducesTheTrueTotal()
    {
        var result = new TabularQueryResult
        {
            Columns = ["Client_Name", "Total_Proj__Revnue_2026_09_01"],
            Rows =
            [
                ["Eli Lilly - LAC", 512595.00],
                ["Eli Lilly - Direct Pharmacy", 265460.00],
                ["Eli Lilly - Flex", 730180.00],
                ["Eli Lilly - Tempo & Phone App", 60451.00],
                ["Eli Lilly - Payment Processing", 121737.00],
            ],
        };

        var totals = TabularResultAnalyzer.FormatColumnTotals(result);

        Assert.Contains("Total_Proj__Revnue_2026_09_01 = 1690423", totals);
        Assert.DoesNotContain("1985423", totals);
    }

    /// <summary>
    /// A column that mixes numeric and text values is not summable and must be skipped rather than
    /// silently coerced, while a genuinely numeric sibling column in the same result still totals.
    /// </summary>
    [Fact]
    public void FormatColumnTotals_SkipsNonNumericColumn_ButTotalsNumericSibling()
    {
        var result = new TabularQueryResult
        {
            Columns = ["Client_Name", "Total_Revenue"],
            Rows =
            [
                ["Eli Lilly", 1807013.27],
                ["LendKey", 85047.53],
            ],
        };

        var totals = TabularResultAnalyzer.FormatColumnTotals(result);

        Assert.DoesNotContain("Client_Name", totals);
        Assert.Contains("Total_Revenue = 1892060.8", totals);
    }

    [Fact]
    public void FormatColumnTotals_WithFewerThanTwoRows_ReturnsNull()
    {
        var result = new TabularQueryResult
        {
            Columns = ["Client_Name", "Total_Revenue"],
            Rows = [["Eli Lilly", 1807013.27]],
        };

        Assert.Null(TabularResultAnalyzer.FormatColumnTotals(result));
    }

    [Fact]
    public void FormatColumnTotals_WhenTruncated_NotesTheRowsShownAreNotTheWholeTable()
    {
        var result = new TabularQueryResult
        {
            Columns = ["Client_Name", "Total_Revenue"],
            Rows =
            [
                ["Eli Lilly", 1807013.27],
                ["LendKey", 85047.53],
            ],
            Truncated = true,
        };

        var totals = TabularResultAnalyzer.FormatColumnTotals(result);

        Assert.Contains("not the whole table", totals);
    }

    /// <summary>
    /// Table names are scanned as whole identifiers so a table name that is a substring of another
    /// identifier in the SQL text does not produce a false match.
    /// </summary>
    [Fact]
    public void FindReferencedTables_MatchesWholeIdentifierOnly()
    {
        var tables = new List<TabularTableInfo>
        {
            new() { TableName = "Client" },
            new() { TableName = "Client_Breakdown" },
        };

        var referenced = TabularResultAnalyzer.FindReferencedTables(
            "SELECT * FROM Client_Breakdown WHERE is_subtotal = 0",
            tables);

        Assert.Single(referenced);
        Assert.Equal("Client_Breakdown", referenced[0].TableName);
    }

    [Fact]
    public void FindReferencedTables_WithBothTablesNamed_ReturnsBoth()
    {
        var tables = new List<TabularTableInfo>
        {
            new() { TableName = "Projections_By_Client" },
            new() { TableName = "Client_Breakdown" },
        };

        var referenced = TabularResultAnalyzer.FindReferencedTables(
            "SELECT * FROM Projections_By_Client cs, Client_Breakdown cb",
            tables);

        Assert.Equal(2, referenced.Count);
    }

    [Fact]
    public void FindReferencedTables_WithNoMatchingTableName_ReturnsEmpty()
    {
        var tables = new List<TabularTableInfo> { new() { TableName = "Client_Breakdown" } };

        var referenced = TabularResultAnalyzer.FindReferencedTables(
            "SELECT * FROM Master_Client_Services",
            tables);

        Assert.Empty(referenced);
    }
}
