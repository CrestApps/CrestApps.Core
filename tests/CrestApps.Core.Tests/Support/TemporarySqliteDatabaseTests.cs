using Microsoft.Data.Sqlite;

namespace CrestApps.Core.Tests.Support;

/// <summary>
/// Verifies that <see cref="TemporarySqliteDatabase"/> leaves no file or pooled connection behind.
/// </summary>
public sealed class TemporarySqliteDatabaseTests
{
    /// <summary>
    /// Verifies the database file is deleted even though a closed connection to it is still held by the pool.
    /// </summary>
    [Fact]
    public async Task Dispose_WhenThePoolStillHoldsAConnection_DeletesTheDatabaseFile()
    {
        var database = new TemporarySqliteDatabase();

        await using (var connection = new SqliteConnection(database.ConnectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE \"Items\" (\"Id\" INTEGER PRIMARY KEY)";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(database.DatabasePath));

        database.Dispose();

        Assert.False(File.Exists(database.DatabasePath));
    }
}
