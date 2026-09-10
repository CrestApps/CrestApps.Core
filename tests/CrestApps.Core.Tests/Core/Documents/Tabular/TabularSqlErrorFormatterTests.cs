using CrestApps.Core.AI.Documents.Tabular;
using Microsoft.Data.Sqlite;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

/// <summary>
/// Verifies <see cref="TabularSqlErrorFormatter"/> against genuine <see cref="SqliteException"/>
/// instances (not hand-built messages), and specifically the fix that scopes a schema-error's column
/// dump to the table the failing query actually referenced instead of every loaded table.
/// </summary>
public sealed class TabularSqlErrorFormatterTests
{
    private static readonly List<TabularTableInfo> TwoTables =
    [
        new TabularTableInfo
        {
            TableName = "Client_Breakdown",
            Columns =
            [
                new("Site", "TEXT"),
                new("Campaign", "TEXT"),
                new("Total_Revenue", "REAL"),
                new("is_subtotal", "INTEGER"),
            ],
        },
        new TabularTableInfo
        {
            TableName = "Projections_By_Client",
            Columns =
            [
                new("Client_Name", "TEXT"),
                new("Total_Proj__Revnue_2026_09_01", "REAL"),
            ],
        },
    ];

    /// <summary>
    /// The exact real failure: the model guessed "Client" as a column name on a table whose real key
    /// column is "Campaign". With two tables loaded, the enriched error must show only Client_Breakdown's
    /// columns — the table the failing SQL actually named — not Projections_By_Client's as well.
    /// </summary>
    [Fact]
    public void Format_SchemaErrorNamingOneRealTable_ScopesColumnsToThatTableOnly()
    {
        var sql = "SELECT Client, Total_Revenue FROM Client_Breakdown WHERE is_subtotal = 0";
        var exception = CaptureRealSqliteException(sql);

        var message = TabularSqlErrorFormatter.Format("The query could not be executed", exception, TwoTables, sql);

        Assert.Contains("Client_Breakdown", message);
        Assert.Contains("Campaign", message);
        Assert.DoesNotContain("Projections_By_Client", message);
        Assert.DoesNotContain("Total_Proj__Revnue_2026_09_01", message);
    }

    /// <summary>
    /// A query that names no real table at all (a hallucinated table name) cannot be scoped to "the"
    /// table it meant, so every loaded table's columns must still be shown — the model needs the real
    /// table names, not just one guessed column's siblings.
    /// </summary>
    [Fact]
    public void Format_SchemaErrorNamingNoRealTable_FallsBackToEveryTable()
    {
        var sql = "SELECT * FROM Master_Client_Services";
        var exception = CaptureRealSqliteException(sql);

        var message = TabularSqlErrorFormatter.Format("The query could not be executed", exception, TwoTables, sql);

        Assert.Contains("Client_Breakdown", message);
        Assert.Contains("Projections_By_Client", message);
    }

    [Fact]
    public void Format_WithoutSql_FallsBackToEveryTable()
    {
        var exception = CaptureRealSqliteException("SELECT Client FROM Client_Breakdown");

        var message = TabularSqlErrorFormatter.Format("The query could not be executed", exception, TwoTables);

        Assert.Contains("Client_Breakdown", message);
        Assert.Contains("Projections_By_Client", message);
    }

    [Fact]
    public void Format_WithNonSchemaError_ReturnsBareMessageWithoutSchemaDump()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE Client_Breakdown (Campaign TEXT UNIQUE)";
        create.ExecuteNonQuery();

        using var seed = connection.CreateCommand();
        seed.CommandText = "INSERT INTO Client_Breakdown (Campaign) VALUES ('Eli Lilly')";
        seed.ExecuteNonQuery();

        using var duplicate = connection.CreateCommand();
        duplicate.CommandText = "INSERT INTO Client_Breakdown (Campaign) VALUES ('Eli Lilly')";
        var exception = Assert.Throws<SqliteException>(() => duplicate.ExecuteNonQuery());

        var message = TabularSqlErrorFormatter.Format("The command could not be executed", exception, TwoTables, duplicate.CommandText);

        Assert.DoesNotContain("Available tables and columns", message);
    }

    [Fact]
    public void Format_WithNoTablesLoaded_ReturnsBareMessage()
    {
        var exception = CaptureRealSqliteException("SELECT Client FROM Client_Breakdown");

        var message = TabularSqlErrorFormatter.Format("The query could not be executed", exception, []);

        Assert.DoesNotContain("Available tables and columns", message);
    }

    /// <summary>
    /// Runs the given SQL against a real in-memory Client_Breakdown table so the resulting exception's
    /// message is exactly what SQLite itself produces, not a hand-authored approximation of one.
    /// </summary>
    private static SqliteException CaptureRealSqliteException(string sql)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE Client_Breakdown (Campaign TEXT, Total_Revenue REAL, is_subtotal INTEGER)";
        create.ExecuteNonQuery();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
    }
}
