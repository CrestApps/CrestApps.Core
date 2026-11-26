using Microsoft.Data.Sqlite;

namespace CrestApps.Core.Tests.Support;

/// <summary>
/// Provides a uniquely named SQLite database file in the temporary folder and removes it when disposed.
/// </summary>
/// <remarks>
/// Microsoft.Data.Sqlite keeps a closed connection open in its pool, and Windows cannot delete a file that is
/// still open. Disposing closes the pooled connections of this database first, so the file is deleted and no
/// connection is left for Microsoft.Data.Sqlite to close while the test process exits.
/// </remarks>
internal sealed class TemporarySqliteDatabase : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TemporarySqliteDatabase"/> class.
    /// </summary>
    public TemporarySqliteDatabase()
    {
        DatabasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        ConnectionString = $"Data Source={DatabasePath}";
    }

    /// <summary>
    /// Gets the full path of the database file.
    /// </summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Gets the connection string every connection to the database must use, so they share one pool.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Closes the pooled connections of the database and deletes its file.
    /// </summary>
    public void Dispose()
    {
        using (var connection = new SqliteConnection(ConnectionString))
        {
            SqliteConnection.ClearPool(connection);
        }

        File.Delete(DatabasePath);
    }
}
