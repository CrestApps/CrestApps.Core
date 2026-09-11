using System.Diagnostics;
using System.Globalization;
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
    // Bounds memory; above the largest real padding run seen in practice (~13,600 rows).
    private const int MaxPendingBlankRows = 25_000;

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

        // Runs parallel to leadingRows: whether each buffered row proved itself a rollup by formula.
        var leadingRowIsRollup = new List<bool>(profileRowCount);
        IReadOnlyList<TabularColumnInfo> dataColumns = null;
        var finalized = false;
        SqliteCommand insertCommand = null;
        SqliteTransaction transaction = null;
        var rowCount = 0;
        var insertCommandCount = 0;

        // Rollup rows are written to a sibling table rather than the data table, so an aggregate over
        // the data table cannot double-count the rows a rollup already covers. The sibling table is
        // created only when the worksheet actually contains one.
        string rollupTableName = null;
        SqliteCommand rollupInsertCommand = null;
        var rollupRowCount = 0;

        // Withheld until a following row proves the run isn't trailing formula-fill padding (e.g. "0" cells past the real data).
        List<int> textColumnIndexes = null;
        var pendingBlankRows = new List<List<string>>();

        void InsertDataRow(List<string> row)
        {
            BindDataRow(insertCommand, row, dataColumns);
            insertCommand.ExecuteNonQuery();
            rowCount++;
            insertCommandCount++;
        }

        void InsertRollupRow(List<string> row)
        {
            // Created on first use, so a worksheet without rollups gains no extra table.
            if (rollupInsertCommand is null)
            {
                rollupTableName = TabularWorksheetShaper.GetRollupTableName(currentTableName);
                TabularWorkspaceSqliteHelpers.CreateTable(connection, rollupTableName, dataColumns);
                rollupInsertCommand = CreateInsertCommand(connection, transaction, rollupTableName, dataColumns);
            }

            BindDataRow(rollupInsertCommand, row, dataColumns);
            rollupInsertCommand.ExecuteNonQuery();
            rollupRowCount++;
            insertCommandCount++;
        }

        void InsertRow(List<string> row, bool isRollup)
        {
            if (isRollup)
            {
                InsertRollupRow(row);

                return;
            }

            InsertDataRow(row);
        }

        bool IsStructurallyBlank(List<string> row)
        {
            if (textColumnIndexes.Count == 0)
            {
                return false;
            }

            foreach (var columnIndex in textColumnIndexes)
            {
                var value = columnIndex < row.Count ? row[columnIndex] : null;

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (TabularWorkspaceSqliteHelpers.TryNormalizeNumeric(value, out var normalized, out _) &&
                    double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric) &&
                    numeric == 0)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        void FlushPendingBlankRows()
        {
            if (pendingBlankRows.Count == 0)
            {
                return;
            }

            foreach (var blankRow in pendingBlankRows)
            {
                InsertDataRow(blankRow);
            }

            pendingBlankRows.Clear();
        }

        // The table cannot be created until the header row has been located and enough data rows sampled
        // to infer column types, so the leading rows are buffered and written once the table exists.
        void FinalizeTable()
        {
            var headerIndex = TabularWorksheetShaper.DetectHeaderRowIndex(leadingRows);
            var header = TabularWorksheetShaper.FixDuplicateColumnNames(leadingRows, headerIndex);
            var dataRows = leadingRows.GetRange(headerIndex + 1, leadingRows.Count - headerIndex - 1);
            var expandedHeader = TabularWorksheetShaper.ExpandHeader(header, dataRows);

            dataColumns = TabularWorkspaceSqliteHelpers.BuildColumns(expandedHeader, dataRows);
            textColumnIndexes = [.. Enumerable.Range(0, dataColumns.Count).Where(index => string.Equals(dataColumns[index].DeclaredType, "TEXT", StringComparison.Ordinal))];

            currentTableName = tableName(worksheetName, singleWorksheet);
            TabularWorkspaceSqliteHelpers.CreateTable(connection, currentTableName, dataColumns);
            transaction = connection.BeginTransaction();
            insertCommand = CreateInsertCommand(connection, transaction, currentTableName, dataColumns);
            finalized = true;

            // Buffered rows are replayed through the same routing the streamed rows use, so a rollup
            // inside the profiling window is separated exactly like one encountered after it.
            for (var index = 0; index < dataRows.Count; index++)
            {
                var dataRow = dataRows[index];

                InsertRow(dataRow, TabularWorksheetShaper.IsSubtotalRow(dataRow, leadingRowIsRollup[headerIndex + 1 + index]));
            }

            leadingRows.Clear();
            leadingRowIsRollup.Clear();
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
                    leadingRowIsRollup.Clear();
                    dataColumns = null;
                    finalized = false;
                    insertCommand = null;
                    transaction = null;
                    rollupTableName = null;
                    rollupInsertCommand = null;
                    rollupRowCount = 0;
                    rowCount = 0;
                    insertCommandCount = 0;
                    textColumnIndexes = null;
                    pendingBlankRows.Clear();
                },
                (row, hasVerticalAggregateFormula) =>
                {
                    if (!finalized)
                    {
                        leadingRows.Add(row);
                        leadingRowIsRollup.Add(hasVerticalAggregateFormula);

                        if (leadingRows.Count >= profileRowCount)
                        {
                            FinalizeTable();
                        }

                        return;
                    }

                    if (IsStructurallyBlank(row))
                    {
                        pendingBlankRows.Add(row);

                        // Cap exceeded: too long to plausibly be padding, so keep it as real data.
                        if (pendingBlankRows.Count > MaxPendingBlankRows)
                        {
                            FlushPendingBlankRows();
                        }

                        return;
                    }

                    FlushPendingBlankRows();
                    InsertRow(row, TabularWorksheetShaper.IsSubtotalRow(row, hasVerticalAggregateFormula));
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

                    // Never followed by real data, so it's padding — discard instead of inserting.
                    if (pendingBlankRows.Count > 0)
                    {
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.LogDebug(
                                "OpenXml tabular reader discarded {DiscardedRowCount} trailing structurally-blank row(s) from worksheet '{WorksheetName}' for '{FileName}'.",
                                pendingBlankRows.Count,
                                worksheetName,
                                fileName);
                        }

                        pendingBlankRows.Clear();
                    }

                    transaction.Commit();
                    results.Add(new TabularWorkspaceImportResult(
                        currentTableName,
                        worksheetName,
                        dataColumns,
                        rowCount,
                        insertCommandCount,
                        1));

                    // Registered as a table in its own right, so it is listed, described, and queryable.
                    if (rollupTableName is not null)
                    {
                        results.Add(new TabularWorkspaceImportResult(
                            rollupTableName,
                            worksheetName,
                            dataColumns,
                            rollupRowCount,
                            0,
                            1));

                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.LogDebug(
                                "OpenXml workspace importer separated {RollupRowCount} rollup row(s) from worksheet '{WorksheetName}' into table '{RollupTableName}'.",
                                rollupRowCount,
                                worksheetName,
                                rollupTableName);
                        }
                    }

                    insertCommand?.Dispose();
                    rollupInsertCommand?.Dispose();
                    transaction.Dispose();
                    insertCommand = null;
                    rollupInsertCommand = null;
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
        IReadOnlyList<TabularColumnInfo> dataColumns)
    {
        for (var columnIndex = 0; columnIndex < dataColumns.Count; columnIndex++)
        {
            var value = columnIndex < row.Count ? row[columnIndex] : null;

            command.Parameters[columnIndex].Value = value is null || TabularWorkspaceSqliteHelpers.IsNullValue(dataColumns[columnIndex].DeclaredType, value)
                ? DBNull.Value
                : TabularWorkspaceSqliteHelpers.NormalizeCellValue(dataColumns[columnIndex].DeclaredType, value);
        }
    }
}
