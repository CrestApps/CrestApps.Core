using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Wordprocessing = DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class ConvertFromPdfToolTests
{
    [Fact]
    public async Task Convert_ToWord_KeepsHeadingsParagraphsListsAndTables()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ConvertFromPdfTool(), new { format = "docx" });

        Assert.Contains("to Word as \"report.docx\"", result, StringComparison.Ordinal);
        Assert.Contains("[doc:1]", result, StringComparison.Ordinal);

        var (document, bytes) = await host.ReadMarkerAsync("[doc:1]");

        Assert.Equal("report.docx", document.FileName);

        using var word = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var body = word.MainDocumentPart.Document.Body;
        var text = body.InnerText;

        Assert.Contains("Quarterly Report", text, StringComparison.Ordinal);
        Assert.Contains("Revenue rose by twelve percent", text, StringComparison.Ordinal);
        Assert.Contains("North grew faster than expected", text, StringComparison.Ordinal);
        Assert.Single(body.Elements<Wordprocessing.Table>());
    }

    [Fact]
    public async Task Convert_ToMarkdown_WritesStructureWithPageMarkers()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        await host.InvokeAsync(new ConvertFromPdfTool(), new { format = "markdown" });

        var markdown = Encoding.UTF8.GetString((await host.ReadMarkerAsync("[doc:1]")).Bytes);

        Assert.StartsWith("<!-- page 1 -->\n\n# Quarterly Report", markdown, StringComparison.Ordinal);
        Assert.Contains("## Regional results", markdown, StringComparison.Ordinal);
        Assert.Contains("| North | 74612 | 25% |", markdown, StringComparison.Ordinal);
        Assert.Contains("<!-- page 3 -->\n\n# Appendix", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_ToHtml_EncodesTextAndRunsNoScript()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("page.pdf", PdfReadingFixtures.TextAndBlankPage("<script>alert('x')</script> & more"));
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        await host.InvokeAsync(new ConvertFromPdfTool(), new { pdf = "page.pdf", format = "html" });
        await host.InvokeAsync(new ConvertFromPdfTool(), new { pdf = "report.pdf", format = "html" });

        var hostile = Encoding.UTF8.GetString((await host.ReadMarkerAsync("[doc:1]")).Bytes);
        var report = Encoding.UTF8.GetString((await host.ReadMarkerAsync("[doc:2]")).Bytes);

        Assert.Contains("&lt;script&gt;", hostile, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", hostile, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("<!DOCTYPE html>\n<html lang=\"en-US\">", report, StringComparison.Ordinal);
        Assert.Contains("<h1>Quarterly Report</h1>", report, StringComparison.Ordinal);
        Assert.Contains("<th>Region</th>", report, StringComparison.Ordinal);
        Assert.Contains("<img alt=\"Figure 1: Company logo\" src=\"data:image/png;base64,", report, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", report, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_ToExcel_WritesOneSheetPerTable()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("long.pdf", await PdfReadingFixtures.LongTableAsync(70));

        var result = await host.InvokeAsync(new ConvertFromPdfTool(), new { format = "xlsx", file_name = "inventory" });

        Assert.Contains("\"inventory.xlsx\": 1 table(s), one worksheet each", result, StringComparison.Ordinal);

        var (_, bytes) = await host.ReadMarkerAsync("[doc:1]");

        using var spreadsheet = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheet = Assert.Single(spreadsheet.WorkbookPart.Workbook.Sheets.Elements<Sheet>());
        var part = (WorksheetPart)spreadsheet.WorkbookPart.GetPartById(sheet.Id);

        // The header and the seventy rows from both pages, the repeated header dropped.
        Assert.Equal(71, part.Worksheet.Descendants<Row>().Count());
    }

    [Fact]
    public async Task Convert_ToExcel_WithoutTables_IsDeclined()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("plain.pdf", await PdfReadingFixtures.ParagraphsAsync("Only a paragraph of running text."));

        var result = await host.InvokeAsync(new ConvertFromPdfTool(), new { format = "xlsx" });

        Assert.Contains("No tables were detected", result, StringComparison.Ordinal);
        Assert.Empty(host.Invocation.ToolReferences);
    }

    [Fact]
    public async Task Convert_ToText_SeparatesPages()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ConvertFromPdfTool(), new { format = "txt", pages = "2-3", file_name = "Summary.docx" });
        var text = Encoding.UTF8.GetString((await host.ReadMarkerAsync("[doc:1]")).Bytes);

        Assert.Contains("\"Summary.txt\"", result, StringComparison.Ordinal);
        Assert.StartsWith("--- Page 2 ---", text, StringComparison.Ordinal);
        Assert.Contains("--- Page 3 ---", text, StringComparison.Ordinal);
        Assert.DoesNotContain("--- Page 1 ---", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_ToJson_DescribesTheStructure()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        await host.InvokeAsync(new ConvertFromPdfTool(), new { format = "json" });

        using var json = JsonDocument.Parse((await host.ReadMarkerAsync("[doc:1]")).Bytes);
        var root = json.RootElement;
        var elements = root.GetProperty("elements").EnumerateArray().ToList();

        Assert.Equal("Quarterly Report", root.GetProperty("title").GetString());
        Assert.Equal(3, root.GetProperty("page_count").GetInt32());
        Assert.Equal("heading", elements[0].GetProperty("type").GetString());

        var table = elements.Single(element => element.GetProperty("type").GetString() == "table");

        Assert.Equal("74612", table.GetProperty("data")[1][1].GetString());
    }

    [Fact]
    public async Task Convert_ToSvg_DrawsOnePageOrZipsSeveral()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        await host.InvokeAsync(new ConvertFromPdfTool(), new { format = "svg", pages = "1" });
        await host.InvokeAsync(new ConvertFromPdfTool(), new { format = "svg" });

        var (single, singleBytes) = await host.ReadMarkerAsync("[doc:1]");
        var (zip, zipBytes) = await host.ReadMarkerAsync("[doc:2]");

        Assert.Equal("report.svg", single.FileName);
        Assert.Equal("image/svg+xml", single.ContentType);
        Assert.StartsWith("<svg", Encoding.UTF8.GetString(singleBytes), StringComparison.Ordinal);
        Assert.Equal("report.zip", zip.FileName);

        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);

        Assert.Equal(["report-page-1.svg", "report-page-2.svg", "report-page-3.svg"], archive.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task Convert_UnknownFormat_IsDeclined()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ConvertFromPdfTool(), new { format = "pptx" });

        Assert.Contains("cannot be converted to \"pptx\"", result, StringComparison.Ordinal);
    }
}
