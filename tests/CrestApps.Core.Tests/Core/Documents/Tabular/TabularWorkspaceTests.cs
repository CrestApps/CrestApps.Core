using CrestApps.Core.AI.Documents.Tabular;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public class TabularWorkspaceTests
{
    private const string Csv = "region,amount\nNorth,100\nSouth,200\nNorth,50";

    [Fact]
    public async Task EnsureReadyAsync_LoadsTableWithSchema()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();

        var tables = await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        var table = Assert.Single(tables);
        Assert.Equal("sales", table.TableName);
        Assert.Equal("sales.csv", table.SourceFileName);
        Assert.Equal(3, table.RowCount);
        Assert.Equal(["region", "amount"], table.Columns.Select(c => c.Name));
    }

    [Fact]
    public async Task QueryAsync_RunsAggregation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        var result = await workspace.QueryAsync(
            "SELECT region, SUM(CAST(amount AS INTEGER)) AS total FROM sales GROUP BY region ORDER BY region",
            100,
            cancellationToken);

        Assert.Equal(["region", "total"], result.Columns);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("North", result.Rows[0][0]);
        Assert.Equal(150L, result.Rows[0][1]);
        Assert.Equal("South", result.Rows[1][0]);
        Assert.Equal(200L, result.Rows[1][1]);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task EnsureReadyAsync_SurveyHeader_UsesQuestionCodeAsSqlColumnName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        const string csv = "Respondent,Q3_C28/What fast food or quick service restaurants have you visited?\n1,1\n2,975";

        await workspace.EnsureReadyAsync(Documents(), Loader(csv), cancellationToken);

        var result = await workspace.QueryAsync(
            "SELECT COUNT(*) AS visitors FROM sales WHERE Q3_C28 = '1'",
            100,
            cancellationToken);
        var tables = await workspace.GetTablesAsync(cancellationToken);
        var column = Assert.Single(Assert.Single(tables).Columns, c => c.Name == "Q3_C28");

        Assert.Equal("Q3_C28/What fast food or quick service restaurants have you visited?", column.SourceName);
        Assert.Equal(1L, Assert.Single(result.Rows)[0]);
    }

    [Fact]
    public async Task QueryAsync_TruncatesToRowLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace(new TabularWorkspaceOptions { MaxRowsPerQuery = 2 });
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        var result = await workspace.QueryAsync("SELECT * FROM sales", 100, cancellationToken);

        Assert.Equal(2, result.Rows.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task QueryAsync_RejectsNonSelect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        await Assert.ThrowsAsync<TabularSqlException>(
            () => workspace.QueryAsync("UPDATE sales SET amount = '1'", 100, cancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_MutatesInMemoryCopy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        var command = await workspace.ExecuteAsync("UPDATE sales SET amount = '300' WHERE region = 'South'", cancellationToken);
        Assert.Equal(1, command.AffectedRows);

        var result = await workspace.QueryAsync("SELECT amount FROM sales WHERE region = 'South'", 100, cancellationToken);
        Assert.Equal("300", Assert.Single(result.Rows)[0]);
    }

    [Fact]
    public async Task ExecuteAsync_AddsColumn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        await workspace.ExecuteAsync("ALTER TABLE sales ADD COLUMN country TEXT", cancellationToken);
        await workspace.ExecuteAsync("UPDATE sales SET country = 'US'", cancellationToken);

        var tables = await workspace.GetTablesAsync(cancellationToken);
        Assert.Contains(Assert.Single(tables).Columns, c => c.Name == "country");
    }

    [Fact]
    public async Task ExecuteAsync_RejectsForbiddenStatement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        await Assert.ThrowsAsync<TabularSqlException>(
            () => workspace.ExecuteAsync("ATTACH DATABASE 'x' AS y", cancellationToken));
    }

    [Fact]
    public async Task EnsureReadyAsync_SamePrompt_ReusesDatabaseWithoutReloading()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        var loadCount = 0;
        var loader = CountingLoader(Csv, () => loadCount++);

        // Multiple tabular tool calls in the same prompt must not rebuild the table or reload
        // the file content.
        await workspace.EnsureReadyAsync(Documents(), loader, cancellationToken);
        await workspace.EnsureReadyAsync(Documents(), loader, cancellationToken);
        await workspace.EnsureReadyAsync(Documents(), loader, cancellationToken);

        Assert.Equal(1, loadCount);
    }

    [Fact]
    public async Task Dispose_DisposesDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        workspace.Dispose();

        // After disposal the in-memory database is gone; querying throws.
        await Assert.ThrowsAnyAsync<Exception>(
            () => workspace.QueryAsync("SELECT * FROM sales", 100, cancellationToken));
    }

    private static TabularWorkspace CreateWorkspace(TabularWorkspaceOptions options = null)
    {
        return new TabularWorkspace(options ?? new TabularWorkspaceOptions());
    }

    private static IReadOnlyList<TabularDocumentRef> Documents()
    {
        return [new TabularDocumentRef("doc1", "sales.csv")];
    }

    private static Func<string, CancellationToken, Task<string>> Loader(string content)
    {
        return (_, _) => Task.FromResult(content);
    }

    private static Func<string, CancellationToken, Task<string>> CountingLoader(string content, Action onLoad)
    {
        return (_, _) =>
        {
            onLoad();

            return Task.FromResult(content);
        };
    }
}
