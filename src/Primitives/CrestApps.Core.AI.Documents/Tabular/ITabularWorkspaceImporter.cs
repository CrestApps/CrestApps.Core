using Microsoft.Data.Sqlite;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Streams a tabular source file directly into a SQLite workspace for a specific extension.
/// Implementations can avoid materializing a full <see cref="TabularDocumentArtifact"/> when a
/// format-specific fast path is available.
/// </summary>
public interface ITabularWorkspaceImporter
{
    /// <summary>
    /// Imports the supplied source file directly into the target SQLite table.
    /// </summary>
    /// <param name="source">The source file stream.</param>
    /// <param name="fileName">The original file name.</param>
    /// <param name="contentType">The source content type.</param>
    /// <param name="connection">The SQLite connection that owns the workspace.</param>
    /// <param name="tableName">The destination table name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The import result, or <see langword="null"/> when the importer cannot handle the source.</returns>
    Task<TabularWorkspaceImportResult> ImportAsync(
        Stream source,
        string fileName,
        string contentType,
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken = default);
}
