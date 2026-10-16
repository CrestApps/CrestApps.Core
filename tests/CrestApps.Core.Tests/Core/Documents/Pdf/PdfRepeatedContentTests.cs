using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class PdfRepeatedContentTests
{
    private const string Title = "2026 Project Tracker Issue Report";

    // A chart marker as the chart tool writes it. Every value is invented.
    private const string Marker = """[chart:{"type":"bar","data":{"labels":["Scoring form","Call list"],"datasets":[{"label":"Issues","data":[194,49]}]}}]""";

    [Fact]
    public async Task AddPdfContent_RepeatsTheTitleHeadingCreatePdfAdded_KeepsItOnce()
    {
        using var host = new PdfToolTestHost();

        // What gpt-4.1 sent: the title heading with create_pdf, then the whole content again starting with it.
        await host.InvokeAsync(new CreatePdfTool(), new
        {
            name = "report",
            title = Title,
            blocks = new object[] { new { type = "heading", text = Title, level = 1 } },
        });

        var added = await host.InvokeAsync(new AddPdfContentTool(), new
        {
            blocks = new object[]
            {
                new { type = "heading", text = Title, level = 1 },
                new { type = "heading", text = "Cover Page & Introduction", level = 2 },
                new { type = "paragraph", text = "This presentation summarizes the top issues." },
            },
        });

        Assert.Contains("already the heading before it", added, StringComparison.Ordinal);
        Assert.Equal(1, await CountAsync(host, Title));
    }

    [Fact]
    public async Task CreatePdf_SameHeadingTwiceInARow_KeepsItOnce()
    {
        using var host = new PdfToolTestHost();

        await host.InvokeAsync(new CreatePdfTool(), new
        {
            name = "report",
            blocks = new object[]
            {
                new { type = "title", text = Title },
                new { type = "heading", text = "  2026 project tracker issue report ", level = 1 },
                new { type = "paragraph", text = "Body." },
            },
        });

        Assert.Equal(1, await CountAsync(host, Title));
    }

    [Fact]
    public async Task CreatePdf_SameHeadingWithContentBetween_KeepsBoth()
    {
        using var host = new PdfToolTestHost();

        // A heading used again for another part of the document is the author's choice, not a repeat.
        await host.InvokeAsync(new CreatePdfTool(), new
        {
            name = "report",
            blocks = new object[]
            {
                new { type = "heading", text = "Overview", level = 2 },
                new { type = "paragraph", text = "Frequency." },
                new { type = "heading", text = "Overview", level = 2 },
                new { type = "paragraph", text = "Hours." },
            },
        });

        Assert.Equal(2, await CountAsync(host, "Overview"));
    }

    [Fact]
    public async Task AddPdfContent_ChartMarkerPlaceholderAsText_IsRefusedAndNamesTheWayToAddTheChart()
    {
        using var host = new PdfToolTestHost();

        await host.InvokeAsync(new CreatePdfTool(), new { name = "report", title = Title });

        // What the agent wrote under "Chart: Top 20 Issue Groups by Frequency"; the reader saw these characters.
        var added = await host.InvokeAsync(new AddPdfContentTool(), new
        {
            blocks = new object[]
            {
                new { type = "heading", text = "Chart: Top 20 Issue Groups by Frequency", level = 2 },
                new { type = "paragraph", text = "[chart:Refer to chart marker above]" },
            },
        });

        Assert.Contains("Nothing was added", added, StringComparison.Ordinal);
        Assert.Contains("conversation_chart", added, StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync(host, "[chart:"));
    }

    [Fact]
    public async Task AddPdfContent_ChartMarkerAsParagraph_IsDrawnAsTheChart()
    {
        using var host = new PdfToolTestHost();

        await host.InvokeAsync(new CreatePdfTool(), new { name = "report", title = Title });

        var added = await host.InvokeAsync(new AddPdfContentTool(), new
        {
            blocks = new object[] { new { type = "paragraph", text = Marker } },
        });

        Assert.DoesNotContain("Nothing was added", added, StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync(host, "[chart:"));

        var definition = (await host.LoadWorkspaceAsync()).Documents.Single().Definition;
        var block = Assert.Single(definition.Sections.SelectMany(section => section.Blocks));

        Assert.Equal("chart", block.Type);
        Assert.Equal(["Scoring form", "Call list"], block.Chart.Labels);
    }

    [Fact]
    public async Task AddPdfContent_ChartMarkerInsideASentence_IsRefused()
    {
        using var host = new PdfToolTestHost();

        await host.InvokeAsync(new CreatePdfTool(), new { name = "report", title = Title });

        var added = await host.InvokeAsync(new AddPdfContentTool(), new
        {
            blocks = new object[] { new { type = "paragraph", text = $"The chart below shows it. {Marker}" } },
        });

        Assert.Contains("Nothing was added", added, StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync(host, "[chart:"));
    }

    private static async Task<int> CountAsync(PdfToolTestHost host, string text)
    {
        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("report"));
        var all = string.Join('\n', pdf.GetPages().Select(page => string.Join(' ', page.GetWords().Select(word => word.Text))));
        var count = 0;

        for (var index = all.IndexOf(text, StringComparison.OrdinalIgnoreCase); index >= 0; index = all.IndexOf(text, index + text.Length, StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }
}
