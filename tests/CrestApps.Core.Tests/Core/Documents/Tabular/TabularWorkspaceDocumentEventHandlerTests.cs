using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Handlers;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class TabularWorkspaceDocumentEventHandlerTests
{
    [Fact]
    public async Task UploadedAsync_TabularDocument_DoesNotSaveArtifact()
    {
        var artifactStore = new Mock<ITabularDocumentArtifactStore>();
        var handler = CreateHandler(artifactStore);

        await handler.UploadedAsync(new AIChatDocumentUploadContext
        {
            ReferenceId = "session-1",
            ReferenceType = AIReferenceTypes.Document.ChatSession,
            UploadedDocuments =
            [
                new AIChatUploadedDocument
                {
                    DocumentInfo = new ChatDocumentInfo
                    {
                        DocumentId = "doc-1",
                        FileName = "survey.csv",
                    },
                },
            ],
        }, TestContext.Current.CancellationToken);

        artifactStore.Verify(
            store => store.SaveAsync(It.IsAny<string>(), It.IsAny<TabularDocumentArtifact>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RemovedAsync_NonTabularDocument_DoesNotDeleteArtifact()
    {
        var artifactStore = new Mock<ITabularDocumentArtifactStore>();
        var handler = CreateHandler(artifactStore);

        await handler.RemovedAsync(new AIChatDocumentRemoveContext
        {
            ReferenceId = "session-1",
            ReferenceType = AIReferenceTypes.Document.ChatSession,
            DocumentInfo = new ChatDocumentInfo
            {
                DocumentId = "doc-1",
                FileName = "notes.txt",
            },
        }, TestContext.Current.CancellationToken);

        artifactStore.Verify(
            store => store.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RemovedAsync_TabularDocument_DeletesArtifact()
    {
        var artifactStore = new Mock<ITabularDocumentArtifactStore>();
        var handler = CreateHandler(artifactStore);

        await handler.RemovedAsync(new AIChatDocumentRemoveContext
        {
            ReferenceId = "session-1",
            ReferenceType = AIReferenceTypes.Document.ChatSession,
            DocumentInfo = new ChatDocumentInfo
            {
                DocumentId = "doc-1",
                FileName = "data.csv",
            },
        }, TestContext.Current.CancellationToken);

        artifactStore.Verify(
            store => store.DeleteAsync("doc-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemovedAsync_WhenTheDocumentOwnedTheLastTables_DeletesTheDatabaseFile()
    {
        var basePath = CreateTemporaryBasePath();

        try
        {
            var databasePath = CreateWorkspaceDatabase(basePath, "session-1", "doc-1", "sales", "sales_q2");

            var artifactStore = new Mock<ITabularDocumentArtifactStore>();
            var handler = CreateHandler(artifactStore, basePath);

            await handler.RemovedAsync(new AIChatDocumentRemoveContext
            {
                ReferenceId = "session-1",
                ReferenceType = AIReferenceTypes.Document.ChatSession,
                DocumentInfo = new ChatDocumentInfo
                {
                    DocumentId = "doc-1",
                    FileName = "data.csv",
                },
            }, TestContext.Current.CancellationToken);

            // The handler's own connection must release the file handle when it closes. A pooled
            // connection keeps the SQLite handle open after Dispose and the delete silently fails.
            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            TryDeleteDirectory(basePath);
        }
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../session-2")]
    [InlineData("session-1/../session-2")]
    public async Task RemovedAsync_WhenTheScopeIsNotASinglePathSegment_LeavesEveryDatabaseAlone(string referenceId)
    {
        var basePath = CreateTemporaryBasePath();

        try
        {
            // Each conversation scope owns its own database file, so a reference id that walks out of
            // its directory would let one scope drop another scope's tables.
            var databasePath = CreateWorkspaceDatabase(basePath, "session-2", "doc-1", "sales");

            var artifactStore = new Mock<ITabularDocumentArtifactStore>();
            var handler = CreateHandler(artifactStore, basePath);

            await handler.RemovedAsync(new AIChatDocumentRemoveContext
            {
                ReferenceId = referenceId,
                ReferenceType = AIReferenceTypes.Document.ChatSession,
                DocumentInfo = new ChatDocumentInfo
                {
                    DocumentId = "doc-1",
                    FileName = "data.csv",
                },
            }, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(databasePath));
            Assert.Equal(["sales"], GetUserTableNames(databasePath));
        }
        finally
        {
            TryDeleteDirectory(basePath);
        }
    }

    [Fact]
    public async Task RemovedAsync_WhenAnEarlierConnectionLeftTheDatabaseReadOnly_StillDropsTheDocumentTables()
    {
        var basePath = CreateTemporaryBasePath();

        try
        {
            var databasePath = CreateWorkspaceDatabase(basePath, "session-1", "doc-1", "sales");
            AddWorkspaceTable(databasePath, "doc-2", "budget");

            // The workspace keeps its own connection in query_only mode between write windows. A
            // pooled connection carries that pragma over to the next caller that opens the same file,
            // so every write then fails with "attempt to write a readonly database".
            LeaveDatabaseReadOnlyInThePool(databasePath);

            var artifactStore = new Mock<ITabularDocumentArtifactStore>();
            var handler = CreateHandler(artifactStore, basePath);

            await handler.RemovedAsync(new AIChatDocumentRemoveContext
            {
                ReferenceId = "session-1",
                ReferenceType = AIReferenceTypes.Document.ChatSession,
                DocumentInfo = new ChatDocumentInfo
                {
                    DocumentId = "doc-1",
                    FileName = "data.csv",
                },
            }, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(databasePath));
            Assert.Equal(["budget"], GetUserTableNames(databasePath));
        }
        finally
        {
            TryDeleteDirectory(basePath);
        }
    }

    private static string CreateTemporaryBasePath()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "tabular-doc-event-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(basePath);

        return basePath;
    }

    private static string CreateWorkspaceDatabase(string basePath, string referenceId, string documentId, params string[] tableNames)
    {
        var databasePath = Path.Combine(basePath, "documents", AIReferenceTypes.Document.ChatSession, referenceId, "data", "tabular.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath));

        using (var connection = OpenUnpooled(databasePath))
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS "_workspace_meta" (
                    "table_name" TEXT PRIMARY KEY,
                    "document_id" TEXT NOT NULL,
                    "worksheet_name" TEXT,
                    "file_name" TEXT NOT NULL,
                    "source_names_json" TEXT NOT NULL
                )
                """;
            command.ExecuteNonQuery();
        }

        foreach (var tableName in tableNames)
        {
            AddWorkspaceTable(databasePath, documentId, tableName);
        }

        return databasePath;
    }

    private static void AddWorkspaceTable(string databasePath, string documentId, string tableName)
    {
        using var connection = OpenUnpooled(databasePath);

        using (var createCommand = connection.CreateCommand())
        {
            createCommand.CommandText = $"CREATE TABLE \"{tableName}\" (\"value\" TEXT)";
            createCommand.ExecuteNonQuery();
        }

        using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = """
            INSERT INTO "_workspace_meta" ("table_name", "document_id", "worksheet_name", "file_name", "source_names_json")
            VALUES ($tableName, $documentId, NULL, 'data.csv', '{}')
            """;
        insertCommand.Parameters.AddWithValue("$tableName", tableName);
        insertCommand.Parameters.AddWithValue("$documentId", documentId);
        insertCommand.ExecuteNonQuery();
    }

    private static void LeaveDatabaseReadOnlyInThePool(string databasePath)
    {
        // Uses the pooled connection string on purpose: disposing this connection returns it to the
        // pool still carrying query_only = ON, which is what production hit.
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA query_only = ON";
        command.ExecuteNonQuery();
    }

    private static List<string> GetUserTableNames(string databasePath)
    {
        var names = new List<string>();

        using var connection = OpenUnpooled(databasePath);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name <> '_workspace_meta' ORDER BY name";

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static SqliteConnection OpenUnpooled(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();

        return connection;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static TabularWorkspaceDocumentEventHandler CreateHandler(
        Mock<ITabularDocumentArtifactStore> artifactStore,
        string basePath = null)
    {
        var options = new ChatDocumentsOptions();
        options.Add(new ExtractorExtension(".csv", embeddable: false, isTabular: true));

        var fileStoreOptions = new DocumentFileSystemFileStoreOptions
        {
            BasePath = basePath ?? Path.Combine(Path.GetTempPath(), "tabular-doc-event-tests"),
        };

        return new TabularWorkspaceDocumentEventHandler(
            Options.Create(options),
            artifactStore.Object,
            Options.Create(fileStoreOptions),
            NullLogger<TabularWorkspaceDocumentEventHandler>.Instance);
    }
}
