using CrestApps.Core.AI.Documents.Tabular;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public class TabularSqlGuardTests
{
    [Theory]
    [InlineData("SELECT * FROM sales")]
    [InlineData("  select region from sales  ")]
    [InlineData("WITH t AS (SELECT 1) SELECT * FROM t")]
    [InlineData("SELECT * FROM sales;")]
    public void EnsureReadOnlyQuery_AllowsSelectStatements(string sql)
    {
        var normalized = TabularSqlGuard.EnsureReadOnlyQuery(sql);

        Assert.False(normalized.EndsWith(';'));
    }

    [Theory]
    [InlineData("UPDATE sales SET amount = 1")]
    [InlineData("DELETE FROM sales")]
    [InlineData("DROP TABLE sales")]
    public void EnsureReadOnlyQuery_RejectsNonSelect(string sql)
    {
        Assert.Throws<TabularSqlException>(() => TabularSqlGuard.EnsureReadOnlyQuery(sql));
    }

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1; DROP TABLE sales")]
    public void EnsureReadOnlyQuery_RejectsMultipleStatements(string sql)
    {
        Assert.Throws<TabularSqlException>(() => TabularSqlGuard.EnsureReadOnlyQuery(sql));
    }

    [Theory]
    [InlineData("SELECT * FROM sales; ATTACH DATABASE 'x' AS y")]
    [InlineData("ATTACH DATABASE 'x' AS y")]
    [InlineData("PRAGMA table_info(sales)")]
    [InlineData("SELECT load_extension('evil')")]
    public void EnsureReadOnlyQuery_RejectsForbiddenKeywords(string sql)
    {
        Assert.Throws<TabularSqlException>(() => TabularSqlGuard.EnsureReadOnlyQuery(sql));
    }

    [Theory]
    [InlineData("INSERT INTO sales (region) VALUES ('North')")]
    [InlineData("UPDATE sales SET amount = '1'")]
    [InlineData("ALTER TABLE sales ADD COLUMN country TEXT")]
    [InlineData("DELETE FROM sales WHERE amount = '0'")]
    public void EnsureCommand_AllowsManipulationStatements(string sql)
    {
        var normalized = TabularSqlGuard.EnsureCommand(sql);

        Assert.False(string.IsNullOrWhiteSpace(normalized));
    }

    [Theory]
    [InlineData("ATTACH DATABASE 'x' AS y")]
    [InlineData("VACUUM INTO 'out.db'")]
    [InlineData("UPDATE sales SET a = 1; DROP TABLE sales")]
    public void EnsureCommand_RejectsUnsafeStatements(string sql)
    {
        Assert.Throws<TabularSqlException>(() => TabularSqlGuard.EnsureCommand(sql));
    }
}
