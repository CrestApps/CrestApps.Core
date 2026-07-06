using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Documents.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class FillEmptyTabularCellsToolTests
{
    [Fact]
    public void BuildStatements_WhenTableHasManyColumns_SplitsIntoInternalBatches()
    {
        var table = new TabularTableInfo
        {
            TableName = "survey",
            Columns = Enumerable
                .Range(1, 165)
                .Select(index => new TabularColumnInfo($"C{index}", "TEXT"))
                .ToList(),
        };

        var statements = FillEmptyTabularCellsTool.BuildStatements(table, "NULL");

        Assert.Equal(3, statements.Count);
        Assert.Contains("\"C1\"", statements[0], StringComparison.Ordinal);
        Assert.Contains("\"C64\"", statements[0], StringComparison.Ordinal);
        Assert.DoesNotContain("\"C65\"", statements[0], StringComparison.Ordinal);
        Assert.Contains("\"C65\"", statements[1], StringComparison.Ordinal);
        Assert.Contains("\"C128\"", statements[1], StringComparison.Ordinal);
        Assert.DoesNotContain("\"C129\"", statements[1], StringComparison.Ordinal);
        Assert.Contains("\"C129\"", statements[2], StringComparison.Ordinal);
        Assert.Contains("\"C165\"", statements[2], StringComparison.Ordinal);
    }
}
