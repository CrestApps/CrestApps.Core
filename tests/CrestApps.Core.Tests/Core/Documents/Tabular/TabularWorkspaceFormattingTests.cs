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

    /// <summary>
    /// Formatting recorded for the export as a whole is stored under its own key, separate from any
    /// source table. An export that joins several tables has no single source table, and before this
    /// existed its formatting was silently dropped — found by running the real agent end to end.
    /// </summary>
    [Fact]
    public async Task SaveFormattingAsync_WorkspaceKey_IsSeparateFromTableFormatting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = await CreateLoadedWorkspaceAsync(cancellationToken);

        await workspace.SaveFormattingAsync("sales", Spec, cancellationToken);
        await workspace.SaveFormattingAsync(
            TabularToolNames.WorkspaceFormattingKey,
            """{"bandedRows":true}""",
            cancellationToken);

        var table = await workspace.GetFormattingAsync("sales", cancellationToken);
        var export = await workspace.GetFormattingAsync(TabularToolNames.WorkspaceFormattingKey, cancellationToken);

        Assert.Equal(Spec, table.SpecJson);
        Assert.Equal("""{"bandedRows":true}""", export.SpecJson);
    }

    /// <summary>
    /// Verifies that the export-wide key is not reported as a data table, so the model never sees it as
    /// something it could query.
    /// </summary>
    [Fact]
    public async Task GetTablesAsync_DoesNotExposeTheWorkspaceFormattingKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = await CreateLoadedWorkspaceAsync(cancellationToken);

        await workspace.SaveFormattingAsync(
            TabularToolNames.WorkspaceFormattingKey,
            """{"bandedRows":true}""",
            cancellationToken);

        var tables = await workspace.GetTablesAsync(cancellationToken);

        Assert.Equal(["sales"], tables.Select(table => table.TableName));
    }

    /// <summary>
    /// Renaming a table used to leave the metadata pointing at a name that no longer existed. Every
    /// later call still reported the table as loaded while every query against it failed, and the
    /// conversation concluded the uploaded file had been lost and asked for a re-upload. Found by
    /// driving the real agent, which renamed a table while building a comparison.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_RenamingATable_ReconcilesTheMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = await CreateLoadedWorkspaceAsync(cancellationToken);

        await workspace.ExecuteAsync("ALTER TABLE sales RENAME TO old_sales", cancellationToken);

        var tables = await workspace.GetTablesAsync(cancellationToken);

        Assert.DoesNotContain(tables, table => table.TableName == "sales");
        Assert.Contains(tables, table => table.TableName == "old_sales");

        // The renamed table must be queryable under its new name rather than reported and unusable.
        var result = await workspace.QueryAsync("SELECT COUNT(*) FROM old_sales", 100, cancellationToken);

        Assert.Equal(2L, result.Rows[0][0]);
    }

    /// <summary>
    /// Verifies that a table the caller builds is listed, so it can be queried and exported. Before
    /// this, a created table was invisible to the caller that had just created it.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CreatingATable_RegistersIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = await CreateLoadedWorkspaceAsync(cancellationToken);

        await workspace.ExecuteAsync(
            "CREATE TABLE summary AS SELECT region, SUM(amount) AS total FROM sales GROUP BY region",
            cancellationToken);

        var tables = await workspace.GetTablesAsync(cancellationToken);

        Assert.Contains(tables, table => table.TableName == "summary");
    }

    /// <summary>
    /// Verifies that dropping a table removes its metadata, so it is no longer reported as loaded.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DroppingATable_RemovesItsMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = await CreateLoadedWorkspaceAsync(cancellationToken);

        await workspace.ExecuteAsync("DROP TABLE sales", cancellationToken);

        Assert.Empty(await workspace.GetTablesAsync(cancellationToken));
    }

    /// <summary>
    /// A table the caller built belongs to no uploaded document, so it must survive the cleanup that
    /// removes tables whose document is no longer attached. Dropping it would destroy work the
    /// conversation just did.
    /// </summary>
    [Fact]
    public async Task EnsureReadyAsync_AfterCreatingATable_KeepsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = await CreateLoadedWorkspaceAsync(cancellationToken);

        await workspace.ExecuteAsync("CREATE TABLE scratch AS SELECT * FROM sales", cancellationToken);

        // A second synchronization is what happens on the next tool call in the conversation.
        await workspace.EnsureReadyAsync(
            [new TabularDocumentRef("doc1", "sales.csv")],
            (_, _) => Task.FromResult(Csv),
            cancellationToken);

        var tables = await workspace.GetTablesAsync(cancellationToken);

        Assert.Contains(tables, table => table.TableName == "scratch");
        Assert.Contains(tables, table => table.TableName == "sales");
    }

    /// <summary>
    /// A workspace left inconsistent by an earlier session must heal itself rather than stay broken for
    /// the rest of the conversation. Dropping a table behind the workspace's back leaves metadata
    /// claiming it exists; the next synchronization should notice and import the document again.
    /// </summary>
    [Fact]
    public async Task EnsureReadyAsync_WhenATableVanished_ReimportsTheDocument()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = await CreateLoadedWorkspaceAsync(cancellationToken);

        // Simulates the state a renamed or externally dropped table leaves behind.
        await workspace.ExecuteAsync("ALTER TABLE sales RENAME TO stale", cancellationToken);
        await workspace.ExecuteAsync("DROP TABLE stale", cancellationToken);

        await workspace.EnsureReadyAsync(
            [new TabularDocumentRef("doc1", "sales.csv")],
            (_, _) => Task.FromResult(Csv),
            cancellationToken);

        var tables = await workspace.GetTablesAsync(cancellationToken);

        Assert.Contains(tables, table => table.TableName == "sales");

        var result = await workspace.QueryAsync("SELECT COUNT(*) FROM sales", 100, cancellationToken);

        Assert.Equal(2L, result.Rows[0][0]);
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
