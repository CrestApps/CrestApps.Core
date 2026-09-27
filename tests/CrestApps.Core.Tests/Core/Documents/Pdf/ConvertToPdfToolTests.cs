using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class ConvertToPdfToolTests
{
    [Fact]
    public async Task ConvertWord_KeepsHeadingsFormattingListsTablesAndPictures()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("review.docx", PdfConversionFixtures.WordDocument(), "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var result = await host.InvokeAsync(new ConvertToPdfTool(), new { });

        Assert.Contains("Converted \"review.docx\"", result, StringComparison.Ordinal);
        Assert.Contains("into working PDF \"review\"", result, StringComparison.Ordinal);
        Assert.Contains("The uploaded files were not changed", result, StringComparison.Ordinal);

        var blocks = await BlocksAsync(host, "review");

        var heading = blocks.First(block => block.Type == PdfBlockTypes.Heading);

        Assert.Equal("Quarterly review", heading.Text);
        Assert.Equal(1, heading.Level);

        var paragraph = Assert.Single(blocks, block => block.Type == PdfBlockTypes.Paragraph);

        Assert.Contains("**12%**", paragraph.Text, StringComparison.Ordinal);
        Assert.Contains("[the report](https://example.com/report)", paragraph.Text, StringComparison.Ordinal);
        Assert.Contains("a\\*b", paragraph.Text, StringComparison.Ordinal);

        var lists = blocks.Where(block => block.Type == PdfBlockTypes.List).ToList();

        Assert.Equal(2, lists.Count);
        Assert.Equal(["North", "South"], lists[0].Items);
        Assert.NotEqual(true, lists[0].Ordered);
        Assert.Equal(["Plan", "Execute"], lists[1].Items);
        Assert.True(lists[1].Ordered);

        var table = Assert.Single(blocks, block => block.Type == PdfBlockTypes.Table).Table;

        Assert.Equal(["Region", "Revenue"], table.Columns.Select(column => column.Header));
        Assert.Equal(2, table.Rows.Count);

        Assert.Contains(blocks, block => block.Type == PdfBlockTypes.PageBreak);
        Assert.Contains(blocks, block => block.Type == PdfBlockTypes.Heading && block.Text == "Appendix" && block.Level == 2);

        var image = Assert.Single(blocks, block => block.Type == PdfBlockTypes.Image).Image;

        Assert.StartsWith("asset:img", image.Source, StringComparison.Ordinal);
        Assert.Equal(72, image.Width);
        Assert.Equal("Contoso logo", image.AltText);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("review"));
        var text = string.Join(' ', pdf.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));

        Assert.True(pdf.NumberOfPages >= 2);
        Assert.Contains("Quarterly", text, StringComparison.Ordinal);
        Assert.Contains("1,200", text, StringComparison.Ordinal);
        Assert.Contains(pdf.GetPages(), page => page.GetImages().Any());
    }

    [Fact]
    public async Task ConvertPowerPoint_GivesEachVisibleSlideAPageInLandscape()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("roadmap.pptx", PdfConversionFixtures.Presentation(), "application/vnd.openxmlformats-officedocument.presentationml.presentation");

        var result = await host.InvokeAsync(new ConvertToPdfTool(), new { sources = new[] { "roadmap.pptx" }, include_notes = true });

        Assert.Contains("1 hidden slide(s) were left out", result, StringComparison.Ordinal);

        var workspace = await host.LoadWorkspaceAsync();
        var definition = Assert.Single(workspace.Documents).Definition;
        var blocks = definition.Sections[0].Blocks;

        Assert.Equal("landscape", definition.PageSetup?.Orientation);
        Assert.Equal("Roadmap", definition.Title);
        Assert.Equal(["Roadmap", "Slide 3"], blocks.Where(block => block.Type == PdfBlockTypes.Heading).Select(block => block.Text));
        Assert.DoesNotContain(blocks, block => block.Text == "Draft ideas");

        var list = Assert.Single(blocks, block => block.Type == PdfBlockTypes.List);

        Assert.Equal(["Launch in March", "Hire two engineers", "  One in support"], list.Items);
        Assert.Contains(blocks, block => block.Type == PdfBlockTypes.Table);
        Assert.Contains(blocks, block => block.Type == PdfBlockTypes.Image && block.Image.AltText == "Team photo");
        Assert.Contains(blocks, block => block.Type == PdfBlockTypes.Quote && block.Text.Contains("Mention the budget", StringComparison.Ordinal));
        Assert.Contains(blocks, block => block.Type == PdfBlockTypes.Paragraph && block.Text == "Questions welcome");

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("roadmap"));
        var first = pdf.GetPage(1);

        Assert.Equal(2, pdf.NumberOfPages);
        Assert.True(first.Width > first.Height);
    }

    [Fact]
    public async Task ConvertSeveralFiles_CombinesMarkdownCsvAndAnImageIntoOnePdf()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("notes.md", Encoding.UTF8.GetBytes("# Launch plan\n\nShip the **beta** first.\n\n- Recruit testers\n- Collect feedback\n"), "text/markdown");
        await host.UploadAsync("budget.csv", Encoding.UTF8.GetBytes("Item,Cost\nLaptops,4200\nTravel,1300\n"), "text/csv");
        await host.UploadAsync("chart.png", PdfTestImages.SolidPng(200, 120, 0x0B, 0x53, 0x94), "image/png");

        var result = await host.InvokeAsync(new ConvertToPdfTool(), new
        {
            sources = new[] { "notes.md", "budget.csv", "chart.png" },
            name = "launch-pack",
            page_numbers = new { enabled = true },
        });

        Assert.Contains("into working PDF \"launch-pack\"", result, StringComparison.Ordinal);
        Assert.Contains("read without their number formats", result, StringComparison.Ordinal);

        var blocks = await BlocksAsync(host, "launch-pack");

        Assert.Contains(blocks, block => block.Type == PdfBlockTypes.Heading && block.Text == "Launch plan");
        Assert.Contains(blocks, block => block.Type == PdfBlockTypes.Paragraph && block.Text == "Ship the **beta** first.");
        Assert.Contains(blocks, block => block.Type == PdfBlockTypes.List && block.Items.SequenceEqual(new[] { "Recruit testers", "Collect feedback" }));
        Assert.Contains(blocks, block => block.Type == PdfBlockTypes.Table && block.Table.Rows.Count == 2);
        Assert.Contains(blocks, block => block.Type == PdfBlockTypes.Image);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("launch-pack"));
        var text = string.Join(' ', pdf.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));

        Assert.Contains("Laptops", text, StringComparison.Ordinal);
        Assert.Contains(pdf.GetPages(), page => page.GetImages().Any());
    }

    [Fact]
    public async Task ConvertToPdf_RefusesPdfsAndOldBinaryFormats()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("already.pdf", await PdfToolFixtures.SimplePdfAsync("Text"));
        await host.UploadAsync("legacy.doc", [0xD0, 0xCF, 0x11, 0xE0], "application/msword");

        var pdf = await host.InvokeAsync(new ConvertToPdfTool(), new { sources = new[] { "already.pdf" } });
        var legacy = await host.InvokeAsync(new ConvertToPdfTool(), new { sources = new[] { "legacy.doc" } });

        Assert.Contains("is already a PDF", pdf, StringComparison.Ordinal);
        Assert.Contains("old binary .doc format", legacy, StringComparison.Ordinal);
    }

    private static async Task<List<PdfBlockDefinition>> BlocksAsync(PdfToolTestHost host, string name)
    {
        var workspace = await host.LoadWorkspaceAsync();

        return workspace.Find(name).Definition.Sections[0].Blocks;
    }
}
