using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.Tabular;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

/// <summary>
/// End-to-end tests that stream real workbooks through <see cref="OpenXmlTabularWorkspaceImporter"/>
/// into an in-memory SQLite database and assert on the resulting schema and query results. Each test
/// pins one of the import problems the fix targets: multi-tab tables, header detection, column typing,
/// subtotal handling, unlabeled columns, date conversion, and hidden-sheet skipping.
/// </summary>
public sealed class OpenXmlTabularWorkspaceImporterTests
{
    private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly OpenXmlTabularWorkspaceImporter _importer = new(NullLogger<OpenXmlTabularWorkspaceImporter>.Instance);

    /// <summary>
    /// Problem: every worksheet used to be merged into one table. Each worksheet must now become its
    /// own independent table, keeping its worksheet name, without data bleeding between sheets.
    /// </summary>
    [Fact]
    public async Task ImportAsync_MultipleWorksheets_CreatesOneIndependentTablePerSheet()
    {
        using var stream = BuildWorkbook(
            new SheetSpec("Client Breakdown",
            [
                ["Site", "Revenue"],
                ["Henderson", "100"],
                ["Milford", "200"],
            ]),
            new SheetSpec("Overall Projections",
            [
                ["Site Location", "Projection"],
                ["Henderson", "1420000"],
            ]));

        using var connection = OpenConnection();

        var results = await ImportAsync(stream, "revenue.xlsx", connection);

        Assert.Equal(2, results.Count);
        Assert.Collection(
            results,
            r => Assert.Equal("Client Breakdown", r.WorksheetName),
            r => Assert.Equal("Overall Projections", r.WorksheetName));

        Assert.Equal(2L, Scalar(connection, $"SELECT COUNT(*) FROM {Quote(results[0].TableName)}"));
        Assert.Equal(1L, Scalar(connection, $"SELECT COUNT(*) FROM {Quote(results[1].TableName)}"));
    }

    /// <summary>
    /// Problem: the header was assumed to be the first non-empty row, so a title/banner row above the
    /// real header corrupted the schema. The real header row must be detected and the title row skipped.
    /// </summary>
    [Fact]
    public async Task ImportAsync_TitleRowAboveHeader_UsesRealHeaderAndDropsTitleRow()
    {
        // Row 1 is a sparse date-band title (like "Projections - By Client"); row 2 is the real header.
        using var stream = BuildWorkbook(new SheetSpec("Projections",
        [
            ["", "", "46266"],
            ["CSD", "Client", "Production"],
            ["Alicia Welage", "BayCare", "74612"],
            ["Alicia Welage", "Hallmark", "184363"],
        ]));

        using var connection = OpenConnection();
        var result = Assert.Single(await ImportAsync(stream, "projections.xlsx", connection));

        var columns = ColumnNames(connection, result.TableName);
        Assert.Equal(["CSD", "Client", "Production"], columns);

        // Only the two genuine data rows are imported; the title row is dropped.
        Assert.Equal(2L, Scalar(connection, $"SELECT COUNT(*) FROM {Quote(result.TableName)}"));
        Assert.Equal("Alicia Welage", Scalar(connection, $"SELECT CSD FROM {Quote(result.TableName)} LIMIT 1"));
    }

    /// <summary>
    /// Problem: numeric columns were stored as TEXT, so math and ordering were wrong. Numeric columns
    /// must be typed so aggregation and numeric ordering work, even when the source cells are text.
    /// </summary>
    [Fact]
    public async Task ImportAsync_NumericColumns_AreTypedAndAggregatable()
    {
        using var stream = BuildWorkbook(new SheetSpec("Data",
        [
            ["Site", "Revenue"],
            ["Henderson", "1000"],
            ["Milford", "2500"],
            ["Nogales", "500"],
        ]));

        using var connection = OpenConnection();
        var result = Assert.Single(await ImportAsync(stream, "data.xlsx", connection));

        // Whole-number values type as INTEGER; the point is that the column is numeric, not TEXT, so
        // SUM and numeric ORDER BY behave correctly instead of aggregating/sorting text.
        Assert.Equal("INTEGER", ColumnType(connection, result.TableName, "Revenue"));
        Assert.Equal(4000L, Scalar(connection, $"SELECT SUM(Revenue) FROM {Quote(result.TableName)}"));
        Assert.Equal("Milford", Scalar(connection, $"SELECT Site FROM {Quote(result.TableName)} ORDER BY Revenue DESC LIMIT 1"));
    }

    /// <summary>
    /// Problem: embedded subtotal/total rows were imported as data and double-counted by aggregates.
    /// They must be flagged in an is_subtotal column so they can be excluded, while still being kept.
    /// </summary>
    [Fact]
    public async Task ImportAsync_SubtotalRows_AreFlaggedAndExcludableFromAggregates()
    {
        using var stream = BuildWorkbook(new SheetSpec("Breakdown",
        [
            ["Site", "Revenue"],
            ["Henderson", "100"],
            ["Milford", "200"],
            ["Site Total", "300"],
            ["Nogales", "50"],
            ["Totals:", "350"],
        ]));

        using var connection = OpenConnection();
        var result = Assert.Single(await ImportAsync(stream, "breakdown.xlsx", connection));

        Assert.Contains("is_subtotal", ColumnNames(connection, result.TableName));

        // All rows are kept.
        Assert.Equal(5L, Scalar(connection, $"SELECT COUNT(*) FROM {Quote(result.TableName)}"));
        // Two rollup rows are flagged.
        Assert.Equal(2L, Scalar(connection, $"SELECT COUNT(*) FROM {Quote(result.TableName)} WHERE is_subtotal = 1"));
        // Excluding them yields the true total (100 + 200 + 50) rather than the double-counted 1000.
        Assert.Equal(350L, Scalar(connection, $"SELECT SUM(Revenue) FROM {Quote(result.TableName)} WHERE is_subtotal = 0"));
    }

    /// <summary>
    /// Problem: a table with no subtotal rows should not gain an is_subtotal column.
    /// </summary>
    [Fact]
    public async Task ImportAsync_NoSubtotalRows_OmitsIsSubtotalColumn()
    {
        using var stream = BuildWorkbook(new SheetSpec("Data",
        [
            ["Site", "Revenue"],
            ["Henderson", "100"],
            ["Milford", "200"],
        ]));

        using var connection = OpenConnection();
        var result = Assert.Single(await ImportAsync(stream, "data.xlsx", connection));

        Assert.DoesNotContain("is_subtotal", ColumnNames(connection, result.TableName));
    }

    /// <summary>
    /// Problem: populated cells with no header (for example column K) were dropped. They must be
    /// imported under a synthesized column_N name instead of being lost.
    /// </summary>
    [Fact]
    public async Task ImportAsync_PopulatedColumnWithoutHeader_IsImportedAsSynthesizedColumn()
    {
        using var stream = BuildWorkbook(new SheetSpec("Breakdown",
        [
            ["Site", "Campaign", "Revenue"],
            ["True Blue", "BayCare", "71822", "Imaging"],
            ["True Blue", "CARE", "9137", "Food and Nutrition"],
        ]));

        using var connection = OpenConnection();
        var result = Assert.Single(await ImportAsync(stream, "breakdown.xlsx", connection));

        var columns = ColumnNames(connection, result.TableName);
        Assert.Contains("column_4", columns);

        Assert.Equal("Imaging", Scalar(connection, $"SELECT column_4 FROM {Quote(result.TableName)} WHERE Campaign = 'BayCare'"));
    }

    /// <summary>
    /// Problem: Excel date serials were left as opaque numbers. Date/time-formatted cells must be
    /// converted to ISO strings on import.
    /// </summary>
    [Fact]
    public async Task ImportAsync_DateFormattedColumn_StoredAsIsoString()
    {
        using var stream = BuildWorkbook(new SheetSpec("Revenue",
        [
            ["Client", "Month"],
            ["Aero", "45658"],
            ["Aero", "45689"],
        ],
        dateColumns: [1]));

        using var connection = OpenConnection();
        var result = Assert.Single(await ImportAsync(stream, "revenue.xlsx", connection));

        Assert.Equal("2025-01-01", Scalar(connection, $"SELECT Month FROM {Quote(result.TableName)} LIMIT 1"));
    }

    /// <summary>
    /// Problem: hidden lookup/scratch sheets were imported as opaque extra tables. They must be skipped
    /// by default so only visible worksheets become tables.
    /// </summary>
    [Fact]
    public async Task ImportAsync_HiddenWorksheet_IsSkipped()
    {
        using var stream = BuildWorkbook(
            new SheetSpec("Visible",
            [
                ["Site", "Revenue"],
                ["Henderson", "100"],
            ]),
            new SheetSpec("HiddenLookup",
            [
                ["Code", "Description"],
                ["A", "Internal"],
            ])
            { Hidden = true });

        using var connection = OpenConnection();
        var results = await ImportAsync(stream, "revenue.xlsx", connection);

        var result = Assert.Single(results);
        Assert.Equal("Visible", result.WorksheetName);
    }

    private async Task<IReadOnlyList<TabularWorkspaceImportResult>> ImportAsync(
        MemoryStream stream,
        string fileName,
        SqliteConnection connection)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TabularTableNameAllocator allocator = (worksheetName, singleWorksheet) =>
        {
            var baseName = singleWorksheet || string.IsNullOrWhiteSpace(worksheetName)
                ? Path.GetFileNameWithoutExtension(fileName)
                : worksheetName;
            var candidate = new string([.. (baseName ?? "data").Select(c => char.IsLetterOrDigit(c) ? c : '_')]).Trim('_');

            if (candidate.Length == 0)
            {
                candidate = "data";
            }

            var unique = candidate;
            var suffix = 2;

            while (!used.Add(unique))
            {
                unique = $"{candidate}_{suffix++}";
            }

            return unique;
        };

        return await _importer.ImportAsync(stream, fileName, ContentType, connection, allocator, TestContext.Current.CancellationToken);
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        return connection;
    }

    private static object Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar();
    }

    private static List<string> ColumnNames(SqliteConnection connection, string tableName)
    {
        var names = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({Quote(tableName)})";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            names.Add(reader["name"].ToString());
        }

        return names;
    }

    private static string ColumnType(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({Quote(tableName)})";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (string.Equals(reader["name"].ToString(), columnName, StringComparison.Ordinal))
            {
                return reader["type"].ToString();
            }
        }

        return null;
    }

    private static string Quote(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static MemoryStream BuildWorkbook(params SheetSpec[] sheets)
    {
        var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            // Style index 1 renders a value with the built-in date format (numFmtId 14).
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = new Stylesheet(
                new CellFormats(
                    new CellFormat(),
                    new CellFormat
                    {
                        NumberFormatId = 14,
                        ApplyNumberFormat = true,
                    }));
            stylesPart.Stylesheet.Save();

            var sheetElements = workbookPart.Workbook.AppendChild(new Sheets());
            uint sheetId = 1;

            foreach (var spec in sheets)
            {
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();

                for (var rowIndex = 0; rowIndex < spec.Rows.Length; rowIndex++)
                {
                    var excelRowIndex = (uint)rowIndex + 1;
                    var row = new Row { RowIndex = excelRowIndex };

                    for (var columnIndex = 0; columnIndex < spec.Rows[rowIndex].Length; columnIndex++)
                    {
                        var value = spec.Rows[rowIndex][columnIndex];
                        var cellReference = $"{(char)('A' + columnIndex)}{excelRowIndex}";

                        // Data cells in a date column are written as styled numeric serials so the reader
                        // must convert them; the header row and everything else are inline strings.
                        if (rowIndex > 0 && spec.DateColumns.Contains(columnIndex))
                        {
                            row.AppendChild(new Cell
                            {
                                CellReference = cellReference,
                                StyleIndex = 1,
                                CellValue = new CellValue(value),
                            });
                        }
                        else
                        {
                            row.AppendChild(new Cell
                            {
                                CellReference = cellReference,
                                DataType = CellValues.InlineString,
                                InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(value)),
                            });
                        }
                    }

                    sheetData.AppendChild(row);
                }

                worksheetPart.Worksheet = new Worksheet(sheetData);

                var sheet = new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = sheetId,
                    Name = spec.Name,
                };

                if (spec.Hidden)
                {
                    sheet.State = SheetStateValues.Hidden;
                }

                sheetElements.AppendChild(sheet);
                sheetId++;
            }
        }

        stream.Position = 0;

        return stream;
    }

    private sealed class SheetSpec(string name, string[][] rows, int[] dateColumns = null)
    {
        public string Name { get; } = name;

        public string[][] Rows { get; } = rows;

        public HashSet<int> DateColumns { get; } = [.. dateColumns ?? []];

        public bool Hidden { get; init; }
    }
}
