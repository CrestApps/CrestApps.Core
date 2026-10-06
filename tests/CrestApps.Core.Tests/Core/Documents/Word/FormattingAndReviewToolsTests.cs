using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Rendering;
using CrestApps.Core.AI.Documents.Word.Tools;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.Tests.Core.Documents.Word;

public sealed class FormattingAndReviewToolsTests
{
    [Fact]
    public async Task FormatWordContent_BoldsOnlyThePhrase_AndTracksTheChange()
    {
        using var host = new WordToolTestHost();
        var blocks = await CreateAsync(host);
        var body = blocks.First(block => block.Text == "Alpha body text.");

        await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "doc", action = "track" });
        await host.InvokeAsync(new FormatWordContentTool(), new { document = "doc", ids = new[] { body.Id }, text = "body", format = new { bold = true } });

        using var document = await OpenAsync(host);
        var paragraph = document.MainDocumentPart.Document.Body.Descendants<Paragraph>().First(item => WordText.Of(item) == "Alpha body text.");
        var bold = paragraph.Elements<Run>().Where(run => run.RunProperties?.Bold is not null).ToList();

        Assert.Equal("body", string.Concat(bold.Select(run => run.InnerText)));
        Assert.NotNull(Assert.Single(bold).RunProperties.RunPropertiesChange);
        WordAuthoringToolsTests.AssertValid(await host.ReadWorkingDocumentAsync("doc"));
    }

    [Fact]
    public async Task ManageWordStyles_CreatesAStyleThatFormatContentApplies()
    {
        using var host = new WordToolTestHost();
        var blocks = await CreateAsync(host);

        await host.InvokeAsync(new ManageWordStylesTool(), new { document = "doc", action = "create", name = "Call Out", format = new { italic = true, color = "#1F4E79" } });
        await host.InvokeAsync(new FormatWordContentTool(), new { document = "doc", ids = new[] { blocks.First(block => block.Text == "Beta body.").Id }, apply_style = "Call Out" });

        var list = await host.InvokeAsync(new ManageWordStylesTool(), new { document = "doc", action = "list" });
        var look = await host.InvokeAsync(new ManageWordStylesTool(), new { document = "doc", action = "get", name = "Call Out" });

        Assert.Contains("\"Call Out\"", list, StringComparison.Ordinal);
        Assert.Contains("used 1 time(s)", list, StringComparison.Ordinal);
        Assert.Contains("italic", look, StringComparison.Ordinal);
        Assert.Contains("#1F4E79", look, StringComparison.Ordinal);
        WordAuthoringToolsTests.AssertValid(await host.ReadWorkingDocumentAsync("doc"));
    }

    [Fact]
    public async Task HeaderFooterAndPageLayout_WritePageNumbersBordersAndRomanNumbering()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);

        await host.InvokeAsync(new AddWordHeaderFooterTool(), new { document = "doc", kind = "footer", center = "Page {page} of {pages}", hide_on_first_page = true });
        await host.InvokeAsync(new SetWordPageLayoutTool(), new { document = "doc", number_format = "lower_roman", start_at = 1, page_borders = new { style = "double", color = "#336699" } });
        await host.InvokeAsync(new SetWordPageBackgroundTool(), new { document = "doc", watermark = "DRAFT" });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var section = document.MainDocumentPart.Document.Body.Elements<SectionProperties>().Single();
        var footer = document.MainDocumentPart.FooterParts.Select(part => part.Footer).Single(item => item.InnerText.Contains("Page", StringComparison.Ordinal));

        Assert.NotNull(section.GetFirstChild<TitlePage>());
        Assert.Equal(NumberFormatValues.LowerRoman, section.GetFirstChild<PageNumberType>().Format.Value);
        Assert.Equal(BorderValues.Double, section.GetFirstChild<PageBorders>().TopBorder.Val.Value);
        Assert.Contains(footer.Descendants<FieldCode>(), code => code.Text.Contains("NUMPAGES", StringComparison.Ordinal));
        WordAuthoringToolsTests.AssertValid(bytes);

        // The page number is laid out at its real width, so " of " follows it without a gap.
        using var package = WordPackage.Open(bytes);
        var layout = WordPreview.Layout(package, host.Services);

        // The watermark is drawn on every page, the first-page one included, turned diagonally.
        Assert.All(layout.Pages, item => Assert.Contains(item.Items.OfType<WordTextItem>(), text => text.Text == "DRAFT" && text.Rotation != 0));

        var page = layout.Pages[1];
        var items = page.Items.OfType<WordTextItem>().Where(item => item.Baseline > page.Height - 100).OrderBy(item => item.X).ToList();
        var number = items.Single(item => item.Text == "ii");
        var next = items.First(item => item.X > number.X);

        Assert.Contains("Page ii of 2", WordPreview.PageText(page), StringComparison.Ordinal);
        Assert.InRange(next.X - (number.X + number.Width), 0, 4);
    }

    [Fact]
    public async Task UpdateWordTable_SetsCellsAndAddsAndRemovesRowsAndColumns()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);

        await host.InvokeAsync(new UpdateWordTableTool(), new
        {
            document = "doc",
            cells = new[] { new { row = "north", column = 2, text = "**20**" } },
            add_rows = new[] { new object[] { "East", 30 } },
            add_column = new { header = "Note", values = new[] { "a", "b", "c" } },
            remove_rows = new[] { "South" },
            repeat_header = true,
        });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var rows = document.MainDocumentPart.Document.Body.Descendants<Table>().Single().Elements<TableRow>().Select(row => row.Elements<TableCell>().Select(WordText.OfCell).ToList()).ToList();

        Assert.Equal(["Region", "Value", "Note"], rows[0]);
        Assert.Equal(["North", "20", "a"], rows[1]);
        Assert.Equal(["East", "30", string.Empty], rows[2]);
        Assert.Equal(3, rows.Count);
        WordAuthoringToolsTests.AssertValid(bytes);
    }

    [Fact]
    public async Task ManageWordComments_AddsRepliesResolvesAndDeletes()
    {
        using var host = new WordToolTestHost();
        var blocks = await CreateAsync(host);
        var body = blocks.First(block => block.Text == "Alpha body text.");

        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "add", id = body.Id, text = "body", comment = "Is this right?" });
        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "reply", comment_id = "0", comment = "Yes." });
        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "resolve", comment_id = "0" });

        var list = await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "list" });

        Assert.Contains("[resolved] on \"body\"", list, StringComparison.Ordinal);
        Assert.Contains("Is this right?", list, StringComparison.Ordinal);
        Assert.Contains("reply #1", list, StringComparison.Ordinal);
        WordAuthoringToolsTests.AssertValid(await host.ReadWorkingDocumentAsync("doc"));

        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "delete", comment_id = "0" });

        using var document = await OpenAsync(host);

        Assert.Empty(document.MainDocumentPart.Document.Descendants<CommentReference>());
        Assert.Empty(document.MainDocumentPart.WordprocessingCommentsPart.Comments.Elements<Comment>());
    }

    [Fact]
    public async Task ManageWordRevisions_RejectRestoresTheText_AndAcceptKeepsTheEdit()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);

        await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "doc", action = "track" });
        await host.InvokeAsync(new UpdateWordContentTool(), new { document = "doc", find = "Alpha body", replace = "Omega body" });

        var list = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "doc", action = "list" });

        Assert.Contains("insertion", list, StringComparison.Ordinal);
        Assert.Contains("deletion", list, StringComparison.Ordinal);

        await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "doc", action = "reject" });
        Assert.Contains("Alpha body text.", await TextAsync(host), StringComparison.Ordinal);

        await host.InvokeAsync(new UpdateWordContentTool(), new { document = "doc", find = "Beta body", replace = "Gamma body" });
        await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "doc", action = "accept" });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);

        Assert.Contains("Gamma body.", WordText.Of(document.MainDocumentPart.Document.Body), StringComparison.Ordinal);
        Assert.Empty(document.MainDocumentPart.Document.Descendants<InsertedRun>());
        Assert.Empty(document.MainDocumentPart.Document.Descendants<DeletedRun>());
        WordAuthoringToolsTests.AssertValid(bytes);
    }

    [Fact]
    public async Task ExtractSearchCompareAndCheck_ReadTheDocument()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);
        await host.UploadAsync("original.docx", await host.ReadWorkingDocumentAsync("doc"));
        await host.InvokeAsync(new UpdateWordContentTool(), new { document = "doc", find = "Beta body", replace = "Beta changed body" });

        var markdown = await host.InvokeAsync(new ExtractWordContentTool(), new { document = "doc" });
        var csv = await host.InvokeAsync(new ExtractWordContentTool(), new { document = "doc", what = "tables", save_as_file = true });
        var search = await host.InvokeAsync(new SearchWordDocumentTool(), new { document = "doc", query = "beta" });
        var compare = await host.InvokeAsync(new CompareWordDocumentsTool(), new { original = "original.docx", revised = "doc" });
        var check = await host.InvokeAsync(new CheckWordDocumentTool(), new { document = "doc", checks = new[] { "accessibility" } });

        Assert.Contains("## Alpha", markdown, StringComparison.Ordinal);
        Assert.Contains("| Region | Value |", markdown, StringComparison.Ordinal);
        Assert.Contains("[doc:", csv, StringComparison.Ordinal);
        Assert.Contains("Region,Value", csv, StringComparison.Ordinal);
        Assert.Contains("page 2", search, StringComparison.Ordinal);
        Assert.Contains("**Beta**", search, StringComparison.Ordinal);
        Assert.Contains("1 changed", compare, StringComparison.Ordinal);
        Assert.Contains("\"\" → \"changed\"", compare, StringComparison.Ordinal);
        Assert.Contains("level 3 right after level 1", check, StringComparison.Ordinal);
        Assert.Contains("no title property", check, StringComparison.Ordinal);
        Assert.DoesNotContain("no header row", check, StringComparison.Ordinal);
    }

    private static async Task<List<WordBlock>> CreateAsync(WordToolTestHost host)
    {
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "heading", text = "Alpha", level = 1 },
                new { type = "paragraph", text = "Alpha body text." },
                new { type = "table", columns = new[] { "Region", "Value" }, rows = new object[] { new object[] { "North", "10" }, new object[] { "South", "15" } } },
                new { type = "page_break" },
                new { type = "heading", text = "Beta", level = 1 },
                new { type = "heading", text = "Deep", level = 3 },
                new { type = "paragraph", text = "Beta body." },
            },
        });

        using var package = WordPackage.Open(await host.ReadWorkingDocumentAsync("doc"));

        return WordBlockReader.Read(package);
    }

    private static async Task<WordprocessingDocument> OpenAsync(WordToolTestHost host)
    {
        return WordprocessingDocument.Open(new MemoryStream(await host.ReadWorkingDocumentAsync("doc")), isEditable: false);
    }

    private static async Task<string> TextAsync(WordToolTestHost host)
    {
        using var document = await OpenAsync(host);

        return WordText.Of(document.MainDocumentPart.Document.Body);
    }
}
