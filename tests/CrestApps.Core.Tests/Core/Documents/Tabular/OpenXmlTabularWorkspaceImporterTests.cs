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
                ["Eastport", "100"],
                ["Westfield", "200"],
            ]),
            new SheetSpec("Overall Projections",
            [
                ["Site Location", "Projection"],
                ["Eastport", "1500000"],
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
            ["AD", "Client", "Production"],
            ["Dana Reed", "Contoso", "80000"],
            ["Dana Reed", "Proseware", "190000"],
        ]));

        using var connection = OpenConnection();
        var result = Assert.Single(await ImportAsync(stream, "projections.xlsx", connection));

        var columns = ColumnNames(connection, result.TableName);
        Assert.Equal(["AD", "Client", "Production"], columns);

        // Only the two genuine data rows are imported; the title row is dropped.
        Assert.Equal(2L, Scalar(connection, $"SELECT COUNT(*) FROM {Quote(result.TableName)}"));
        Assert.Equal("Dana Reed", Scalar(connection, $"SELECT AD FROM {Quote(result.TableName)} LIMIT 1"));
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
            ["Eastport", "1000"],
            ["Westfield", "2500"],
            ["Southgate", "500"],
        ]));

        using var connection = OpenConnection();
        var result = Assert.Single(await ImportAsync(stream, "data.xlsx", connection));

        // Whole-number values type as INTEGER; the point is that the column is numeric, not TEXT, so
        // SUM and numeric ORDER BY behave correctly instead of aggregating/sorting text.
        Assert.Equal("INTEGER", ColumnType(connection, result.TableName, "Revenue"));
        Assert.Equal(4000L, Scalar(connection, $"SELECT SUM(Revenue) FROM {Quote(result.TableName)}"));
        Assert.Equal("Westfield", Scalar(connection, $"SELECT Site FROM {Quote(result.TableName)} ORDER BY Revenue DESC LIMIT 1"));
    }

    /// <summary>
    /// Problem: numbers are often stored as text in a spreadsheet (currency, thousands separators,
    /// accounting negatives). These must be recognized as numeric and normalized so aggregates are
    /// correct, based on the row data rather than the cell's format.
    /// </summary>
    [Fact]
    public async Task ImportAsync_NumbersStoredAsText_AreTypedNumericAndAggregateCorrectly()
    {
        using var stream = BuildWorkbook(new SheetSpec("Money",
        [
            ["Site", "Currency", "Thousands", "Accounting"],
            ["Eastport", "$1,000.50", "2,500", "(100)"],
            ["Westfield", "$2,000.50", "1,500", "(400)"],
        ]));

        using var connection = OpenConnection();
        var result = Assert.Single(await ImportAsync(stream, "money.xlsx", connection));

        Assert.Equal("REAL", ColumnType(connection, result.TableName, "Currency"));
        Assert.Equal("INTEGER", ColumnType(connection, result.TableName, "Thousands"));
        Assert.Equal("INTEGER", ColumnType(connection, result.TableName, "Accounting"));

        Assert.Equal(3001.0, Scalar(connection, $"SELECT SUM(Currency) FROM {Quote(result.TableName)}"));
        Assert.Equal(4000L, Scalar(connection, $"SELECT SUM(Thousands) FROM {Quote(result.TableName)}"));
        Assert.Equal(-500L, Scalar(connection, $"SELECT SUM(Accounting) FROM {Quote(result.TableName)}"));
    }

    /// <summary>
    /// Problem: what happens when a value cannot be parsed to the column's type. The import must not
    /// fail; the bad cell is kept as text in the otherwise-numeric column and aggregates still work.
    /// </summary>
    [Fact]
    public async Task ImportAsync_NumericColumnWithOneBadRow_ImportsWithoutFailing()
    {
        var rows = new List<string[]> { new[] { "Site", "Revenue" } };
        for (var i = 0; i < 50; i++)
        {
            rows.Add([$"Site{i}", i == 49 ? "N/A" : "100"]);
        }

        using var stream = BuildWorkbook(new SheetSpec("Data", rows.ToArray()));
        using var connection = OpenConnection();

        var result = Assert.Single(await ImportAsync(stream, "data.xlsx", connection));

        // The column is still typed numeric from the majority of rows.
        Assert.Equal("INTEGER", ColumnType(connection, result.TableName, "Revenue"));
        // Every row is imported, including the bad one.
        Assert.Equal(50L, Scalar(connection, $"SELECT COUNT(*) FROM {Quote(result.TableName)}"));
        // The 49 numeric rows sum correctly; the "N/A" cell contributes 0 rather than breaking the sum.
        Assert.Equal(4900L, Convert.ToInt64(Scalar(connection, $"SELECT SUM(Revenue) FROM {Quote(result.TableName)}")));
    }

    /// <summary>
    /// Problem: embedded subtotal/total rows were imported alongside the rows they summarize, so a plain
    /// SUM double-counted them. Excluding them by convention (a flag column the caller has to filter on)
    /// fails silently whenever the filter is forgotten, so the two grains are separated into two tables
    /// instead: the data table must sum correctly with no filter at all.
    /// </summary>
    [Fact]
    public async Task ImportAsync_SubtotalRows_AreSeparatedIntoRollupTable()
    {
        using var stream = BuildWorkbook(new SheetSpec("Breakdown",
        [
            ["Site", "Revenue"],
            ["Eastport", "100"],
            ["Westfield", "200"],
            ["Site Total", "300"],
            ["Southgate", "50"],
            ["Totals:", "350"],
        ]));

        using var connection = OpenConnection();
        var results = await ImportAsync(stream, "breakdown.xlsx", connection);

        Assert.Equal(2, results.Count);
        var data = results[0];
        var rollups = results[1];
        Assert.Equal(TabularWorksheetShaper.GetRollupTableName(data.TableName), rollups.TableName);

        // The unqualified aggregate -- the one the model actually writes -- is now correct on its own.
        Assert.Equal(350L, Scalar(connection, $"SELECT SUM(Revenue) FROM {Quote(data.TableName)}"));
        Assert.Equal(3L, Scalar(connection, $"SELECT COUNT(*) FROM {Quote(data.TableName)}"));

        // Nothing is lost: the sheet's own totals stay available to reconcile against.
        Assert.Equal(2L, Scalar(connection, $"SELECT COUNT(*) FROM {Quote(rollups.TableName)}"));
        Assert.Equal(650L, Scalar(connection, $"SELECT SUM(Revenue) FROM {Quote(rollups.TableName)}"));

        // The flag column is gone; separation replaced it.
        Assert.DoesNotContain("is_subtotal", ColumnNames(connection, data.TableName));
    }

    /// <summary>
    /// Problem: a rollup row proves itself through its formula, not its label. A vertical aggregate
    /// (=SUM over other rows) is a rollup even when the row carries no "Total" wording at all, which is
    /// how a sheet that labels its rollups "Site 1" or leaves them blank still imports correctly.
    /// </summary>
    [Fact]
    public async Task ImportAsync_VerticalAggregateFormulaWithoutTotalLabel_IsSeparated()
    {
        using var stream = BuildWorkbook(new SheetSpec("Breakdown",
        [
            ["Site", "Revenue"],
            ["Eastport", "100"],
            ["Westfield", "200"],
            ["Region A", "=SUM(B2:B3)|300"],
        ]));

        using var connection = OpenConnection();
        var results = await ImportAsync(stream, "breakdown.xlsx", connection);

        Assert.Equal(2, results.Count);
        Assert.Equal(300L, Scalar(connection, $"SELECT SUM(Revenue) FROM {Quote(results[0].TableName)}"));
        Assert.Equal("Region A", Scalar(connection, $"SELECT Site FROM {Quote(results[1].TableName)}"));
    }

    /// <summary>
    /// Problem: the mirror image of the case above, and the one that makes formula evidence safe to act
    /// on. A row total (=SUM across its own cells) is an ordinary record -- summing that column down the
    /// rows is exactly right -- so it must stay in the data table. Only aggregates over *other rows*
    /// double-count.
    /// </summary>
    [Fact]
    public async Task ImportAsync_HorizontalRowTotalFormula_StaysInDataTable()
    {
        using var stream = BuildWorkbook(new SheetSpec("Breakdown",
        [
            ["Site", "Production", "Ancillary", "Total"],
            ["Eastport", "100", "10", "=SUM(B2,C2)|110"],
            ["Westfield", "200", "20", "=SUM(B3,C3)|220"],
        ]));

        using var connection = OpenConnection();
        var result = Assert.Single(await ImportAsync(stream, "breakdown.xlsx", connection));

        Assert.Equal(2L, Scalar(connection, $"SELECT COUNT(*) FROM {Quote(result.TableName)}"));
        Assert.Equal(330L, Scalar(connection, $"SELECT SUM(Total) FROM {Quote(result.TableName)}"));
    }

    /// <summary>
    /// Problem: a workbook that pulls actuals alongside projections carries a cross-sheet lookup on
    /// every record. Reading those as rollups would move the entire sheet into the rollup table and
    /// leave the data table empty -- a far worse failure than the double-counting this change targets,
    /// and one that only showed up against a real workbook. Records that merely reference another sheet
    /// must stay put.
    /// </summary>
    [Fact]
    public async Task ImportAsync_RecordsCarryingCrossSheetLookups_StayInDataTable()
    {
        using var stream = BuildWorkbook(
            new SheetSpec("Projections",
            [
                ["AD", "Client", "Production", "Total", "Actual"],
                ["Dana", "Contoso", "100", "=SUM(C2:C2)|100", "=SUMIFS(Revenue!$K:$K,Revenue!$G:$G,'Projections'!$B2,Revenue!$J:$J,'Projections'!D$1)|0"],
                ["Jordan", "Coho", "200", "=SUM(C3:C3)|200", "=SUMIFS(Revenue!$K:$K,Revenue!$G:$G,'Projections'!$B3,Revenue!$J:$J,'Projections'!D$1)|0"],
            ]),
            new SheetSpec("Revenue",
            [
                ["Account", "Customer"],
                ["1", "Contoso"],
            ]));

        using var connection = OpenConnection();
        var results = await ImportAsync(stream, "master.xlsx", connection);

        // Two worksheets, and neither gains a rollup table.
        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, r => r.TableName.EndsWith(TabularWorksheetShaper.RollupTableSuffix, StringComparison.Ordinal));

        Assert.Equal(2L, Scalar(connection, $"SELECT COUNT(*) FROM {Quote(results[0].TableName)}"));
        Assert.Equal(300L, Scalar(connection, $"SELECT SUM(Total) FROM {Quote(results[0].TableName)}"));
    }

    /// <summary>
    /// Problem: the importer streams, and decided whether a worksheet had rollups from only the rows it
    /// buffered for profiling (header scan + type sample). A sheet whose first rollup sits past that
    /// window silently kept every rollup as data. Detection must hold for the whole sheet, however late
    /// the first rollup appears.
    /// </summary>
    [Fact]
    public async Task ImportAsync_RollupBeyondProfilingWindow_IsStillSeparated()
    {
        var profileRowCount = TabularWorksheetShaper.HeaderScanRows + TabularWorkspaceSqliteHelpers.TypeSampleRowCount;
        List<string[]> rows = [["Site", "Revenue"]];

        for (var i = 0; i < profileRowCount + 40; i++)
        {
            rows.Add([$"Site{i}", "10"]);
        }

        var expected = (profileRowCount + 40) * 10;
        rows.Add(["Grand Total", expected.ToString()]);

        using var stream = BuildWorkbook(new SheetSpec("Breakdown", rows.ToArray()));
        using var connection = OpenConnection();

        var results = await ImportAsync(stream, "breakdown.xlsx", connection);

        Assert.Equal(2, results.Count);
        Assert.Equal(expected, Convert.ToInt64(Scalar(connection, $"SELECT SUM(Revenue) FROM {Quote(results[0].TableName)}")));
        Assert.Equal(1L, Scalar(connection, $"SELECT COUNT(*) FROM {Quote(results[1].TableName)}"));
    }

    /// <summary>
    /// Problem: separating rows is only safe if the classifier does not claim real records. A client
    /// whose name merely begins with "Total" is a data row, and moving its revenue into the rollup table
    /// would understate the very total this change exists to get right.
    /// </summary>
    [Theory]
    [InlineData("Total Wine & More")]
    [InlineData("Total Quality Logistics")]
    public async Task ImportAsync_ClientNameBeginningWithTotal_StaysInDataTable(string clientName)
    {
        using var stream = BuildWorkbook(new SheetSpec("Breakdown",
        [
            ["Client", "Revenue"],
            ["Eastport", "100"],
            [clientName, "250"],
        ]));

        using var connection = OpenConnection();
        var result = Assert.Single(await ImportAsync(stream, "breakdown.xlsx", connection));

        Assert.Equal(350L, Scalar(connection, $"SELECT SUM(Revenue) FROM {Quote(result.TableName)}"));
    }

    /// <summary>
    /// Regression for the reported bug, shaped like the sheet that produced it: per-site subtotals plus
    /// a grand total over those subtotals. Importing all three grains into one table returned exactly
    /// three times the real figure, which is what "a sum that doesn't match the sheet" looked like.
    /// </summary>
    [Fact]
    public async Task ImportAsync_SiteSubtotalsAndGrandTotal_DoNotInflateTheSum()
    {
        using var stream = BuildWorkbook(new SheetSpec("Client Breakdown",
        [
            ["Site", "Campaign", "Total Revenue"],
            ["Northside", "Contoso", "100"],
            ["Northside", "Tailspin", "200"],
            ["", "Northside Total", "=SUM(C2:C3)|300"],
            ["Rivertown", "Woodgrove", "400"],
            ["", "Rivertown Total", "=SUM(C5:C5)|400"],
            ["", "Company Total", "=SUM(C4,C6)|700"],
        ]));

        using var connection = OpenConnection();
        var results = await ImportAsync(stream, "revenue.xlsx", connection);

        Assert.Equal(2, results.Count);

        // 700, not 2100.
        Assert.Equal(700L, Scalar(connection, $"SELECT SUM({Quote("Total_Revenue")}) FROM {Quote(results[0].TableName)}"));
        Assert.Equal(3L, Scalar(connection, $"SELECT COUNT(*) FROM {Quote(results[0].TableName)}"));

        // The sheet's own grand total is preserved and reconciles against the data table.
        Assert.Equal(700L, Scalar(connection, $"SELECT MAX({Quote("Total_Revenue")}) FROM {Quote(results[1].TableName)}"));
    }

    /// <summary>
    /// Problem: a worksheet with no rollup rows must not gain an empty sibling table, so the common case
    /// stays exactly one table per sheet.
    /// </summary>
    [Fact]
    public async Task ImportAsync_NoSubtotalRows_CreatesNoRollupTable()
    {
        using var stream = BuildWorkbook(new SheetSpec("Data",
        [
            ["Site", "Revenue"],
            ["Eastport", "100"],
            ["Westfield", "200"],
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
            ["Northside", "Contoso", "80000", "Division A"],
            ["Northside", "Relecloud", "9000", "Division B"],
        ]));

        using var connection = OpenConnection();
        var result = Assert.Single(await ImportAsync(stream, "breakdown.xlsx", connection));

        var columns = ColumnNames(connection, result.TableName);
        Assert.Contains("column_4", columns);

        Assert.Equal("Division A", Scalar(connection, $"SELECT column_4 FROM {Quote(result.TableName)} WHERE Campaign = 'Contoso'"));
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
    /// A date serial that carries a time-of-day fraction is converted to a full ISO date-time string,
    /// not just a date, so timestamped data keeps its time component.
    /// </summary>
    [Fact]
    public async Task ImportAsync_DateSerialWithTimeComponent_StoredAsIsoDateTime()
    {
        // 45658.75 = 2025-01-01 18:00:00.
        using var stream = BuildWorkbook(new SheetSpec("Events",
        [
            ["Event", "Occurred"],
            ["Start", "45658.75"],
        ],
        dateColumns: [1]));

        using var connection = OpenConnection();
        var result = Assert.Single(await ImportAsync(stream, "events.xlsx", connection));

        Assert.Equal("2025-01-01 18:00:00", Scalar(connection, $"SELECT Occurred FROM {Quote(result.TableName)} LIMIT 1"));
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
                ["Eastport", "100"],
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

                        // "=FORMULA|cached" writes a real formula cell carrying its cached value, which
                        // is what Excel stores and what rollup detection reads.
                        if (value.StartsWith('='))
                        {
                            var parts = value[1..].Split('|');

                            row.AppendChild(new Cell
                            {
                                CellReference = cellReference,
                                CellFormula = new CellFormula(parts[0]),
                                CellValue = new CellValue(parts.Length > 1 ? parts[1] : "0"),
                            });

                            continue;
                        }

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
