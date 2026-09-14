using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Centralizes how the file-backed tabular workspace database is located and opened so every caller
/// agrees on the path layout, the connection string, and the connection's initial write state.
/// </summary>
internal static partial class TabularWorkspaceDatabase
{
    private const string DatabaseFileName = "tabular.db";
    private const string DocumentsFolderName = "documents";
    private const string DataFolderName = "data";

    /// <summary>
    /// Builds the absolute path of the workspace database for a conversation scope.
    /// </summary>
    /// <param name="basePath">The document file store base path.</param>
    /// <param name="referenceType">The reference type owning the workspace.</param>
    /// <param name="referenceId">The reference identifier owning the workspace.</param>
    /// <returns>The database path, or <see langword="null"/> when the scope is incomplete or unsafe.</returns>
    public static string GetDatabasePath(string basePath, string referenceType, string referenceId)
    {
        if (string.IsNullOrEmpty(basePath) || !IsSafeScope(referenceType, referenceId))
        {
            return null;
        }

        return Path.Combine(basePath, DocumentsFolderName, referenceType, referenceId, DataFolderName, DatabaseFileName);
    }

    /// <summary>
    /// Builds the workspace database path relative to the document file store, using forward slashes.
    /// </summary>
    /// <param name="referenceType">The reference type owning the workspace.</param>
    /// <param name="referenceId">The reference identifier owning the workspace.</param>
    /// <returns>The store-relative path, or <see langword="null"/> when the scope is incomplete or unsafe.</returns>
    public static string GetStorageRelativePath(string referenceType, string referenceId)
    {
        if (!IsSafeScope(referenceType, referenceId))
        {
            return null;
        }

        return string.Join('/', DocumentsFolderName, referenceType, referenceId, DataFolderName, DatabaseFileName);
    }

    /// <summary>
    /// Determines whether a scope maps to exactly one directory of its own.
    /// </summary>
    /// <remarks>
    /// Each conversation scope owns a separate database file, so isolation between chat sessions and
    /// chat interactions rests entirely on these two segments. Restricting them to a single path
    /// segment keeps a scope from ever resolving into another scope's directory, whatever the
    /// identifiers turn out to contain.
    /// </remarks>
    /// <param name="referenceType">The reference type owning the workspace.</param>
    /// <param name="referenceId">The reference identifier owning the workspace.</param>
    /// <returns><see langword="true"/> when both segments are safe; otherwise <see langword="false"/>.</returns>
    private static bool IsSafeScope(string referenceType, string referenceId)
    {
        return IsSafePathSegment(referenceType) && IsSafePathSegment(referenceId);
    }

    private static bool IsSafePathSegment(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value is not "." and not ".."
            && SafePathSegmentExpression().IsMatch(value);
    }

    [GeneratedRegex("^[a-zA-Z0-9._-]+$")]
    private static partial Regex SafePathSegmentExpression();

    /// <summary>
    /// Returns the database file and its write-ahead-log sidecars, which must be removed together.
    /// </summary>
    /// <param name="databasePath">The database path.</param>
    /// <returns>The database path followed by its <c>-wal</c> and <c>-shm</c> sidecars.</returns>
    public static string[] GetDatabaseFilePaths(string databasePath)
    {
        return [databasePath, databasePath + "-wal", databasePath + "-shm"];
    }

    /// <summary>
    /// Builds the connection string for a workspace database.
    /// </summary>
    /// <remarks>
    /// Connection pooling is disabled for file-backed workspaces. A pooled connection keeps the
    /// underlying SQLite handle open after <see cref="SqliteConnection.Dispose"/>, which both leaks
    /// connection-level state such as the <c>query_only</c> pragma to the next caller that opens the
    /// same file and holds a file lock that prevents the database from being deleted. The workspace
    /// opens a single long-lived connection per scope, so pooling buys nothing here.
    /// </remarks>
    /// <param name="databasePath">The database path, or <see langword="null"/> for an in-memory workspace.</param>
    /// <returns>The connection string.</returns>
    public static string BuildConnectionString(string databasePath)
    {
        if (string.IsNullOrEmpty(databasePath))
        {
            return "Data Source=:memory:";
        }

        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString();
    }

    /// <summary>
    /// Opens a connection to a workspace database and puts it into a known-writable state.
    /// </summary>
    /// <param name="databasePath">The database path, or <see langword="null"/> for an in-memory workspace.</param>
    /// <returns>The open connection.</returns>
    public static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection(BuildConnectionString(databasePath));
        connection.Open();

        // Never inherit the write state from whatever opened this database last. The workspace runs
        // most of its life with query_only turned on, so a caller that assumed the SQLite default
        // would otherwise fail with "attempt to write a readonly database".
        SetWritable(connection, true);

        return connection;
    }

    /// <summary>
    /// Toggles SQLite's connection-level <c>query_only</c> flag.
    /// </summary>
    /// <param name="connection">The connection to toggle.</param>
    /// <param name="writable">When <see langword="true"/>, writes are allowed; otherwise they are blocked.</param>
    public static void SetWritable(SqliteConnection connection, bool writable)
    {
        if (connection is null)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = writable ? "PRAGMA query_only = OFF" : "PRAGMA query_only = ON";
        command.ExecuteNonQuery();
    }
}
