using System.Diagnostics;
using CrestApps.Core.AI.Documents.Tabular;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.OpenXml.Services;

/// <summary>
/// Streams Open XML spreadsheet rows directly into a SQLite tabular workspace.
/// </summary>
public sealed class OpenXmlTabularWorkspaceImporter : ITabularWorkspaceImporter
{
    private readonly ILogger<OpenXmlTabularWorkspaceImporter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenXmlTabularWorkspaceImporter"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public OpenXmlTabularWorkspaceImporter(ILogger<OpenXmlTabularWorkspaceImporter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Imports an Open XML spreadsheet into the supplied SQLite workspace table.
    /// </summary>
    /// <param name="source">The spreadsheet stream.</param>
    /// <param name="fileName">The source file name.</param>
    /// <param name="contentType">The source content type.</param>
    /// <param name="connection">The SQLite workspace connection.</param>
    /// <param name="tableName">The destination table name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The import result.</returns>
    public Task<TabularWorkspaceImportResult> ImportAsync(
        Stream source,
        string fileName,
        string contentType,
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrEmpty(tableName);

        if (source.CanSeek)
        {
            source.Position = 0;
        }

        var stopwatch = Stopwatch.StartNew();
        using var document = SpreadsheetDocument.Open(source, false);
        var workbookPart = document.WorkbookPart;

        if (workbookPart == null)
        {
            TabularWorkspaceSqliteHelpers.CreateEmptyPlaceholderTable(connection, tableName);

            return Task.FromResult(new TabularWorkspaceImportResult(
                [new TabularColumnInfo("value", "TEXT")],
                0,
                0,
                1));
        }

        List<string> header = null;
        IReadOnlyList<TabularColumnInfo> columns = null;
        SqliteCommand insertCommand = null;
        SqliteTransaction transaction = null;
        var rowCount = 0;
        var insertCommandCount = 0;

        try
        {
            OpenXmlTabularWorksheetReader.ReadNonEmptyRows(
                workbookPart,
                fileName,
                _logger,
                (row, firstNonEmptyRowInWorksheet) =>
                {
                    if (header == null)
                    {
                        header = row;
                        columns = TabularWorkspaceSqliteHelpers.BuildColumns(header);
                        TabularWorkspaceSqliteHelpers.CreateTable(connection, tableName, columns);
                        transaction = connection.BeginTransaction();
                        insertCommand = CreateInsertCommand(connection, transaction, tableName, columns);

                        return;
                    }

                    if (firstNonEmptyRowInWorksheet)
                    {
                        return;
                    }

                    BindInsertParameters(insertCommand, row, columns.Count);
                    insertCommand.ExecuteNonQuery();
                    rowCount++;
                    insertCommandCount++;
                },
                cancellationToken);

            if (header == null)
            {
                TabularWorkspaceSqliteHelpers.CreateEmptyPlaceholderTable(connection, tableName);

                return Task.FromResult(new TabularWorkspaceImportResult(
                    [new TabularColumnInfo("value", "TEXT")],
                    0,
                    0,
                    1));
            }

            transaction?.Commit();

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "OpenXml workspace importer loaded '{FileName}' into table '{TableName}' with {ColumnCount} column(s) and {RowCount} row(s) in {ElapsedMilliseconds} ms.",
                    fileName,
                    tableName,
                    columns.Count,
                    rowCount,
                    stopwatch.ElapsedMilliseconds);
            }

            return Task.FromResult(new TabularWorkspaceImportResult(columns, rowCount, insertCommandCount, 1));
        }
        catch
        {
            transaction?.Rollback();

            throw;
        }
        finally
        {
            insertCommand?.Dispose();
            transaction?.Dispose();
        }
    }

    private static SqliteCommand CreateInsertCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        IReadOnlyList<TabularColumnInfo> columns)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        var parameterNames = new string[columns.Count];

        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            var parameterName = $"$p{columnIndex}";
            parameterNames[columnIndex] = parameterName;

            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.Value = DBNull.Value;
            command.Parameters.Add(parameter);
        }

        var columnList = string.Join(", ", columns.Select(column => TabularWorkspaceSqliteHelpers.QuoteIdentifier(column.Name)));
        command.CommandText = $"INSERT INTO {TabularWorkspaceSqliteHelpers.QuoteIdentifier(tableName)} ({columnList}) VALUES ({string.Join(", ", parameterNames)})";
        command.Prepare();

        return command;
    }

    private static void BindInsertParameters(
        SqliteCommand command,
        List<string> row,
        int columnCount)
    {
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            command.Parameters[columnIndex].Value = columnIndex < row.Count
                ? (object)(row[columnIndex] ?? string.Empty)
                : DBNull.Value;
        }
    }
}
