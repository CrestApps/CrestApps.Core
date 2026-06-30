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

    [Fact]
    public void EnsureCommandBatch_AllowsMultipleStatements()
    {
        var statements = TabularSqlGuard.EnsureCommandBatch(
            "UPDATE sales SET amount = '0' WHERE amount = ''; ALTER TABLE sales ADD COLUMN country TEXT; UPDATE sales SET country = 'US'");

        Assert.Equal(3, statements.Count);
        Assert.StartsWith("UPDATE", statements[0]);
        Assert.StartsWith("ALTER", statements[1]);
        Assert.StartsWith("UPDATE", statements[2]);
    }

    [Fact]
    public void EnsureCommandBatch_AllowsTrailingSemicolonAndWhitespace()
    {
        var statements = TabularSqlGuard.EnsureCommandBatch("UPDATE sales SET amount = '1';  ");

        Assert.Single(statements);
        Assert.Equal("UPDATE sales SET amount = '1'", statements[0]);
    }

    [Fact]
    public void EnsureCommandBatch_DoesNotSplitOnSemicolonInsideStringLiteral()
    {
        var statements = TabularSqlGuard.EnsureCommandBatch("UPDATE sales SET note = 'a;b;c' WHERE region = 'North'");

        Assert.Single(statements);
        Assert.Contains("'a;b;c'", statements[0]);
    }

    [Fact]
    public void EnsureCommandBatch_IgnoresComments()
    {
        var statements = TabularSqlGuard.EnsureCommandBatch(
            "UPDATE sales SET amount = '1'; -- a comment; with semicolon\nUPDATE sales SET amount = '2'");

        Assert.Equal(2, statements.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";;")]
    public void EnsureCommandBatch_RejectsEmptyInput(string sql)
    {
        Assert.Throws<TabularSqlException>(() => TabularSqlGuard.EnsureCommandBatch(sql));
    }

    [Theory]
    [InlineData("UPDATE sales SET a = 1; ATTACH DATABASE 'x' AS y")]
    [InlineData("UPDATE sales SET a = 1; SELECT load_extension('evil')")]
    [InlineData("UPDATE sales SET a = 1; GRANT ALL ON sales")]
    public void EnsureCommandBatch_RejectsUnsafeOrDisallowedStatements(string sql)
    {
        Assert.Throws<TabularSqlException>(() => TabularSqlGuard.EnsureCommandBatch(sql));
    }
}
