using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares legacy and current spreadsheet row extraction using in-memory Open XML workbooks.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class OpenXmlSpreadsheetRowExtractionBenchmarks
{
    private const string SpreadsheetMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string SparseScenario = "Sparse1000x34";
    private const string DenseScenario = "Dense10000x16";

    private readonly OpenXmlIngestionDocumentReader _reader = new();
    private byte[] _workbook;

    /// <summary>
    /// Gets or sets the synthetic workbook scenario.
    /// </summary>
    [Params(SparseScenario, DenseScenario)]
    public string Scenario { get; set; }

    /// <summary>
    /// Creates the synthetic workbook and verifies that the legacy and current paths produce identical output.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _workbook = Scenario == SparseScenario
            ? CreateWorkbook(rowCount: 1000, columnCount: 34, sparse: true)
            : CreateWorkbook(rowCount: 10000, columnCount: 16, sparse: false);

        var legacy = ReadLegacy();
        var current = ReadCurrent();
        var sharedStringCacheCandidate = ReadSharedStringCacheCandidate();
        var legacyText = legacy.Sections.SelectMany(section => section.Elements).Select(element => element.Text);
        var currentText = current.Sections.SelectMany(section => section.Elements).Select(element => element.Text);
        var sharedStringCacheCandidateText = sharedStringCacheCandidate.Sections
            .SelectMany(section => section.Elements)
            .Select(element => element.Text);

        if (!legacyText.SequenceEqual(currentText, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Legacy and current spreadsheet extraction produced different output.");
        }

        if (!legacyText.SequenceEqual(sharedStringCacheCandidateText, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Legacy and shared-string cache spreadsheet extraction produced different output.");
        }
    }

    /// <summary>
    /// Reads the workbook with the original list-based row extraction implementation.
    /// </summary>
    /// <returns>The extracted ingestion document.</returns>
    [Benchmark(Baseline = true)]
    public IngestionDocument ReadLegacy()
    {
        using var stream = new MemoryStream(_workbook, writable: false);

        return ExtractLegacy(stream);
    }

    /// <summary>
    /// Reads the workbook with the production spreadsheet extraction implementation.
    /// </summary>
    /// <returns>The extracted ingestion document.</returns>
    [Benchmark]
    public IngestionDocument ReadCurrent()
    {
        using var stream = new MemoryStream(_workbook, writable: false);

        return _reader.ReadAsync(stream, "benchmark.xlsx", SpreadsheetMediaType).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Reads the workbook with reusable row storage and a per-read shared-string cache.
    /// </summary>
    /// <returns>The extracted ingestion document.</returns>
    [Benchmark]
    public IngestionDocument ReadSharedStringCacheCandidate()
    {
        using var stream = new MemoryStream(_workbook, writable: false);

        return ExtractSharedStringCacheCandidate(stream);
    }

    /// <summary>
    /// Creates a synthetic workbook entirely in memory.
    /// </summary>
    /// <param name="rowCount">The number of worksheet rows.</param>
    /// <param name="columnCount">The logical number of worksheet columns.</param>
    /// <param name="sparse">Whether each row should contain only three populated cells.</param>
    /// <returns>The serialized workbook.</returns>
    private static byte[] CreateWorkbook(int rowCount, int columnCount, bool sparse)
    {
        using var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
            var sharedStrings = new SharedStringTable();

            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                sharedStrings.AppendChild(new SharedStringItem(new Text($"Value {columnIndex}")));
            }

            sharedStringPart.SharedStringTable = sharedStrings;

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            for (var rowIndex = 1; rowIndex <= rowCount; rowIndex++)
            {
                var row = new Row
                {
                    RowIndex = (uint)rowIndex,
                };

                if (sparse)
                {
                    AppendSharedStringCell(row, rowIndex, 0);
                    AppendSharedStringCell(row, rowIndex, columnCount / 2);
                    AppendSharedStringCell(row, rowIndex, columnCount - 1);
                }
                else
                {
                    for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                    {
                        AppendSharedStringCell(row, rowIndex, columnIndex);
                    }
                }

                sheetData.AppendChild(row);
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

        return stream.ToArray();
    }

    /// <summary>
    /// Appends a shared-string cell to a synthetic row.
    /// </summary>
    /// <param name="row">The row receiving the cell.</param>
    /// <param name="rowIndex">The one-based row index.</param>
    /// <param name="columnIndex">The zero-based column index.</param>
    private static void AppendSharedStringCell(Row row, int rowIndex, int columnIndex)
    {
        row.AppendChild(new Cell
        {
            CellReference = $"{GetColumnName(columnIndex)}{rowIndex}",
            DataType = CellValues.SharedString,
            CellValue = new CellValue(columnIndex.ToString()),
        });
    }

    /// <summary>
    /// Converts a zero-based column index to an Excel column name.
    /// </summary>
    /// <param name="columnIndex">The zero-based column index.</param>
    /// <returns>The Excel column name.</returns>
    private static string GetColumnName(int columnIndex)
    {
        var value = columnIndex + 1;
        var columnName = string.Empty;

        while (value > 0)
        {
            value--;
            columnName = (char)('A' + (value % 26)) + columnName;
            value /= 26;
        }

        return columnName;
    }

    /// <summary>
    /// Reproduces the original spreadsheet extraction implementation.
    /// </summary>
    /// <param name="stream">The workbook stream.</param>
    /// <returns>The extracted ingestion document.</returns>
    private static IngestionDocument ExtractLegacy(Stream stream)
    {
        using var document = SpreadsheetDocument.Open(stream, false);
        var result = new IngestionDocument("benchmark.xlsx");
        var workbook = document.WorkbookPart;

        if (workbook == null)
        {
            return result;
        }

        var sharedStrings = workbook.SharedStringTablePart?.SharedStringTable;
        var section = new IngestionDocumentSection();

        foreach (var sheet in workbook.WorksheetParts)
        {
            var data = sheet.Worksheet.GetFirstChild<SheetData>();

            if (data == null)
            {
                continue;
            }

            foreach (var row in data.Elements<Row>())
            {
                var values = GetLegacyRowValues(row, sharedStrings);

                if (values.Any(value => !string.IsNullOrEmpty(value)))
                {
                    var rowText = string.Join("\t", values);
                    section.Elements.Add(new IngestionDocumentParagraph(rowText)
                    {
                        Text = rowText,
                    });
                }
            }
        }

        if (section.Elements.Count > 0)
        {
            result.Sections.Add(section);
        }

        return result;
    }

    /// <summary>
    /// Reproduces the current reusable row storage with shared strings materialized once per read.
    /// </summary>
    /// <param name="stream">The workbook stream.</param>
    /// <returns>The extracted ingestion document.</returns>
    private static IngestionDocument ExtractSharedStringCacheCandidate(Stream stream)
    {
        using var document = SpreadsheetDocument.Open(stream, false);
        var result = new IngestionDocument("benchmark.xlsx");
        var workbook = document.WorkbookPart;

        if (workbook == null)
        {
            return result;
        }

        var sharedStrings = OpenXmlTabularWorksheetReader.CreateSharedStringCache(workbook);
        var section = new IngestionDocumentSection();
        var values = new List<string>();

        foreach (var sheet in workbook.WorksheetParts)
        {
            var data = sheet.Worksheet.GetFirstChild<SheetData>();

            if (data == null)
            {
                continue;
            }

            foreach (var row in data.Elements<Row>())
            {
                GetCachedRowValues(row, sharedStrings, values);

                if (values.Count > 0)
                {
                    var rowText = string.Join("\t", values);
                    section.Elements.Add(new IngestionDocumentParagraph(rowText)
                    {
                        Text = rowText,
                    });
                }
            }
        }

        if (section.Elements.Count > 0)
        {
            result.Sections.Add(section);
        }

        return result;
    }

    /// <summary>
    /// Populates reusable row storage using cached shared strings.
    /// </summary>
    /// <param name="row">The spreadsheet row.</param>
    /// <param name="sharedStrings">The cached shared strings.</param>
    /// <param name="values">The reusable row values.</param>
    private static void GetCachedRowValues(
        Row row,
        string[] sharedStrings,
        List<string> values)
    {
        values.Clear();

        foreach (var cell in row.Elements<Cell>())
        {
            var columnIndex = GetLegacyColumnIndex(cell.CellReference?.Value, values.Count);

            while (values.Count < columnIndex)
            {
                values.Add(string.Empty);
            }

            values.Add(GetCachedCellValue(cell, sharedStrings));
        }

        for (var index = values.Count - 1; index >= 0; index--)
        {
            if (!string.IsNullOrEmpty(values[index]))
            {
                break;
            }

            values.RemoveAt(index);
        }
    }

    /// <summary>
    /// Reproduces the original list-based row materialization.
    /// </summary>
    /// <param name="row">The spreadsheet row.</param>
    /// <param name="sharedStrings">The workbook shared-string table.</param>
    /// <returns>The logical cell values with trailing empty cells removed.</returns>
    private static List<string> GetLegacyRowValues(Row row, SharedStringTable sharedStrings)
    {
        var values = new List<string>();

        foreach (var cell in row.Elements<Cell>())
        {
            var columnIndex = GetLegacyColumnIndex(cell.CellReference?.Value, values.Count);

            while (values.Count < columnIndex)
            {
                values.Add(string.Empty);
            }

            values.Add(GetLegacyCellValue(cell, sharedStrings));
        }

        for (var index = values.Count - 1; index >= 0; index--)
        {
            if (!string.IsNullOrEmpty(values[index]))
            {
                break;
            }

            values.RemoveAt(index);
        }

        return values;
    }

    /// <summary>
    /// Reproduces the original Excel column-reference parsing.
    /// </summary>
    /// <param name="cellReference">The optional cell reference.</param>
    /// <param name="fallbackIndex">The fallback zero-based column index.</param>
    /// <returns>The zero-based column index.</returns>
    private static int GetLegacyColumnIndex(string cellReference, int fallbackIndex)
    {
        if (string.IsNullOrEmpty(cellReference))
        {
            return fallbackIndex;
        }

        var columnIndex = 0;
        var hasColumnName = false;

        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            hasColumnName = true;
            columnIndex = (columnIndex * 26) + char.ToUpperInvariant(character) - 'A' + 1;
        }

        return hasColumnName ? columnIndex - 1 : fallbackIndex;
    }

    /// <summary>
    /// Reproduces the original spreadsheet cell value conversion.
    /// </summary>
    /// <param name="cell">The spreadsheet cell.</param>
    /// <param name="table">The workbook shared-string table.</param>
    /// <returns>The extracted cell text.</returns>
    private static string GetLegacyCellValue(Cell cell, SharedStringTable table)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText ?? string.Empty;
        }

        if (cell.CellValue == null)
        {
            return string.Empty;
        }

        var value = cell.CellValue.InnerText;

        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(value, out var index) &&
            table != null)
        {
            var item = table.ChildElements.Count > index
                ? table.ChildElements[index]
                : null;

            return item?.InnerText ?? value;
        }

        if (cell.DataType?.Value == CellValues.Boolean)
        {
            return value == "1" ? "TRUE" : "FALSE";
        }

        return value;
    }

    /// <summary>
    /// Converts a spreadsheet cell to text using cached shared strings.
    /// </summary>
    /// <param name="cell">The spreadsheet cell.</param>
    /// <param name="sharedStrings">The cached shared strings.</param>
    /// <returns>The extracted cell text.</returns>
    private static string GetCachedCellValue(Cell cell, string[] sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText ?? string.Empty;
        }

        if (cell.CellValue == null)
        {
            return string.Empty;
        }

        var value = cell.CellValue.InnerText;

        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(value, out var index) &&
            sharedStrings != null &&
            (uint)index < (uint)sharedStrings.Length)
        {
            return sharedStrings[index];
        }

        if (cell.DataType?.Value == CellValues.Boolean)
        {
            return value == "1" ? "TRUE" : "FALSE";
        }

        return value;
    }
}
