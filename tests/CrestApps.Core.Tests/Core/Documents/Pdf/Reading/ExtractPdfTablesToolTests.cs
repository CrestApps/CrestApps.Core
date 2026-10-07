using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class ExtractPdfTablesToolTests
{
    [Fact]
    public async Task ExtractTables_ReturnsEachTableAsMarkdown()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfTablesTool());

        Assert.Contains("Found 1 table(s)", result, StringComparison.Ordinal);
        Assert.Contains("Table 1 — page 1, 4 rows × 3 columns", result, StringComparison.Ordinal);
        Assert.Contains("| Region | Revenue | Share |", result, StringComparison.Ordinal);
        Assert.Contains("| North | 74612 | 25% |", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractTables_TableAcrossPages_IsJoinedWithoutTheRepeatedHeader()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("long.pdf", await PdfReadingFixtures.LongTableAsync(70));

        var merged = await host.InvokeAsync(new ExtractPdfTablesTool(), new { format = "json" });
        var split = await host.InvokeAsync(new ExtractPdfTablesTool(), new { merge_across_pages = false });

        Assert.Contains("Found 1 table(s)", merged, StringComparison.Ordinal);
        Assert.Contains("pages 1-2, 71 rows × 3 columns", merged, StringComparison.Ordinal);
        Assert.Contains("\"header\":[\"Item\",\"Quantity\",\"Parity\"]", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"Item\",\"Quantity\",\"Parity\"],", merged[(merged.IndexOf("\"data\"", StringComparison.Ordinal))..], StringComparison.Ordinal);
        Assert.Contains("Found 2 table(s)", split, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractTables_LongerThanAnAnswer_ShowsTheRowsThatFit()
    {
        using var host = new PdfToolTestHost(configure: services => services.Configure<PdfAgentOptions>(options => options.MaxToolResponseCharacters = 2_000));
        await host.UploadAsync("long.pdf", await PdfReadingFixtures.LongTableAsync(70));

        var result = await host.InvokeAsync(new ExtractPdfTablesTool(), new { table = 1 });

        Assert.Contains("| Item 1 | 10 | Odd |", result, StringComparison.Ordinal);
        Assert.Contains("more rows not shown", result, StringComparison.Ordinal);
        Assert.Contains("Not every row of table(s) 1 is shown", result, StringComparison.Ordinal);
        Assert.True(result.Length <= 2_000, "The answer stays within the configured size.");
    }

    [Fact]
    public async Task ExtractTables_ExportXlsx_WritesOneSheetPerTable()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfTablesTool(), new { export = "xlsx" });

        Assert.Contains("[doc:1]", result, StringComparison.Ordinal);

        var (document, bytes) = await host.ReadMarkerAsync("[doc:1]");

        Assert.Equal("report-tables.xlsx", document.FileName);

        using var spreadsheet = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheets = spreadsheet.WorkbookPart.Workbook.Sheets.Elements<Sheet>().ToList();

        Assert.Single(sheets);
        Assert.StartsWith("Table 1", sheets[0].Name.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractTables_ExportCsv_WritesTheCells()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        await host.InvokeAsync(new ExtractPdfTablesTool(), new { export = "csv", table = 1 });

        var (_, bytes) = await host.ReadMarkerAsync("[doc:1]");
        var csv = Encoding.UTF8.GetString(bytes);

        Assert.StartsWith("Region,Revenue,Share", csv, StringComparison.Ordinal);
        Assert.Contains("North,74612,25%", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractTables_ProtectedPdf_NeedsAndUsesThePassword()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("secret.pdf", PdfReadingFixtures.Protect(await PdfReadingFixtures.ReportAsync(), "open-me", "owner-secret"));

        var refused = await host.InvokeAsync(new ExtractPdfTablesTool());
        var opened = await host.InvokeAsync(new ExtractPdfTablesTool(), new { password = "open-me" });

        Assert.Contains("protected with a password", refused, StringComparison.Ordinal);
        Assert.Contains("| Region | Revenue | Share |", opened, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractTables_TableNumberPastTheEnd_IsExplained()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfTablesTool(), new { table = 5 });

        Assert.Contains("There is no table 5; 1 table(s) were found", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractTables_NoTables_SaysSo()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("plain.pdf", await PdfReadingFixtures.ParagraphsAsync("Nothing but a paragraph of running text on this page."));

        var result = await host.InvokeAsync(new ExtractPdfTablesTool());

        Assert.Contains("No tables were detected", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractTables_Json_IsValidJson()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfTablesTool(), new { format = "json" });
        var json = result.Split('\n').Single(line => line.StartsWith('{'));

        using var parsed = JsonDocument.Parse(json);

        Assert.Equal(4, parsed.RootElement.GetProperty("rows").GetInt32());
        Assert.Equal("North", parsed.RootElement.GetProperty("data")[0][0].GetString());
    }
}
