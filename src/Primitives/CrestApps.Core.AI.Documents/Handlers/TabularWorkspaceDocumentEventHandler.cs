using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Handlers;

internal sealed class TabularWorkspaceDocumentEventHandler : IAIChatDocumentEventHandler
{
    private readonly ChatDocumentsOptions _documentOptions;
    private readonly ITabularDocumentArtifactStore _artifactStore;
    private readonly string _basePath;
    private readonly ILogger<TabularWorkspaceDocumentEventHandler> _logger;

    public TabularWorkspaceDocumentEventHandler(
        IOptions<ChatDocumentsOptions> documentOptions,
        ITabularDocumentArtifactStore artifactStore,
        IOptions<DocumentFileSystemFileStoreOptions> fileStoreOptions,
        ILogger<TabularWorkspaceDocumentEventHandler> logger)
    {
        _documentOptions = documentOptions.Value;
        _artifactStore = artifactStore;
        _basePath = fileStoreOptions.Value.BasePath;
        _logger = logger;
    }

    public Task UploadedAsync(AIChatDocumentUploadContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task RemovedAsync(AIChatDocumentRemoveContext context, CancellationToken cancellationToken = default)
    {
        if (IsTabular(context?.DocumentInfo))
        {
            await _artifactStore.DeleteAsync(context.DocumentInfo.DocumentId, cancellationToken);
            TryDropDocumentTable(context.ReferenceType, context.ReferenceId, context.DocumentInfo.DocumentId);
        }
    }

    /// <summary>
    /// Opens the workspace database for the scope, drops every table associated with the removed
    /// document, removes its metadata rows, and deletes the database file if no tables remain.
    /// </summary>
    private void TryDropDocumentTable(string referenceType, string referenceId, string documentId)
    {
        if (string.IsNullOrEmpty(_basePath) || string.IsNullOrEmpty(referenceType) || string.IsNullOrEmpty(referenceId))
        {
            return;
        }

        var databasePath = Path.Combine(_basePath, "documents", referenceType, referenceId, "data", "tabular.db");

        if (!File.Exists(databasePath))
        {
            return;
        }

        try
        {
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();

            // A single document can produce multiple tables (one per worksheet), so drop them all.
            var tableNames = new List<string>();

            using (var lookupCommand = connection.CreateCommand())
            {
                lookupCommand.CommandText = """
                    SELECT table_name FROM "_workspace_meta" WHERE document_id = $documentId
                    """;
                lookupCommand.Parameters.AddWithValue("$documentId", documentId);

                using var reader = lookupCommand.ExecuteReader();

                while (reader.Read())
                {
                    tableNames.Add(reader.GetString(0));
                }
            }

            foreach (var tableName in tableNames)
            {
                using var dropCommand = connection.CreateCommand();
                dropCommand.CommandText = $"DROP TABLE IF EXISTS \"{tableName.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
                dropCommand.ExecuteNonQuery();
            }

            // Remove the metadata rows.
            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.CommandText = """
                    DELETE FROM "_workspace_meta" WHERE document_id = $documentId
                    """;
                deleteCommand.Parameters.AddWithValue("$documentId", documentId);
                deleteCommand.ExecuteNonQuery();
            }

            // If no tables remain, delete the database file.
            using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = """
                    SELECT COUNT(*) FROM "_workspace_meta"
                    """;
                var remaining = Convert.ToInt64(countCommand.ExecuteScalar());

                if (remaining == 0)
                {
                    connection.Close();
                    TryDeleteDatabaseFiles(databasePath);
                }
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "Failed to drop tables for document '{DocumentId}' from workspace database.", documentId);
            }
        }
    }

    private void TryDeleteDatabaseFiles(string databasePath)
    {
        TryDeleteFile(databasePath);
        TryDeleteFile(databasePath + "-wal");
        TryDeleteFile(databasePath + "-shm");
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "Failed to delete file '{Path}'.", path);
            }
        }
    }

    private bool IsTabular(ChatDocumentInfo document)
    {
        return document is not null && _documentOptions.IsTabularFileExtension(document.FileName);
    }
}
