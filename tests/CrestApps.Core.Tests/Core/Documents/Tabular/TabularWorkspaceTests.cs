using System.Reflection;
using CrestApps.Core.AI.Documents.Tabular;
using Microsoft.Data.Sqlite;

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
    public async Task EnsureReadyAsync_InfersColumnStorageTypes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();

        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        var columns = Assert.Single(await workspace.GetTablesAsync(cancellationToken)).Columns;
        Assert.Equal("TEXT", columns.Single(c => c.Name == "region").DeclaredType);
        Assert.Equal("INTEGER", columns.Single(c => c.Name == "amount").DeclaredType);
    }

    [Fact]
    public async Task QueryAsync_NumericColumn_OrdersNumerically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        var result = await workspace.QueryAsync("SELECT amount FROM sales ORDER BY amount DESC", 100, cancellationToken);

        Assert.Equal([200L, 100L, 50L], result.Rows.Select(row => row[0]));
    }

    [Fact]
    public async Task QueryAsync_NumericColumn_ComparesNumerically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        var result = await workspace.QueryAsync("SELECT COUNT(*) FROM sales WHERE amount > 100", 100, cancellationToken);

        Assert.Equal(1L, Assert.Single(result.Rows)[0]);
    }

    [Fact]
    public async Task EnsureReadyAsync_LeadingZeroValues_RemainText()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        const string csv = "code,label\n007,north\n0098,south";

        await workspace.EnsureReadyAsync(Documents(), Loader(csv), cancellationToken);

        var columns = Assert.Single(await workspace.GetTablesAsync(cancellationToken)).Columns;
        Assert.Equal("TEXT", columns.Single(c => c.Name == "code").DeclaredType);

        var result = await workspace.QueryAsync("SELECT code FROM sales ORDER BY label", 100, cancellationToken);
        Assert.Equal("007", result.Rows[0][0]);
    }

    [Fact]
    public async Task EnsureReadyAsync_ColumnWithNonNumericValue_RemainsText()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        const string csv = "rate,label\n35.1,north\n#DIV/0!,south";

        await workspace.EnsureReadyAsync(Documents(), Loader(csv), cancellationToken);

        var columns = Assert.Single(await workspace.GetTablesAsync(cancellationToken)).Columns;
        Assert.Equal("TEXT", columns.Single(c => c.Name == "rate").DeclaredType);
    }

    [Fact]
    public async Task EnsureReadyAsync_BlankNumericCell_StoresNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        const string csv = "region,amount\nNorth,100\nSouth,\nEast,50";

        await workspace.EnsureReadyAsync(Documents(), Loader(csv), cancellationToken);

        var columns = Assert.Single(await workspace.GetTablesAsync(cancellationToken)).Columns;
        Assert.Equal("INTEGER", columns.Single(c => c.Name == "amount").DeclaredType);

        var result = await workspace.QueryAsync("SELECT COUNT(*) FROM sales WHERE amount IS NULL", 100, cancellationToken);
        Assert.Equal(1L, Assert.Single(result.Rows)[0]);
    }

    [Fact]
    public async Task EnsureReadyAsync_DecimalColumn_UsesRealStorageType()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        const string csv = "region,amount\nNorth,1878165.335\nSouth,97534.0224";

        await workspace.EnsureReadyAsync(Documents(), Loader(csv), cancellationToken);

        var columns = Assert.Single(await workspace.GetTablesAsync(cancellationToken)).Columns;
        Assert.Equal("REAL", columns.Single(c => c.Name == "amount").DeclaredType);

        var result = await workspace.QueryAsync("SELECT region FROM sales ORDER BY amount DESC", 100, cancellationToken);
        Assert.Equal("North", result.Rows[0][0]);
    }

    [Fact]
    public async Task EnsureReadyAsync_SubtotalRows_AreFlaggedAndExcludableFromAggregates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        const string csv = "region,amount\nNorth,100\nSouth,200\nTotal,300";

        await workspace.EnsureReadyAsync(Documents(), Loader(csv), cancellationToken);

        var columns = Assert.Single(await workspace.GetTablesAsync(cancellationToken)).Columns;
        Assert.Contains(columns, c => c.Name == "is_subtotal");

        var flagged = await workspace.QueryAsync("SELECT COUNT(*) FROM sales WHERE is_subtotal = 1", 100, cancellationToken);
        Assert.Equal(1L, Assert.Single(flagged.Rows)[0]);

        // The Total rollup row (300) must not inflate the sum of the two real rows (100 + 200).
        var sum = await workspace.QueryAsync("SELECT SUM(amount) FROM sales WHERE is_subtotal = 0", 100, cancellationToken);
        Assert.Equal(300L, Assert.Single(sum.Rows)[0]);
    }

    /// <summary>
    /// The declared type cannot be quoted like an identifier, so it is emitted verbatim into the
    /// CREATE TABLE statement. Only the inferred storage types may reach the statement.
    /// </summary>
    [Fact]
    public void CreateTable_UnrecognizedDeclaredType_FallsBackToText()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        TabularWorkspaceSqliteHelpers.CreateTable(
            connection,
            "sales",
            [new TabularColumnInfo("amount", "TEXT); DROP TABLE \"sales\";--")]);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type FROM pragma_table_info('sales') WHERE name = 'amount'";

        Assert.Equal("TEXT", command.ExecuteScalar());
    }

    [Fact]
    public async Task EnsureReadyAsync_DirectImporter_BypassesArtifactLoader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        var tables = await workspace.EnsureReadyAsync(
            Documents(),
            (_, _) => throw new Xunit.Sdk.XunitException("Artifact loader should not run when direct importer succeeds."),
            (document, connection, tableName, token) =>
            {
                var resolvedTableName = tableName(null, true);
                var columns = TabularWorkspaceSqliteHelpers.BuildColumns(["region", "amount"]);
                TabularWorkspaceSqliteHelpers.CreateTable(connection, resolvedTableName, columns);

                using var command = connection.CreateCommand();
                command.CommandText = $"INSERT INTO {TabularWorkspaceSqliteHelpers.QuoteIdentifier(resolvedTableName)} (\"region\", \"amount\") VALUES ('North', '100'), ('South', '200')";
                command.ExecuteNonQuery();

                IReadOnlyList<TabularWorkspaceImportResult> results =
                [
                    new TabularWorkspaceImportResult(resolvedTableName, null, columns, 2, 1, 1),
                ];

                return Task.FromResult(results);
            },
            cancellationToken);

        var table = Assert.Single(tables);
        Assert.Equal(2, table.RowCount);

        var result = await workspace.QueryAsync("SELECT region, amount FROM sales ORDER BY region", 100, cancellationToken);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("North", result.Rows[0][0]);
        Assert.Equal("100", result.Rows[0][1]);
    }

    /// <summary>
    /// Verifies that a multi-worksheet artifact produces one independent table per worksheet, that the
    /// worksheet names are preserved in the table metadata, and that worksheet data is not merged.
    /// </summary>
    [Fact]
    public async Task EnsureReadyAsync_MultiWorksheetArtifact_CreatesIndependentTablesWithWorksheetMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();

        var artifact = new TabularDocumentArtifact
        {
            Worksheets =
            [
                new TabularWorksheet
                {
                    Name = "Client Breakdown",
                    Header = ["Site", "Total Revenue"],
                    Rows = [["Henderson", "100"]],
                },
                new TabularWorksheet
                {
                    Name = "Overall Projections",
                    Header = ["Site Location", "Total"],
                    Rows = [["Henderson", "1420000"]],
                },
            ],
        };

        IReadOnlyList<TabularDocumentRef> documents = [new TabularDocumentRef("doc1", "revenue.xlsx")];

        var tables = await workspace.EnsureReadyAsync(
            documents,
            (document, token) => Task.FromResult(artifact),
            null,
            cancellationToken);

        // The workspace exposes one table per worksheet and preserves the worksheet names.
        Assert.Equal(2, tables.Count);
        Assert.Equal(
            ["Client Breakdown", "Overall Projections"],
            tables.OrderBy(t => t.WorksheetName, StringComparer.Ordinal).Select(t => t.WorksheetName));

        var clientTable = tables.Single(t => t.WorksheetName == "Client Breakdown");
        var projectionsTable = tables.Single(t => t.WorksheetName == "Overall Projections");
        Assert.NotEqual(clientTable.TableName, projectionsTable.TableName);

        // Each worksheet keeps its own data; nothing is merged across worksheets.
        var clientRows = await workspace.QueryAsync(
            $"SELECT * FROM \"{clientTable.TableName}\"",
            100,
            cancellationToken);
        Assert.Single(clientRows.Rows);
        Assert.Equal("Henderson", clientRows.Rows[0][0]);
        Assert.Equal(100L, clientRows.Rows[0][1]);

        var projectionRows = await workspace.QueryAsync(
            $"SELECT * FROM \"{projectionsTable.TableName}\"",
            100,
            cancellationToken);
        Assert.Single(projectionRows.Rows);
        Assert.Equal(1420000L, projectionRows.Rows[0][1]);
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
    public async Task QueryAsync_AllowsDoubleQuotedStringLiterals()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        // Models frequently quote string values with double quotes. Without the tolerant fallback
        // SQLite treats "North" as an identifier and fails with "no such column"; the workspace
        // re-enables the fallback so the value is parsed as a string literal instead.
        var result = await workspace.QueryAsync(
            "SELECT region, amount FROM sales WHERE region = \"North\" ORDER BY CAST(amount AS INTEGER)",
            100,
            cancellationToken);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("North", result.Rows[0][0]);
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
        Assert.Equal(300L, Assert.Single(result.Rows)[0]);
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
    public async Task ExecuteAsync_RunsMultipleStatementsInOneCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        var command = await workspace.ExecuteAsync(
            "UPDATE sales SET amount = '300' WHERE region = 'South'; ALTER TABLE sales ADD COLUMN country TEXT; UPDATE sales SET country = 'US'",
            cancellationToken);

        Assert.Equal(3, command.StatementCount);
        // At minimum the 1 updated South row and the 3 country updates are reflected.
        Assert.True(command.AffectedRows >= 4);

        var tables = await workspace.GetTablesAsync(cancellationToken);
        Assert.Contains(Assert.Single(tables).Columns, c => c.Name == "country");

        var result = await workspace.QueryAsync("SELECT amount FROM sales WHERE region = 'South'", 100, cancellationToken);
        Assert.Equal(300L, Assert.Single(result.Rows)[0]);
    }

    [Fact]
    public async Task ExecuteAsync_RollsBackEntireBatchWhenAnyStatementFails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        // The second statement references a missing column and must fail, rolling back the first.
        await Assert.ThrowsAnyAsync<Exception>(
            () => workspace.ExecuteAsync(
                "UPDATE sales SET amount = '777' WHERE region = 'South'; UPDATE sales SET missing_column = '1'",
                cancellationToken));

        var result = await workspace.QueryAsync("SELECT amount FROM sales WHERE region = 'South'", 100, cancellationToken);
        Assert.Equal(200L, Assert.Single(result.Rows)[0]);
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
    public async Task ExportCsvAsync_WritesReadOnlyQueryResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        await using var stream = new MemoryStream();
        var export = await workspace.ExportCsvAsync(
            "SELECT region, amount FROM sales ORDER BY CAST(amount AS INTEGER) DESC",
            stream,
            cancellationToken);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var csv = await reader.ReadToEndAsync(cancellationToken);

        Assert.Equal(3, export.RowCount);
        Assert.Equal(["region", "amount"], export.Artifact.Header);
        Assert.Equal("region,amount\nSouth,200\nNorth,100\nNorth,50\n", NormalizeLineEndings(csv));
    }

    [Fact]
    public async Task ExportCsvAsync_EscapesCsvValues()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        const string csv = "name,note\nNorth,\"Hello, world\"\nSouth,\"He said \"\"yes\"\"\"";
        await workspace.EnsureReadyAsync(Documents(), Loader(csv), cancellationToken);

        await using var stream = new MemoryStream();
        await workspace.ExportCsvAsync("SELECT name, note FROM sales ORDER BY name", stream, cancellationToken);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var exported = await reader.ReadToEndAsync(cancellationToken);

        Assert.Equal("name,note\nNorth,\"Hello, world\"\nSouth,\"He said \"\"yes\"\"\"\n", NormalizeLineEndings(exported));
    }

    [Fact]
    public async Task ExportCsvAsync_RejectsManipulationStatement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        await using var stream = new MemoryStream();

        await Assert.ThrowsAsync<TabularSqlException>(
            () => workspace.ExportCsvAsync("UPDATE sales SET amount = '1'", stream, cancellationToken));
    }

    [Fact]
    public async Task ExportAsync_ReturnsArtifactWithoutWriting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        var export = await workspace.ExportAsync(
            "SELECT region, amount FROM sales ORDER BY CAST(amount AS INTEGER) DESC",
            cancellationToken);

        Assert.Equal(3, export.RowCount);
        Assert.Equal(["region", "amount"], export.Artifact.Header);
        Assert.Equal(["South", "200"], export.Artifact.Rows[0]);
        Assert.Equal(["North", "100"], export.Artifact.Rows[1]);
        Assert.Equal(["North", "50"], export.Artifact.Rows[2]);
    }

    [Fact]
    public async Task ExportAsync_RejectsManipulationStatement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        await Assert.ThrowsAsync<TabularSqlException>(
            () => workspace.ExportAsync("UPDATE sales SET amount = '1'", cancellationToken));
    }

    [Fact]
    public async Task ExportFullAsync_ReturnsEntireCurrentTableIncludingMutations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        // Mutate the in-memory copy; the full export must reflect the updated data, not the original file.
        await workspace.ExecuteAsync("UPDATE sales SET amount = '999' WHERE region = 'South'", cancellationToken);
        await workspace.ExecuteAsync("INSERT INTO sales (region, amount) VALUES ('West', '5')", cancellationToken);

        var export = await workspace.ExportFullAsync(cancellationToken);

        Assert.Equal(4, export.RowCount);
        Assert.Equal(["region", "amount"], export.Artifact.Header);
        Assert.Contains(export.Artifact.Rows, row => row[0] == "South" && row[1] == "999");
        Assert.Contains(export.Artifact.Rows, row => row[0] == "West" && row[1] == "5");
    }

    [Fact]
    public async Task ExportFullAsync_UsesOriginalSourceHeaderNames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        const string csv = "Respondent,Q3_C28/What restaurants have you visited?\n1,1\n2,975";
        await workspace.EnsureReadyAsync(Documents(), Loader(csv), cancellationToken);

        var export = await workspace.ExportFullAsync(cancellationToken);

        Assert.Equal(["Respondent", "Q3_C28/What restaurants have you visited?"], export.Artifact.Header);
    }

    [Fact]
    public async Task ExportFullAsync_NoTablesLoaded_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync([], Loader(Csv), cancellationToken);

        await Assert.ThrowsAsync<TabularSqlException>(
            () => workspace.ExportFullAsync(cancellationToken));
    }

    [Fact]
    public async Task ExportFullAsync_MultipleTablesLoaded_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        IReadOnlyList<TabularDocumentRef> documents =
        [
            new TabularDocumentRef("doc1", "sales.csv"),
            new TabularDocumentRef("doc2", "more.csv"),
        ];
        await workspace.EnsureReadyAsync(documents, Loader(Csv), cancellationToken);

        await Assert.ThrowsAsync<TabularSqlException>(
            () => workspace.ExportFullAsync(cancellationToken));
    }

    [Fact]
    public async Task Dispose_DisposesDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        workspace.Dispose();

        // After disposal the database is gone; querying throws.
        await Assert.ThrowsAnyAsync<Exception>(
            () => workspace.QueryAsync("SELECT * FROM sales", 100, cancellationToken));
    }

    /// <summary>
    /// Defense in depth: the workspace connection is kept read-only at the SQLite engine level outside
    /// its narrow write windows, so a write that ever reached a read path is refused by the engine even
    /// if it slipped past the SQL text guard. The sanctioned manipulation path still succeeds.
    /// </summary>
    [Fact]
    public async Task Connection_IsReadOnlyByDefault_AndOnlyWritableInsideCommandWindow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = CreateWorkspace();
        await workspace.EnsureReadyAsync(Documents(), Loader(Csv), cancellationToken);

        var connection = GetConnection(workspace);

        // A direct write on the shared connection is refused because it is in query_only mode.
        using (var write = connection.CreateCommand())
        {
            write.CommandText = "DELETE FROM sales";
            var exception = Assert.Throws<SqliteException>(() => write.ExecuteNonQuery());
            Assert.Contains("readonly", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Reads still work on the same connection.
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT COUNT(*) FROM sales";
            Assert.Equal(3L, Convert.ToInt64(read.ExecuteScalar()));
        }

        // The sanctioned manipulation tool opens a write window and succeeds.
        await workspace.ExecuteAsync("UPDATE sales SET amount = '999' WHERE region = 'North'", cancellationToken);
        var updated = await workspace.QueryAsync("SELECT amount FROM sales WHERE region = 'North' LIMIT 1", 10, cancellationToken);
        Assert.Equal(999L, Convert.ToInt64(updated.Rows[0][0]));

        // Once the window closed, the connection is read-only again.
        using (var write = connection.CreateCommand())
        {
            write.CommandText = "DELETE FROM sales";
            Assert.Throws<SqliteException>(() => write.ExecuteNonQuery());
        }
    }

    private static SqliteConnection GetConnection(TabularWorkspace workspace)
    {
        var field = typeof(TabularWorkspace).GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance);

        return (SqliteConnection)field!.GetValue(workspace)!;
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

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
