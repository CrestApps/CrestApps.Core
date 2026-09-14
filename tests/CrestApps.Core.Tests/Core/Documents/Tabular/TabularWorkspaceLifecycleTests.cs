using CrestApps.Core.AI.Documents.Tabular;
using Microsoft.Data.Sqlite;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

/// <summary>
/// Covers what happens to a file-backed workspace database across requests: tables must not outlive
/// the document they came from, and one conversation's database must never be reachable from another.
/// </summary>
public sealed class TabularWorkspaceLifecycleTests : IDisposable
{
    private const string SalesCsv = "region,amount\nNorth,100\nSouth,200";
    private const string BudgetCsv = "team,total\nOps,10\nEng,20";

    private readonly string _root;

    public TabularWorkspaceLifecycleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tabular-lifecycle-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task EnsureReadyAsync_WhenADocumentIsNoLongerAttached_DropsItsTablesOnTheNextRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databasePath = DatabasePath("session-1");

        using (var workspace = CreateWorkspace(databasePath))
        {
            await workspace.EnsureReadyAsync(
                [Document("doc-1", "sales.csv"), Document("doc-2", "budget.csv")],
                LoaderFor(("doc-1", SalesCsv), ("doc-2", BudgetCsv)),
                cancellationToken);
        }

        Assert.Equal(["budget", "sales"], GetUserTableNames(databasePath));

        // The next request resolves only doc-1, so doc-2 is gone from the conversation.
        using (var workspace = CreateWorkspace(databasePath))
        {
            var tables = await workspace.EnsureReadyAsync(
                [Document("doc-1", "sales.csv")],
                LoaderFor(("doc-1", SalesCsv)),
                cancellationToken);

            var table = Assert.Single(tables);
            Assert.Equal("sales", table.TableName);
        }

        // The table is really gone from the database, not merely hidden from the model.
        Assert.Equal(["sales"], GetUserTableNames(databasePath));
        Assert.Equal(["sales"], GetMetadataTableNames(databasePath));
    }

    [Fact]
    public async Task EnsureReadyAsync_WhenEveryDocumentIsDetached_DropsEveryTable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databasePath = DatabasePath("session-1");

        using (var workspace = CreateWorkspace(databasePath))
        {
            await workspace.EnsureReadyAsync(
                [Document("doc-1", "sales.csv")],
                LoaderFor(("doc-1", SalesCsv)),
                cancellationToken);
        }

        using (var workspace = CreateWorkspace(databasePath))
        {
            var tables = await workspace.EnsureReadyAsync([], LoaderFor(), cancellationToken);

            Assert.Empty(tables);
        }

        Assert.Empty(GetUserTableNames(databasePath));
        Assert.Empty(GetMetadataTableNames(databasePath));
    }

    [Fact]
    public async Task EnsureReadyAsync_WhenADetachedDocumentHadSeveralWorksheets_DropsAllOfItsTables()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databasePath = DatabasePath("session-1");

        var multiSheet = new TabularDocumentArtifact
        {
            Worksheets =
            [
                new TabularWorksheet { Name = "Q1", Header = ["region", "amount"], Rows = [["North", "100"]] },
                new TabularWorksheet { Name = "Q2", Header = ["region", "amount"], Rows = [["South", "200"]] },
            ],
        };

        using (var workspace = CreateWorkspace(databasePath))
        {
            await workspace.EnsureReadyAsync(
                [Document("doc-1", "sales.xlsx"), Document("doc-2", "budget.csv")],
                (document, _) => Task.FromResult(
                    document.DocumentId == "doc-1"
                        ? multiSheet
                        : TabularDocumentArtifact.FromDelimitedContent(BudgetCsv, document.FileName)),
                null,
                cancellationToken);
        }

        Assert.Equal(["budget", "sales_Q1", "sales_Q2"], GetUserTableNames(databasePath));

        using (var workspace = CreateWorkspace(databasePath))
        {
            await workspace.EnsureReadyAsync(
                [Document("doc-2", "budget.csv")],
                LoaderFor(("doc-2", BudgetCsv)),
                cancellationToken);
        }

        Assert.Equal(["budget"], GetUserTableNames(databasePath));
    }

    [Fact]
    public async Task EnsureReadyAsync_DoesNotReachAnotherConversationsDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = DatabasePath("session-1");
        var second = DatabasePath("session-2");

        using (var workspace = CreateWorkspace(first))
        {
            await workspace.EnsureReadyAsync(
                [Document("doc-1", "sales.csv")],
                LoaderFor(("doc-1", SalesCsv)),
                cancellationToken);
        }

        using (var workspace = CreateWorkspace(second))
        {
            await workspace.EnsureReadyAsync(
                [Document("doc-2", "budget.csv")],
                LoaderFor(("doc-2", BudgetCsv)),
                cancellationToken);
        }

        // Two conversations, two files, neither aware of the other's tables.
        Assert.Equal(["sales"], GetUserTableNames(first));
        Assert.Equal(["budget"], GetUserTableNames(second));

        // Detaching everything from the first conversation leaves the second untouched.
        using (var workspace = CreateWorkspace(first))
        {
            await workspace.EnsureReadyAsync([], LoaderFor(), cancellationToken);
        }

        Assert.Empty(GetUserTableNames(first));
        Assert.Equal(["budget"], GetUserTableNames(second));
    }

    [Fact]
    public async Task EnsureReadyAsync_ASecondConversationNeverSeesTheFirstConversationsDocument()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = DatabasePath("session-1");
        var second = DatabasePath("session-2");

        using (var workspace = CreateWorkspace(first))
        {
            await workspace.EnsureReadyAsync(
                [Document("doc-1", "sales.csv")],
                LoaderFor(("doc-1", SalesCsv)),
                cancellationToken);
        }

        using var other = CreateWorkspace(second);
        var tables = await other.EnsureReadyAsync(
            [Document("doc-2", "budget.csv")],
            LoaderFor(("doc-2", BudgetCsv)),
            cancellationToken);

        var table = Assert.Single(tables);
        Assert.Equal("budget", table.TableName);

        // The other conversation's table is not queryable from here.
        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => other.QueryAsync("SELECT * FROM sales", 10, cancellationToken));

        Assert.Contains("no such table", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureReadyAsync_ReattachingADroppedDocument_RebuildsItsTable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databasePath = DatabasePath("session-1");

        using (var workspace = CreateWorkspace(databasePath))
        {
            await workspace.EnsureReadyAsync(
                [Document("doc-1", "sales.csv")],
                LoaderFor(("doc-1", SalesCsv)),
                cancellationToken);
        }

        using (var workspace = CreateWorkspace(databasePath))
        {
            await workspace.EnsureReadyAsync([], LoaderFor(), cancellationToken);
        }

        using (var workspace = CreateWorkspace(databasePath))
        {
            var tables = await workspace.EnsureReadyAsync(
                [Document("doc-1", "sales.csv")],
                LoaderFor(("doc-1", SalesCsv)),
                cancellationToken);

            var table = Assert.Single(tables);
            Assert.Equal("sales", table.TableName);
            Assert.Equal(2, table.RowCount);
        }
    }

    [Fact]
    public async Task EnsureReadyAsync_WhenADocumentsContentCannotBeLoaded_CreatesNoTable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databasePath = DatabasePath("session-1");

        using var workspace = CreateWorkspace(databasePath);

        // A placeholder table here would read to the model as a file that contains no data.
        var tables = await workspace.EnsureReadyAsync(
            [Document("doc-1", "projections.xlsx")],
            LoaderFor(),
            cancellationToken);

        Assert.Empty(tables);
        Assert.Empty(GetUserTableNames(databasePath));
        Assert.Empty(GetMetadataTableNames(databasePath));
    }

    [Fact]
    public async Task EnsureReadyAsync_WhenContentBecomesAvailableLater_RetriesTheFailedImport()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databasePath = DatabasePath("session-1");

        using (var workspace = CreateWorkspace(databasePath))
        {
            await workspace.EnsureReadyAsync([Document("doc-1", "sales.csv")], LoaderFor(), cancellationToken);
        }

        // Registering the failed import would make IsDocumentLoaded true forever after, so the
        // workspace would keep serving an empty table and never read the file again.
        using (var workspace = CreateWorkspace(databasePath))
        {
            var tables = await workspace.EnsureReadyAsync(
                [Document("doc-1", "sales.csv")],
                LoaderFor(("doc-1", SalesCsv)),
                cancellationToken);

            var table = Assert.Single(tables);
            Assert.Equal("sales", table.TableName);
            Assert.Equal(2, table.RowCount);
        }
    }

    [Fact]
    public async Task EnsureReadyAsync_WhenOneDocumentFails_KeepsTheDocumentsThatLoaded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databasePath = DatabasePath("session-1");

        using var workspace = CreateWorkspace(databasePath);

        var tables = await workspace.EnsureReadyAsync(
            [Document("doc-1", "sales.csv"), Document("doc-2", "budget.csv")],
            LoaderFor(("doc-1", SalesCsv)),
            cancellationToken);

        var table = Assert.Single(tables);
        Assert.Equal("sales", table.TableName);
        Assert.Equal(2, table.RowCount);
    }

    [Fact]
    public async Task EnsureReadyAsync_WhenTheDatabaseAlreadyHoldsAFailedImportsEmptyTable_ImportsTheDocumentAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databasePath = DatabasePath("session-1");

        // The shape an earlier build wrote when a document's content could not be loaded. The metadata
        // entry made the document count as loaded, so the empty table was served forever after.
        WritePlaceholderTable(databasePath, "sales", "doc-1", "sales.csv");

        using var workspace = CreateWorkspace(databasePath);

        var tables = await workspace.EnsureReadyAsync(
            [Document("doc-1", "sales.csv")],
            LoaderFor(("doc-1", SalesCsv)),
            cancellationToken);

        var table = Assert.Single(tables);
        Assert.Equal("sales", table.TableName);
        Assert.Equal(2, table.RowCount);
        Assert.Equal(["region", "amount"], table.Columns.Select(c => c.Name));
    }

    [Fact]
    public async Task EnsureReadyAsync_DoesNotReimportATableTheUserEmptied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var databasePath = DatabasePath("session-1");

        using var workspace = CreateWorkspace(databasePath);

        await workspace.EnsureReadyAsync(
            [Document("doc-1", "sales.csv")],
            LoaderFor(("doc-1", SalesCsv)),
            cancellationToken);

        // Deleting every row through the manipulation tool is a deliberate edit, not a failed import.
        await workspace.ExecuteAsync("DELETE FROM sales", cancellationToken);

        var tables = await workspace.EnsureReadyAsync(
            [Document("doc-1", "sales.csv")],
            LoaderFor(("doc-1", SalesCsv)),
            cancellationToken);

        var table = Assert.Single(tables);
        Assert.Equal(0, table.RowCount);
        Assert.Equal(["region", "amount"], table.Columns.Select(c => c.Name));
    }

    private static void WritePlaceholderTable(string databasePath, string tableName, string documentId, string fileName)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();

        using (var schemaCommand = connection.CreateCommand())
        {
            schemaCommand.CommandText = $"""
                CREATE TABLE IF NOT EXISTS "_workspace_meta" (
                    "table_name" TEXT PRIMARY KEY,
                    "document_id" TEXT NOT NULL,
                    "worksheet_name" TEXT,
                    "file_name" TEXT NOT NULL,
                    "source_names_json" TEXT NOT NULL
                );
                CREATE TABLE "{tableName}" ("value" TEXT);
                """;
            schemaCommand.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "_workspace_meta" ("table_name", "document_id", "worksheet_name", "file_name", "source_names_json")
            VALUES ($tableName, $documentId, NULL, $fileName, $sourceNames)
            """;
        command.Parameters.AddWithValue("$tableName", tableName);
        command.Parameters.AddWithValue("$documentId", documentId);
        command.Parameters.AddWithValue("$fileName", fileName);
        command.Parameters.AddWithValue("$sourceNames", "{\"value\":null}");
        command.ExecuteNonQuery();
    }

    private string DatabasePath(string referenceId)
    {
        var path = Path.Combine(_root, "documents", "chat-session", referenceId, "data", "tabular.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        return path;
    }

    private static TabularWorkspace CreateWorkspace(string databasePath)
    {
        return new TabularWorkspace(new TabularWorkspaceOptions(), databasePath);
    }

    private static TabularDocumentRef Document(string documentId, string fileName)
    {
        return new TabularDocumentRef(documentId, fileName);
    }

    private static Func<string, CancellationToken, Task<string>> LoaderFor(params (string DocumentId, string Content)[] contents)
    {
        var map = contents.ToDictionary(c => c.DocumentId, c => c.Content, StringComparer.Ordinal);

        return (documentId, _) => Task.FromResult(map.TryGetValue(documentId, out var content) ? content : string.Empty);
    }

    private static List<string> GetUserTableNames(string databasePath)
    {
        return ReadStrings(databasePath, "SELECT name FROM sqlite_master WHERE type = 'table' AND name <> '_workspace_meta' ORDER BY name");
    }

    private static List<string> GetMetadataTableNames(string databasePath)
    {
        return ReadStrings(databasePath, "SELECT table_name FROM \"_workspace_meta\" ORDER BY table_name");
    }

    private static List<string> ReadStrings(string databasePath, string sql)
    {
        var values = new List<string>();

        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
