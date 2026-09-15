using CrestApps.Core.AI.Documents.Tabular;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class TabularWorkspaceFormattingTests
{
    private const string Csv = "region,amount\nNorth,100\nSouth,200";
    private const string Spec = """{"columns":[{"column":"amount","numberFormat":"Currency"}]}""";

    /// <summary>
    /// Verifies that a table with no recorded formatting reports none, so an export of an unformatted
    /// table stays unformatted.
    /// </summary>
    [Fact]
    public async Task GetFormattingAsync_WithoutSave_ReturnsNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = await CreateLoadedWorkspaceAsync(cancellationToken);

        var (specJson, revision) = await workspace.GetFormattingAsync("sales", cancellationToken);

        Assert.Null(specJson);
        Assert.Equal(0, revision);
    }

    /// <summary>
    /// Verifies that formatting survives being written and read back, which is what lets a sheet
    /// formatted in one turn be exported in a later one.
    /// </summary>
    [Fact]
    public async Task SaveFormattingAsync_StoresSpecification()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = await CreateLoadedWorkspaceAsync(cancellationToken);

        var revision = await workspace.SaveFormattingAsync("sales", Spec, cancellationToken);
        var stored = await workspace.GetFormattingAsync("sales", cancellationToken);

        Assert.Equal(1, revision);
        Assert.Equal(Spec, stored.SpecJson);
        Assert.Equal(1, stored.Revision);
    }

    /// <summary>
    /// The revision must advance on every save. The export tool keys its per-invocation cache on it,
    /// so a revision that did not move would hand the user back the previous, unformatted file.
    /// </summary>
    [Fact]
    public async Task SaveFormattingAsync_AdvancesRevisionOnEverySave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = await CreateLoadedWorkspaceAsync(cancellationToken);

        Assert.Equal(1, await workspace.SaveFormattingAsync("sales", Spec, cancellationToken));
        Assert.Equal(2, await workspace.SaveFormattingAsync("sales", Spec, cancellationToken));
        Assert.Equal(3, await workspace.SaveFormattingAsync("sales", """{"bandedRows":true}""", cancellationToken));

        Assert.Equal(3, (await workspace.GetFormattingAsync("sales", cancellationToken)).Revision);
    }

    /// <summary>
    /// Verifies that clearing removes the recorded formatting.
    /// </summary>
    [Fact]
    public async Task SaveFormattingAsync_WithNullSpecification_ClearsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = await CreateLoadedWorkspaceAsync(cancellationToken);

        await workspace.SaveFormattingAsync("sales", Spec, cancellationToken);
        await workspace.SaveFormattingAsync("sales", specJson: null, cancellationToken);

        var stored = await workspace.GetFormattingAsync("sales", cancellationToken);

        Assert.Null(stored.SpecJson);
        Assert.Equal(0, stored.Revision);
    }

    /// <summary>
    /// Verifies that formatting is scoped to its own table, so formatting one worksheet of a workbook
    /// does not change how another is exported.
    /// </summary>
    [Fact]
    public async Task SaveFormattingAsync_IsScopedToTable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = await CreateLoadedWorkspaceAsync(cancellationToken);

        await workspace.SaveFormattingAsync("sales", Spec, cancellationToken);

        Assert.Null((await workspace.GetFormattingAsync("other", cancellationToken)).SpecJson);
    }

    /// <summary>
    /// Verifies that recording formatting leaves the connection read-only, so the write window opened
    /// for the save cannot let a later read path modify data.
    /// </summary>
    [Fact]
    public async Task SaveFormattingAsync_LeavesWorkspaceReadOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = await CreateLoadedWorkspaceAsync(cancellationToken);

        await workspace.SaveFormattingAsync("sales", Spec, cancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(
            () => workspace.QueryAsync("DELETE FROM sales", 100, cancellationToken));

        var result = await workspace.QueryAsync("SELECT COUNT(*) FROM sales", 100, cancellationToken);

        Assert.Equal(2L, result.Rows[0][0]);
    }

    /// <summary>
    /// Verifies that the formatting table is not surfaced as data, so the model never sees it as a
    /// table it could query or export.
    /// </summary>
    [Fact]
    public async Task GetTablesAsync_DoesNotExposeFormattingTable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = await CreateLoadedWorkspaceAsync(cancellationToken);

        await workspace.SaveFormattingAsync("sales", Spec, cancellationToken);

        var tables = await workspace.GetTablesAsync(cancellationToken);

        Assert.Equal(["sales"], tables.Select(table => table.TableName));
    }

    private static async Task<TabularWorkspace> CreateLoadedWorkspaceAsync(CancellationToken cancellationToken)
    {
        var workspace = new TabularWorkspace(new TabularWorkspaceOptions());

        await workspace.EnsureReadyAsync(
            [new TabularDocumentRef("doc1", "sales.csv")],
            (_, _) => Task.FromResult(Csv),
            cancellationToken);

        return workspace;
    }
}
