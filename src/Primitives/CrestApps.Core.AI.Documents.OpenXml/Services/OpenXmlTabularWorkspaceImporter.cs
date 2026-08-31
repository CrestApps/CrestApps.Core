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
    /// <param name="tableName">Allocates a unique table name for each imported worksheet.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The import results, one per created table.</returns>
    public Task<IReadOnlyList<TabularWorkspaceImportResult>> ImportAsync(
        Stream source,
        string fileName,
        string contentType,
        SqliteConnection connection,
        TabularTableNameAllocator tableName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(tableName);

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
            var placeholderName = tableName(null, true);
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

        // Only visible worksheets are imported (hidden lookup/scratch sheets are skipped by the reader),
        // so table naming is based on the count of visible sheets.
        var visibleSheetCount = workbookPart.Workbook?.Sheets?.Elements<Sheet>()
            .Count(sheet => sheet.State?.Value is not SheetStateValues state || state == SheetStateValues.Visible) ?? 0;
        var singleWorksheet = visibleSheetCount <= 1;

        // Rows are buffered until enough have been seen to both locate the header row (skipping any title
        // rows above it) and infer column storage types. HeaderScanRows covers the header search window
        // and TypeSampleRowCount the type sample taken from the data rows beneath it.
        var profileRowCount = TabularWorksheetShaper.HeaderScanRows + TabularWorkspaceSqliteHelpers.TypeSampleRowCount;

        string worksheetName = null;
        string currentTableName = null;
        var leadingRows = new List<List<string>>(profileRowCount);
        IReadOnlyList<TabularColumnInfo> dataColumns = null;
        IReadOnlyList<TabularColumnInfo> allColumns = null;
        var hasSubtotalColumn = false;
        var finalized = false;
        SqliteCommand insertCommand = null;
        SqliteTransaction transaction = null;
        var rowCount = 0;
        var insertCommandCount = 0;

        void InsertDataRow(List<string> row)
        {
            BindDataRow(insertCommand, row, dataColumns, hasSubtotalColumn);
            insertCommand.ExecuteNonQuery();
            rowCount++;
            insertCommandCount++;
        }

        // The table cannot be created until the header row has been located and enough data rows sampled
        // to infer column types, so the leading rows are buffered and written once the table exists.
        void FinalizeTable()
        {
            var headerIndex = TabularWorksheetShaper.DetectHeaderRowIndex(leadingRows);
            var header = leadingRows[headerIndex];
            var dataRows = leadingRows.GetRange(headerIndex + 1, leadingRows.Count - headerIndex - 1);
            var expandedHeader = TabularWorksheetShaper.ExpandHeader(header, dataRows);

            dataColumns = TabularWorkspaceSqliteHelpers.BuildColumns(expandedHeader, dataRows);
            hasSubtotalColumn = dataRows.Any(TabularWorksheetShaper.IsSubtotalRow);
            allColumns = hasSubtotalColumn
                ? [.. dataColumns, new TabularColumnInfo(TabularWorksheetShaper.SubtotalColumnName, "INTEGER")]
                : dataColumns;

            currentTableName = tableName(worksheetName, singleWorksheet);
            TabularWorkspaceSqliteHelpers.CreateTable(connection, currentTableName, allColumns);
            transaction = connection.BeginTransaction();
            insertCommand = CreateInsertCommand(connection, transaction, currentTableName, allColumns);
            finalized = true;

            foreach (var dataRow in dataRows)
            {
                InsertDataRow(dataRow);
            }

            leadingRows.Clear();
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
                    leadingRows.Clear();
                    dataColumns = null;
                    allColumns = null;
                    hasSubtotalColumn = false;
                    finalized = false;
                    insertCommand = null;
                    transaction = null;
                    rowCount = 0;
                    insertCommandCount = 0;
                },
                row =>
                {
                    if (!finalized)
                    {
                        leadingRows.Add(row);

                        if (leadingRows.Count >= profileRowCount)
                        {
                            FinalizeTable();
                        }

                        return;
                    }

                    InsertDataRow(row);
                },
                () =>
                {
                    // A worksheet with no non-empty rows produces no table.
                    if (!finalized)
                    {
                        if (leadingRows.Count == 0)
                        {
                            return;
                        }

                        FinalizeTable();
                    }

                    transaction.Commit();
                    results.Add(new TabularWorkspaceImportResult(
                        currentTableName,
                        worksheetName,
                        allColumns,
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
                var placeholderName = tableName(null, true);
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

    private static void BindDataRow(
        SqliteCommand command,
        List<string> row,
        IReadOnlyList<TabularColumnInfo> dataColumns,
        bool hasSubtotalColumn)
    {
        for (var columnIndex = 0; columnIndex < dataColumns.Count; columnIndex++)
        {
            var value = columnIndex < row.Count ? row[columnIndex] : null;

            command.Parameters[columnIndex].Value = value is null || TabularWorkspaceSqliteHelpers.IsNullValue(dataColumns[columnIndex].DeclaredType, value)
                ? DBNull.Value
                : TabularWorkspaceSqliteHelpers.NormalizeCellValue(dataColumns[columnIndex].DeclaredType, value);
        }

        if (hasSubtotalColumn)
        {
            command.Parameters[dataColumns.Count].Value = TabularWorksheetShaper.IsSubtotalRow(row) ? 1L : 0L;
        }
    }
}
