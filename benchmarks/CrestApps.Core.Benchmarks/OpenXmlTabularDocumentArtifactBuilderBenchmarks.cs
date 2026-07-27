using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.Tabular;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares default and unconditional 4,096-row capacity for Open XML tabular artifact construction.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class OpenXmlTabularDocumentArtifactBuilderCapacityBenchmarks
{
    private const int PreallocatedRowCapacity = 4096;

    private byte[] _workbook;

    /// <summary>
    /// Gets or sets the number of data rows in the prebuilt workbook.
    /// </summary>
    [Params(0, 1, 32, 1_000, 4_096, 10_000)]
    public int DataRowCount { get; set; }

    /// <summary>
    /// Builds the workbook outside measured code and verifies exact candidate equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _workbook = CreateWorkbook(DataRowCount);

        var defaultCapacity = BuildArtifact(initialRowCapacity: 0);
        var preallocatedCapacity = BuildArtifact(PreallocatedRowCapacity);
        AssertEquivalent(defaultCapacity, preallocatedCapacity);
    }

    /// <summary>
    /// Builds an artifact while allowing the data-row list to grow from its default capacity.
    /// </summary>
    /// <returns>The parsed tabular artifact.</returns>
    [Benchmark(Baseline = true)]
    public TabularDocumentArtifact BuildWithDefaultCapacity()
    {
        return BuildArtifact(initialRowCapacity: 0);
    }

    /// <summary>
    /// Builds an artifact after unconditionally allocating room for 4,096 data rows.
    /// </summary>
    /// <returns>The parsed tabular artifact.</returns>
    [Benchmark]
    public TabularDocumentArtifact BuildWith4096RowCapacity()
    {
        return BuildArtifact(PreallocatedRowCapacity);
    }

    /// <summary>
    /// Builds an artifact from the prebuilt workbook with the requested initial row capacity.
    /// </summary>
    /// <param name="initialRowCapacity">The initial data-row list capacity.</param>
    /// <returns>The parsed tabular artifact.</returns>
    private TabularDocumentArtifact BuildArtifact(int initialRowCapacity)
    {
        using var stream = new MemoryStream(_workbook, writable: false);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart;

        if (workbookPart == null)
        {
            return new TabularDocumentArtifact();
        }

        List<string> header = null;
        var rows = initialRowCapacity == 0
            ? []
            : new List<List<string>>(initialRowCapacity);

        OpenXmlTabularWorksheetReader.ReadNonEmptyRows(
            workbookPart,
            "benchmark.xlsx",
            NullLogger.Instance,
            (row, firstNonEmptyRowInWorksheet) =>
            {
                if (header == null)
                {
                    header = row;

                    return;
                }

                if (!firstNonEmptyRowInWorksheet)
                {
                    rows.Add(row);
                }
            },
            CancellationToken.None);

        return header == null
            ? new TabularDocumentArtifact()
            : new TabularDocumentArtifact
            {
                Header = header,
                Rows = rows,
            };
    }

    /// <summary>
    /// Creates a workbook containing one header and the requested number of sequential data rows.
    /// </summary>
    /// <param name="dataRowCount">The number of data rows.</param>
    /// <returns>The serialized workbook.</returns>
    private static byte[] CreateWorkbook(int dataRowCount)
    {
        using var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            sheetData.AppendChild(new Row(
                new Cell
                {
                    CellReference = "A1",
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text("Index")),
                })
            {
                RowIndex = 1,
            });

            for (var index = 0; index < dataRowCount; index++)
            {
                var rowIndex = index + 2;
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

        return stream.ToArray();
    }

    /// <summary>
    /// Verifies exact header, row, and cell ordering equivalence between two artifacts.
    /// </summary>
    /// <param name="expected">The expected artifact.</param>
    /// <param name="actual">The actual artifact.</param>
    private static void AssertEquivalent(
        TabularDocumentArtifact expected,
        TabularDocumentArtifact actual)
    {
        if (!expected.Header.SequenceEqual(actual.Header, StringComparer.Ordinal) ||
            expected.Rows.Count != actual.Rows.Count)
        {
            throw new InvalidOperationException("Open XML tabular benchmark artifacts differ.");
        }

        for (var rowIndex = 0; rowIndex < expected.Rows.Count; rowIndex++)
        {
            if (!expected.Rows[rowIndex].SequenceEqual(actual.Rows[rowIndex], StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Open XML tabular benchmark artifacts differ at row {rowIndex}.");
            }
        }
    }
}
