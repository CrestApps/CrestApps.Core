using System.Text.Json;
using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class ExtractPdfStructureToolTests
{
    [Fact]
    public async Task ExtractStructure_Outline_ListsElementsInReadingOrder()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfStructureTool());

        string[] expected =
        [
            "p1  heading 1: Quarterly Report",
            "p1    paragraph: Revenue rose by twelve percent",
            "p1    heading 2: Regional results",
            "p1      list item: North grew faster than expected",
            "p1      list item: South held flat",
            "p1      table 4 rows × 3 columns, header: Region | Revenue | Share",
            "p2  heading 1: Outlook",
            "p2    figure (image 64×48 px): Figure 1: Company logo",
            "p3  heading 1: Appendix",
        ];

        var position = 0;

        foreach (var line in expected)
        {
            var found = result.IndexOf(line, position, StringComparison.Ordinal);

            Assert.True(found >= 0, $"Expected \"{line}\" after position {position} in:\n{result}");

            position = found + line.Length;
        }

        // The table's cells are the table, not paragraphs of their own.
        Assert.DoesNotContain("paragraph: Region", result, StringComparison.Ordinal);
        Assert.Contains("4 heading(s)", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractStructure_RunningHeadsAndPageNumbers_AreLeftOut()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("columns.pdf", PdfReadingFixtures.TwoColumnsWithRunningText());

        var result = await host.InvokeAsync(new ExtractPdfStructureTool(), new { max_paragraph_characters = 60 });

        Assert.Contains("p1  heading 1: Market overview", result, StringComparison.Ordinal);
        Assert.Contains("p3  heading 1: Outlook and risks", result, StringComparison.Ordinal);
        Assert.Contains("running head/foot", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Northwind Annual Review", result, StringComparison.Ordinal);
        Assert.DoesNotContain("paragraph: Page 2", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractStructure_Markdown_WritesHeadingsListsAndTables()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfStructureTool(), new { format = "markdown" });

        Assert.Contains("# Quarterly Report", result, StringComparison.Ordinal);
        Assert.Contains("## Regional results", result, StringComparison.Ordinal);
        Assert.Contains("- North grew faster than expected\n- South held flat", result, StringComparison.Ordinal);
        Assert.Contains("| Region | Revenue | Share |", result, StringComparison.Ordinal);
        Assert.Contains("<!-- page 2 -->", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractStructure_Json_DescribesEveryElement()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfStructureTool(), new { format = "json", pages = "1" });
        var json = result.Split('\n').Single(line => line.StartsWith('['));

        using var parsed = JsonDocument.Parse(json);
        var elements = parsed.RootElement.EnumerateArray().ToList();

        Assert.Equal("heading", elements[0].GetProperty("type").GetString());
        Assert.Equal(1, elements[0].GetProperty("level").GetInt32());
        Assert.Contains(elements, element => element.GetProperty("type").GetString() == "table" && element.GetProperty("columns").GetInt32() == 3);
        Assert.Contains(elements, element => element.GetProperty("type").GetString() == "list_item" && element.GetProperty("marker").GetString() == "•");
    }
}
