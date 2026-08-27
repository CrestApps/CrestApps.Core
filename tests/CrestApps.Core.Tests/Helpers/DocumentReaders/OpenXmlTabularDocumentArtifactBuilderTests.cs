using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.Tabular;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Helpers.DocumentReaders;

public sealed class OpenXmlTabularDocumentArtifactBuilderTests
{
    private readonly OpenXmlTabularDocumentArtifactBuilder _builder = new(NullLogger<OpenXmlTabularDocumentArtifactBuilder>.Instance);

    /// <summary>
    /// Verifies that an empty worksheet produces an empty artifact.
    /// </summary>
    [Fact]
    public async Task CreateAsync_EmptyWorksheet_ReturnsEmptyArtifact()
    {
        await using var stream = CreateExcelWithSequentialRows(0, includeHeader: false);

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Empty(artifact.Header);
        Assert.Empty(artifact.Rows);
    }

    /// <summary>
    /// Verifies that a header-only workbook preserves the header without creating data rows.
    /// </summary>
    [Fact]
    public async Task CreateAsync_HeaderOnlyWorkbook_ReturnsHeaderWithoutRows()
    {
        await using var stream = CreateExcelWithSequentialRows(0);

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Equal(["Index"], artifact.Header);
        Assert.Empty(artifact.Rows);
    }

    [Fact]
    public async Task CreateAsync_SharedStringsWorkbook_ExtractsHeaderAndRows()
    {
        await using var stream = CreateExcelWithSharedStrings([["Title", "Question", "Answer"], ["Thor Weapon", "What is Thor's weapon?", "Mjolnir"],]);

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Equal(["Title", "Question", "Answer"], artifact.Header);
        Assert.Collection(
            artifact.Rows,
            row => Assert.Equal(["Thor Weapon", "What is Thor's weapon?", "Mjolnir"], row));
    }

    [Fact]
    public async Task CreateAsync_SparseCellsWorkbook_PreservesColumnPositions()
    {
        await using var stream = CreateExcelWithSparseCells();

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Equal(34, artifact.Header.Count);
        Assert.Equal("Q3_C28/What fast food or quick service restaurants have you visited?", artifact.Header[33]);
        Assert.Single(artifact.Rows);
        Assert.Equal(34, artifact.Rows[0].Count);
        Assert.Equal("1", artifact.Rows[0][33]);
    }

    [Fact]
    public async Task CreateAsync_BooleanWorkbook_ExtractsBooleanValues()
    {
        await using var stream = CreateExcelWithBooleans(true, false);

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Equal(["TRUE", "FALSE"], artifact.Header);
        Assert.Empty(artifact.Rows);
    }

    /// <summary>
    /// Verifies exact row preservation around the former initial-capacity boundary.
    /// </summary>
    /// <param name="rowCount">The number of data rows to read.</param>
    [Theory]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    public async Task CreateAsync_RowCountAroundInitialCapacity_PreservesEveryRow(int rowCount)
    {
        await using var stream = CreateExcelWithSequentialRows(rowCount);

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Equal(["Index"], artifact.Header);
        Assert.Equal(rowCount, artifact.Rows.Count);
        Assert.Equal("0", artifact.Rows[0][0]);
        Assert.Equal((rowCount - 1).ToString(), artifact.Rows[^1][0]);
    }

    /// <summary>
    /// Verifies that sequential data rows retain their source order.
    /// </summary>
    [Fact]
    public async Task CreateAsync_SequentialRows_PreservesSourceOrder()
    {
        await using var stream = CreateExcelWithSequentialRows(32);

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            Enumerable.Range(0, 32).Select(index => index.ToString()),
            artifact.Rows.Select(row => row[0]));
    }

    /// <summary>
    /// Verifies that a multi-sheet workbook keeps each worksheet as an independent table with its own
    /// name and header, instead of flattening every sheet into a single table.
    /// </summary>
    [Fact]
    public async Task CreateAsync_MultipleWorksheets_PreservesEachWorksheetIndependently()
    {
        await using var stream = CreateExcelWithMultipleSheets();

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        // Test 1 — both worksheets are discovered independently and in workbook order.
        Assert.Equal(
            ["Client Breakdown", "Overall Projections"],
            artifact.Worksheets.Select(worksheet => worksheet.Name));

        var clientBreakdown = artifact.Worksheets[0];
        var overallProjections = artifact.Worksheets[1];

        // Test 2 — the first worksheet uses its real header row, not a data row.
        Assert.Equal(["Site", "Campaign", "Total Revenue"], clientBreakdown.Header);
        Assert.Collection(
            clientBreakdown.Rows,
            row => Assert.Equal(["Henderson", "Spring", "100"], row));

        // Test 3 — the second worksheet skips its blank leading row and uses the real header row,
        // never the numeric data row.
        Assert.Equal(["Site Location", "Q1", "Q2", "Total"], overallProjections.Header);
        Assert.DoesNotContain("1420000", overallProjections.Header);
        Assert.Collection(
            overallProjections.Rows,
            row => Assert.Equal(["Henderson", "1063541.42", "1422826.53", "1420000"], row));

        // Test 4 — data from one worksheet is not merged into the other.
        Assert.DoesNotContain(
            clientBreakdown.Rows,
            row => row.Contains("1420000"));
        Assert.DoesNotContain(
            overallProjections.Rows,
            row => row.Contains("Spring"));
    }

    /// <summary>
    /// Verifies that the streaming workspace importer (the production path for Excel files) creates one
    /// SQLite table per worksheet, preserves the worksheet names, and never merges worksheet data.
    /// </summary>
    [Fact]
    public async Task Import_MultipleWorksheets_CreatesOneTablePerWorksheet()
    {
        await using var stream = CreateExcelWithMultipleSheets();
        var importer = new OpenXmlTabularWorkspaceImporter(NullLogger<OpenXmlTabularWorkspaceImporter>.Instance);

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        TabularTableNameAllocator allocator = (worksheetName, singleWorksheet) =>
        {
            var candidate = singleWorksheet || string.IsNullOrEmpty(worksheetName)
                ? "revenue"
                : $"revenue_{worksheetName.Replace(' ', '_')}";
            var unique = candidate;
            var suffix = 2;

            while (!usedNames.Add(unique))
            {
                unique = $"{candidate}_{suffix}";
                suffix++;
            }

            return unique;
        };

        var results = await importer.ImportAsync(
            stream,
            "revenue.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            connection,
            allocator,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.Equal(
            ["Client Breakdown", "Overall Projections"],
            results.Select(result => result.WorksheetName));

        var clientResult = results.Single(result => result.WorksheetName == "Client Breakdown");
        var projectionsResult = results.Single(result => result.WorksheetName == "Overall Projections");

        Assert.Equal(["Site", "Campaign", "Total_Revenue"], clientResult.Columns.Select(column => column.Name));
        Assert.Equal(["Site_Location", "Q1", "Q2", "Total"], projectionsResult.Columns.Select(column => column.Name));

        var clientCount = ExecuteScalar(connection, $"SELECT COUNT(*) FROM \"{clientResult.TableName}\"");
        var projectionsCount = ExecuteScalar(connection, $"SELECT COUNT(*) FROM \"{projectionsResult.TableName}\"");

        Assert.Equal(1L, clientCount);
        Assert.Equal(1L, projectionsCount);
    }

    /// <summary>
    /// Verifies that the streaming importer types a numeric column even when the worksheet holds fewer
    /// rows than the type sample, which is the path where the table is created as the worksheet ends.
    /// </summary>
    [Fact]
    public async Task Import_NumericColumnShorterThanTypeSample_UsesIntegerStorageType()
    {
        await using var stream = CreateExcelWithSequentialRows(5);

        var (connection, result) = await ImportSingleWorksheetAsync(stream);

        using (connection)
        {
            Assert.Equal("INTEGER", Assert.Single(result.Columns).DeclaredType);
            Assert.Equal(5L, ExecuteScalar(connection, $"SELECT COUNT(*) FROM \"{result.TableName}\""));
        }
    }

    /// <summary>
    /// Verifies that a worksheet longer than the type sample still types its column correctly and keeps
    /// every row, covering both the buffered rows and the rows streamed after the table is created.
    /// </summary>
    [Fact]
    public async Task Import_NumericColumnLongerThanTypeSample_OrdersNumerically()
    {
        const int rowCount = TabularWorkspaceSqliteHelpers.TypeSampleRowCount + 8;
        await using var stream = CreateExcelWithSequentialRows(rowCount);

        var (connection, result) = await ImportSingleWorksheetAsync(stream);

        using (connection)
        {
            Assert.Equal("INTEGER", Assert.Single(result.Columns).DeclaredType);
            Assert.Equal(rowCount, ExecuteScalar(connection, $"SELECT COUNT(*) FROM \"{result.TableName}\""));
            Assert.Equal(
                rowCount - 1,
                ExecuteScalar(connection, $"SELECT \"Index\" FROM \"{result.TableName}\" ORDER BY \"Index\" DESC LIMIT 1"));
        }
    }

    private static async Task<(SqliteConnection Connection, TabularWorkspaceImportResult Result)> ImportSingleWorksheetAsync(Stream stream)
    {
        var importer = new OpenXmlTabularWorkspaceImporter(NullLogger<OpenXmlTabularWorkspaceImporter>.Instance);
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var results = await importer.ImportAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            connection,
            (_, _) => "data",
            TestContext.Current.CancellationToken);

        return (connection, Assert.Single(results));
    }

    private static long ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar());
    }

    /// <summary>
    /// Verifies that a canceled operation stops before worksheet rows are materialized.
    /// </summary>
    [Fact]
    public async Task CreateAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        await using var stream = CreateExcelWithSequentialRows(1);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _builder.CreateAsync(
                stream,
                "test.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                cancellationTokenSource.Token));
    }

    private static MemoryStream CreateExcelWithSharedStrings(string[][] rows)
    {
        var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var allStrings = rows.SelectMany(r => r).Distinct().ToList();
            var sstPart = workbookPart.AddNewPart<SharedStringTablePart>();
            var sst = new SharedStringTable();
            foreach (var s in allStrings)
            {
                sst.AppendChild(new SharedStringItem(new DocumentFormat.OpenXml.Spreadsheet.Text(s)));
            }

            sstPart.SharedStringTable = sst;
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            uint rowIndex = 1;
            foreach (var rowData in rows)
            {
                var row = new Row
                {
                    RowIndex = rowIndex,
                };
                var colIndex = 0;
                foreach (var cellValue in rowData)
                {
                    var cellRef = $"{(char)('A' + colIndex)}{rowIndex}";
                    var cell = new Cell
                    {
                        CellReference = cellRef,
                        DataType = CellValues.SharedString,
                        CellValue = new CellValue(allStrings.IndexOf(cellValue).ToString()),
                    };
                    row.AppendChild(cell);
                    colIndex++;
                }

                sheetData.AppendChild(row);
                rowIndex++;
            }

            worksheetPart.Worksheet = new Worksheet(sheetData);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1",
            });
        }

        stream.Position = 0;

        return stream;
    }

    private static MemoryStream CreateExcelWithSparseCells()
    {
        var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            var header = new Row { RowIndex = 1 };
            header.AppendChild(new Cell
            {
                CellReference = "A1",
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text("Respondent")),
            });
            header.AppendChild(new Cell
            {
                CellReference = "AH1",
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text("Q3_C28/What fast food or quick service restaurants have you visited?")),
            });
            sheetData.AppendChild(header);

            var row = new Row { RowIndex = 2 };
            row.AppendChild(new Cell
            {
                CellReference = "A2",
                CellValue = new CellValue("1001"),
            });
            row.AppendChild(new Cell
            {
                CellReference = "AH2",
                CellValue = new CellValue("1"),
            });
            sheetData.AppendChild(row);

            worksheetPart.Worksheet = new Worksheet(sheetData);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1",
            });
        }

        stream.Position = 0;

        return stream;
    }

    private static MemoryStream CreateExcelWithBooleans(params bool[] values)
    {
        var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var row = new Row
            {
                RowIndex = 1,
            };

            for (var i = 0; i < values.Length; i++)
            {
                row.AppendChild(new Cell
                {
                    CellReference = $"{(char)('A' + i)}1",
                    DataType = CellValues.Boolean,
                    CellValue = new CellValue(values[i] ? "1" : "0"),
                });
            }

            sheetData.AppendChild(row);
            worksheetPart.Worksheet = new Worksheet(sheetData);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1",
            });
        }

        stream.Position = 0;

        return stream;
    }

    /// <summary>
    /// Creates a workbook with an optional header and sequential one-column data rows.
    /// </summary>
    /// <param name="rowCount">The number of data rows.</param>
    /// <param name="includeHeader">Whether to include the header row.</param>
    /// <returns>The workbook stream positioned at the beginning.</returns>
    private static MemoryStream CreateExcelWithSequentialRows(
        int rowCount,
        bool includeHeader = true)
    {
        var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            if (includeHeader)
            {
                sheetData.AppendChild(new Row(
                    new Cell
                    {
                        CellReference = "A1",
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text("Index")),
                    })
                {
                    RowIndex = 1,
                });
            }

            for (var index = 0; index < rowCount; index++)
            {
                var rowIndex = index + (includeHeader ? 2 : 1);
                sheetData.AppendChild(new Row(
                    new Cell
                    {
                        CellReference = $"A{rowIndex}",
                        CellValue = new CellValue(index.ToString()),
                    })
                {
                    RowIndex = (uint)rowIndex,
                });
            }

            worksheetPart.Worksheet = new Worksheet(sheetData);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1",
            });
        }

        stream.Position = 0;

        return stream;
    }

    private static MemoryStream CreateExcelWithMultipleSheets()
    {
        var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());

            AppendSheet(
                workbookPart,
                sheets,
                1,
                [
                    ["Site", "Campaign", "Total Revenue"],
                    ["Henderson", "Spring", "100"],
                ],
                "Client Breakdown");

            // The second worksheet has a blank leading row before its real header, mirroring the
            // failing workbook, and its first data row holds numeric values that must never be
            // promoted to column headers.
            AppendSheet(
                workbookPart,
                sheets,
                2,
                [
                    ["Site Location", "Q1", "Q2", "Total"],
                    ["Henderson", "1063541.42", "1422826.53", "1420000"],
                ],
                "Overall Projections",
                firstRowIndex: 2);
        }

        stream.Position = 0;

        return stream;
    }

    private static void AppendSheet(
        WorkbookPart workbookPart,
        Sheets sheets,
        uint sheetId,
        string[][] rows,
        string name = null,
        uint firstRowIndex = 1)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();

        // Emit explicit empty self-closing rows before the first data row (for example <row r="1"/>)
        // so tests can reproduce workbooks that place a blank row above the real header.
        for (uint emptyRowIndex = 1; emptyRowIndex < firstRowIndex; emptyRowIndex++)
        {
            sheetData.AppendChild(new Row
            {
                RowIndex = emptyRowIndex,
            });
        }

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var excelRowIndex = (uint)rowIndex + firstRowIndex;
            var row = new Row
            {
                RowIndex = excelRowIndex,
            };

            for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                row.AppendChild(new Cell
                {
                    CellReference = $"{(char)('A' + columnIndex)}{excelRowIndex}",
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(rows[rowIndex][columnIndex])),
                });
            }

            sheetData.AppendChild(row);
        }

        worksheetPart.Worksheet = new Worksheet(sheetData);
        sheets.AppendChild(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = sheetId,
            Name = name ?? $"Sheet{sheetId}",
        });
    }
}
