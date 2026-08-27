using System.Diagnostics;
using CrestApps.Core.AI.Documents.Tabular;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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
    /// Imports an Open XML spreadsheet into the supplied SQLite workspace, creating one table per
    /// worksheet.
    /// </summary>
    /// <param name="source">The spreadsheet stream.</param>
    /// <param name="fileName">The source file name.</param>
    /// <param name="contentType">The source content type.</param>
    /// <param name="connection">The SQLite workspace connection.</param>
    /// <param name="tableNameAllocator">Allocates a unique table name for each imported worksheet.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The import results, one per created table.</returns>
    public Task<IReadOnlyList<TabularWorkspaceImportResult>> ImportAsync(
        Stream source,
        string fileName,
        string contentType,
        SqliteConnection connection,
        TabularTableNameAllocator tableNameAllocator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(tableNameAllocator);

        if (source.CanSeek)
        {
            source.Position = 0;
        }

        var stopwatch = Stopwatch.StartNew();
        using var document = SpreadsheetDocument.Open(source, false);
        var workbookPart = document.WorkbookPart;

        var results = new List<TabularWorkspaceImportResult>();

        if (workbookPart == null)
        {
            var placeholderName = tableNameAllocator(null, true);
            TabularWorkspaceSqliteHelpers.CreateEmptyPlaceholderTable(connection, placeholderName);
            results.Add(new TabularWorkspaceImportResult(
                placeholderName,
                null,
                [new TabularColumnInfo("value", "TEXT")],
                0,
                0,
                1));

            return Task.FromResult<IReadOnlyList<TabularWorkspaceImportResult>>(results);
        }

        var singleWorksheet = (workbookPart.Workbook?.Sheets?.Elements<Sheet>().Count() ?? 0) <= 1;

        string worksheetName = null;
        string currentTableName = null;
        List<string> header = null;
        var sampleRows = new List<List<string>>(TabularWorkspaceSqliteHelpers.TypeSampleRowCount);
        IReadOnlyList<TabularColumnInfo> columns = null;
        SqliteCommand insertCommand = null;
        SqliteTransaction transaction = null;
        var rowCount = 0;
        var insertCommandCount = 0;

        // The table cannot be created until enough data rows have been seen to infer the column
        // storage types, so the leading rows are buffered and written once the table exists.
        void CreateTableAndFlushSampleRows()
        {
            currentTableName = tableNameAllocator(worksheetName, singleWorksheet);
            columns = TabularWorkspaceSqliteHelpers.BuildColumns(header, sampleRows);
            TabularWorkspaceSqliteHelpers.CreateTable(connection, currentTableName, columns);
            transaction = connection.BeginTransaction();
            insertCommand = CreateInsertCommand(connection, transaction, currentTableName, columns);

            foreach (var sampleRow in sampleRows)
            {
                BindInsertParameters(insertCommand, sampleRow, columns);
                insertCommand.ExecuteNonQuery();
                rowCount++;
                insertCommandCount++;
            }

            sampleRows.Clear();
        }

        try
        {
            OpenXmlTabularWorksheetReader.ReadWorksheets(
                workbookPart,
                fileName,
                _logger,
                name =>
                {
                    worksheetName = name;
                    currentTableName = null;
                    header = null;
                    sampleRows.Clear();
                    columns = null;
                    insertCommand = null;
                    transaction = null;
                    rowCount = 0;
                    insertCommandCount = 0;
                },
                row =>
                {
                    if (header == null)
                    {
                        header = row;

                        return;
                    }

                    if (columns == null)
                    {
                        sampleRows.Add(row);

                        if (sampleRows.Count >= TabularWorkspaceSqliteHelpers.TypeSampleRowCount)
                        {
                            CreateTableAndFlushSampleRows();
                        }

                        return;
                    }

                    BindInsertParameters(insertCommand, row, columns);
                    insertCommand.ExecuteNonQuery();
                    rowCount++;
                    insertCommandCount++;
                },
                () =>
                {
                    // A worksheet with no non-empty rows contributes no header, so it produces no table.
                    if (header == null)
                    {
                        return;
                    }

                    if (columns == null)
                    {
                        CreateTableAndFlushSampleRows();
                    }

                    transaction.Commit();
                    results.Add(new TabularWorkspaceImportResult(
                        currentTableName,
                        worksheetName,
                        columns,
                        rowCount,
                        insertCommandCount,
                        1));
                    insertCommand?.Dispose();
                    transaction.Dispose();
                    insertCommand = null;
                    transaction = null;
                },
                cancellationToken);

            if (results.Count == 0)
            {
                var placeholderName = tableNameAllocator(null, true);
                TabularWorkspaceSqliteHelpers.CreateEmptyPlaceholderTable(connection, placeholderName);
                results.Add(new TabularWorkspaceImportResult(
                    placeholderName,
                    null,
                    [new TabularColumnInfo("value", "TEXT")],
                    0,
                    0,
                    1));
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "OpenXml workspace importer loaded '{FileName}' into {TableCount} table(s) in {ElapsedMilliseconds} ms.",
                    fileName,
                    results.Count,
                    stopwatch.ElapsedMilliseconds);
            }

            return Task.FromResult<IReadOnlyList<TabularWorkspaceImportResult>>(results);
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
        IReadOnlyList<TabularColumnInfo> columns)
    {
        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            var value = columnIndex < row.Count ? row[columnIndex] : null;

            command.Parameters[columnIndex].Value = value is null || TabularWorkspaceSqliteHelpers.IsNullValue(columns[columnIndex].DeclaredType, value)
                ? DBNull.Value
                : value;
        }
    }
}
