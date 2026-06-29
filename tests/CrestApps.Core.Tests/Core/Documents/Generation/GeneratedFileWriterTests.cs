using System.Text;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.Pdf.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.Tests.Core.Documents.Generation;

public class GeneratedFileWriterTests
{
    [Theory]
    [InlineData("pdf", ".pdf")]
    [InlineData(".PDF", ".pdf")]
    [InlineData("  Csv ", ".csv")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_ReturnsLowerCaseDottedExtension(string input, string expected)
    {
        Assert.Equal(expected, GeneratedFileWriterOptions.Normalize(input));
    }

    [Fact]
    public async Task DelimitedWriter_WritesTableAsCsv()
    {
        var content = new GeneratedFileContent
        {
            Header = ["region", "note"],
            Rows = [["North", "Hello, world"], ["South", "plain"]],
        };

        var text = (await WriteAsync(new DelimitedGeneratedFileWriter(), content))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal("region,note\nNorth,\"Hello, world\"\nSouth,plain\n", text);
    }

    [Fact]
    public async Task DelimitedWriter_WritesTextVerbatimWhenNoTable()
    {
        var content = new GeneratedFileContent
        {
            Text = "a,b,c",
        };

        var text = await WriteAsync(new DelimitedGeneratedFileWriter(), content);

        Assert.Equal("a,b,c", text);
    }

    [Fact]
    public async Task PlainTextWriter_WritesBodyText()
    {
        var content = new GeneratedFileContent
        {
            Text = "# Heading\n\nSome body text.",
        };

        var text = await WriteAsync(new PlainTextGeneratedFileWriter(), content);

        Assert.Equal("# Heading\n\nSome body text.", text);
    }

    [Fact]
    public async Task PlainTextWriter_RendersMarkdownTableWhenOnlyTabularDataSupplied()
    {
        var content = new GeneratedFileContent
        {
            Header = ["region", "amount"],
            Rows = [["North", "100"]],
        };

        var text = (await WriteAsync(new PlainTextGeneratedFileWriter(), content))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("| region | amount |", text);
        Assert.Contains("| --- | --- |", text);
        Assert.Contains("| North | 100 |", text);
    }

    [Fact]
    public async Task SpreadsheetWriter_ProducesReadableWorkbook()
    {
        var content = new GeneratedFileContent
        {
            Header = ["region", "amount"],
            Rows = [["North", "100"], ["South", "200"]],
        };

        await using var stream = new MemoryStream();
        await new SpreadsheetGeneratedFileWriter().WriteAsync(content, stream, TestContext.Current.CancellationToken);

        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, isEditable: false);
        var sheetData = document.WorkbookPart!.WorksheetParts.Single().Worksheet.GetFirstChild<SheetData>()!;
        var rows = sheetData.Elements<Row>().ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal("region", CellText(rows[0].Elements<Cell>().First()));
        Assert.Equal("200", CellText(rows[2].Elements<Cell>().Last()));
    }

    [Fact]
    public async Task WordWriter_ProducesDocumentContainingContent()
    {
        var content = new GeneratedFileContent
        {
            Title = "Report",
            Text = "First line.",
            Header = ["name"],
            Rows = [["value"]],
        };

        await using var stream = new MemoryStream();
        await new WordGeneratedFileWriter().WriteAsync(content, stream, TestContext.Current.CancellationToken);

        stream.Position = 0;
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        var body = document.MainDocumentPart!.Document.Body!.InnerText;

        Assert.Contains("Report", body);
        Assert.Contains("First line.", body);
        Assert.Contains("value", body);
    }

    [Fact]
    public async Task PdfWriter_ProducesPdfSignature()
    {
        var content = new GeneratedFileContent
        {
            Title = "Report",
            Text = "Hello PDF.",
        };

        await using var stream = new MemoryStream();
        await new PdfGeneratedFileWriter().WriteAsync(content, stream, TestContext.Current.CancellationToken);

        var bytes = stream.ToArray();

        Assert.True(bytes.Length > 0);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    private static async Task<string> WriteAsync(IGeneratedFileWriter writer, GeneratedFileContent content)
    {
        await using var stream = new MemoryStream();
        await writer.WriteAsync(content, stream, TestContext.Current.CancellationToken);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string CellText(Cell cell)
    {
        return cell.InlineString?.Text?.Text ?? cell.CellValue?.Text ?? string.Empty;
    }
}
