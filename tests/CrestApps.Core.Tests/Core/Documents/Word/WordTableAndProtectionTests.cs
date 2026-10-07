using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Tools;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Documents.Word;

public sealed class WordTableAndProtectionTests
{
    [Fact]
    public async Task MergeCells_HorizontalAndVerticalRanges_MergesThemAndSplitsBack()
    {
        using var host = new WordToolTestHost();
        await CreateTableAsync(host);

        var answer = await host.InvokeAsync(new UpdateWordTableTool(), new
        {
            document = "doc",
            merge_cells = new object[]
            {
                new { row = "One", column = 2, to_column = 3 },
                new { row = "Two", column = 3, to_row = "Three" },
            },
        });

        Assert.Contains("merged 2 range(s)", answer, StringComparison.Ordinal);

        var merged = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(merged);

        using (var document = Open(merged))
        {
            var rows = Table(document).Elements<TableRow>().ToList();
            var one = rows[1].Elements<TableCell>().ToList();

            Assert.Equal(2, one.Count);
            Assert.Equal(2, one[1].TableCellProperties.GridSpan.Val.Value);
            Assert.Equal(["a", "b"], one[1].Elements<Paragraph>().Select(paragraph => WordText.Of(paragraph)).ToList());

            var restart = rows[2].Elements<TableCell>().Last();
            var continuation = rows[3].Elements<TableCell>().Last();

            Assert.Equal(MergedCellValues.Restart, restart.TableCellProperties.VerticalMerge.Val.Value);
            Assert.NotNull(continuation.TableCellProperties.VerticalMerge);
            Assert.Null(continuation.TableCellProperties.VerticalMerge.Val);
            Assert.Equal(["d", "f"], restart.Elements<Paragraph>().Select(paragraph => WordText.Of(paragraph)).ToList());
            Assert.Equal(string.Empty, WordText.OfCell(continuation));
        }

        // Each merged cell is named by a row and column it covers; the vertical one by its lower row.
        await host.InvokeAsync(new UpdateWordTableTool(), new
        {
            document = "doc",
            split_cells = new object[]
            {
                new { row = "One", column = 3 },
                new { row = 4, column = 3 },
            },
        });

        var split = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(split);

        using (var document = Open(split))
        {
            var table = Table(document);

            foreach (var row in table.Elements<TableRow>())
            {
                Assert.Equal(3, row.Elements<TableCell>().Count());
                Assert.All(row.Elements<TableCell>(), cell => Assert.Null(cell.TableCellProperties?.GridSpan));
                Assert.All(row.Elements<TableCell>(), cell => Assert.Null(cell.TableCellProperties?.VerticalMerge));
            }

            Assert.Equal(["One", "ab", string.Empty], Texts(table)[1]);
            Assert.Equal(["Three", "e", string.Empty], Texts(table)[3]);
        }
    }

    [Fact]
    public async Task SplitCells_UnmergedCell_AddsGridColumnsAndWidensTheOtherRows()
    {
        using var host = new WordToolTestHost();
        await CreateTableAsync(host);

        await host.InvokeAsync(new UpdateWordTableTool(), new { document = "doc", split_cells = new[] { new { row = "Two", column = 2, columns = 3 } } });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var document = Open(bytes);
        var table = Table(document);

        Assert.Equal(5, table.GetFirstChild<TableGrid>().Elements<GridColumn>().Count());
        Assert.All(table.Elements<TableRow>(), row => Assert.Equal(5, row.Elements<TableCell>().Sum(cell => cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1)));
        Assert.Equal(["Two", "c", string.Empty, string.Empty, "d"], Texts(table)[2]);
    }

    [Fact]
    public async Task Cells_RowWithMergedCells_SetsTheCellUnderTheColumn()
    {
        using var host = new WordToolTestHost();
        await CreateTableAsync(host);

        await host.InvokeAsync(new UpdateWordTableTool(), new
        {
            document = "doc",
            merge_cells = new object[]
            {
                new { row = 2, column = 1, to_column = 2 },
                new { row = 3, column = 3, to_row = 4 },
            },
        });

        // Column 3 of row 2 is its second cell; column 3 of row 4 continues the merged cell that starts in row 3.
        var answer = await host.InvokeAsync(new UpdateWordTableTool(), new
        {
            document = "doc",
            cells = new object[]
            {
                new { row = 2, column = 3, text = "B3" },
                new { row = 4, column = 3, text = "Merged" },
            },
        });

        Assert.Contains("set 2 cell(s)", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var document = Open(bytes);
        var rows = Texts(Table(document));

        Assert.Equal(["Onea", "B3"], rows[1]);
        Assert.Equal(["Two", "c", "Merged"], rows[2]);
        Assert.Equal(["Three", "e", string.Empty], rows[3]);
    }

    [Fact]
    public async Task AddAndRemoveColumn_TableWithGridSpan_KeepsEveryRowOnTheGrid()
    {
        using var host = new WordToolTestHost();
        await CreateTableAsync(host);

        await host.InvokeAsync(new UpdateWordTableTool(), new { document = "doc", merge_cells = new[] { new { row = 2, column = 1, to_column = 2 } } });

        var added = await host.InvokeAsync(new UpdateWordTableTool(), new
        {
            document = "doc",
            add_column = new { header = "New", values = new[] { "x", "y", "z" }, after_column = 1 },
        });

        Assert.Contains("× 4 columns", added, StringComparison.Ordinal);

        var widened = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(widened);

        using (var document = Open(widened))
        {
            var table = Table(document);

            AssertOnGrid(table, 4);
            Assert.Equal(["A", "New", "B", "C"], Texts(table)[0]);

            // The new column falls inside the merged cell of row 2, which widens instead of getting a cell.
            Assert.Equal(3, table.Elements<TableRow>().ElementAt(1).Elements<TableCell>().First().TableCellProperties.GridSpan.Val.Value);
            Assert.Equal(["Two", "y", "c", "d"], Texts(table)[2]);
        }

        await host.InvokeAsync(new UpdateWordTableTool(), new { document = "doc", remove_columns = new[] { 2 } });

        var narrowed = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(narrowed);

        using (var document = Open(narrowed))
        {
            var table = Table(document);

            AssertOnGrid(table, 3);
            Assert.Equal(["A", "B", "C"], Texts(table)[0]);
            Assert.Equal(2, table.Elements<TableRow>().ElementAt(1).Elements<TableCell>().First().TableCellProperties.GridSpan.Val.Value);
            Assert.Equal(["Three", "e", "f"], Texts(table)[3]);
        }
    }

    [Fact]
    public async Task FormatCells_RangeWithStyleAndWidths_AppliesTheLook()
    {
        using var host = new WordToolTestHost();
        await CreateTableAsync(host);

        await host.InvokeAsync(new UpdateWordTableTool(), new
        {
            document = "doc",
            format_cells = new[] { new { row = 1, column = 1, to_column = 3, fill = "#1F4E79", alignment = "center", vertical_alignment = "center" } },
            column_widths = new object[] { 2, 1, 1 },
            table_style = "light",
            banded = false,
        });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var document = Open(bytes);
        var table = Table(document);
        var header = table.Elements<TableRow>().First().Elements<TableCell>().ToList();
        var widths = table.GetFirstChild<TableGrid>().Elements<GridColumn>().Select(column => int.Parse(column.Width.Value)).ToList();

        Assert.All(header, cell => Assert.Equal("1F4E79", cell.TableCellProperties.Shading.Fill.Value));
        Assert.All(header, cell => Assert.Equal(JustificationValues.Center, cell.Elements<Paragraph>().First().ParagraphProperties.Justification.Val.Value));
        Assert.InRange(widths[0], widths[1] * 2 - 4, widths[1] * 2 + 4);
        Assert.Equal(BorderValues.Nil, table.GetFirstChild<TableProperties>().TableBorders.InsideVerticalBorder.Val.Value);
        Assert.Equal(0x0200, Convert.ToInt32(table.GetFirstChild<TableProperties>().TableLook.Val.Value, 16) & 0x0200);
    }

    private static async Task CreateTableAsync(WordToolTestHost host)
    {
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "paragraph", text = "Before the table." },
                new
                {
                    type = "table",
                    columns = new[] { "A", "B", "C" },
                    rows = new object[]
                    {
                        new object[] { "One", "a", "b" },
                        new object[] { "Two", "c", "d" },
                        new object[] { "Three", "e", "f" },
                    },
                },
            },
        });
    }

    private static WordprocessingDocument Open(byte[] bytes)
    {
        return WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
    }

    private static Table Table(WordprocessingDocument document)
    {
        return document.MainDocumentPart.Document.Body.Descendants<Table>().Single();
    }

    private static List<List<string>> Texts(Table table)
    {
        return [.. table.Elements<TableRow>().Select(row => row.Elements<TableCell>().Select(cell => WordText.OfCell(cell).Replace("\n", string.Empty, StringComparison.Ordinal)).ToList())];
    }

    private static void AssertOnGrid(Table table, int columns)
    {
        Assert.Equal(columns, table.GetFirstChild<TableGrid>().Elements<GridColumn>().Count());

        foreach (var row in table.Elements<TableRow>())
        {
            Assert.Equal(columns, row.Elements<TableCell>().Sum(cell => cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1));
        }
    }
}
