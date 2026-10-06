using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Tools;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.Tests.Core.Documents.Word;

public sealed class StructureToolsTests
{
    [Fact]
    public async Task UpdateWordContent_ReplacesTextAndChangesStyle()
    {
        using var host = new WordToolTestHost();
        var blocks = await CreateAsync(host);
        var paragraph = blocks.First(block => block.Text == "Alpha body.");

        var answer = await host.InvokeAsync(new UpdateWordContentTool(), new { document = "doc", id = paragraph.Id, text = "New **body**.", style = "Quote" });

        Assert.Contains("version 2", answer, StringComparison.Ordinal);

        var updated = await ReadAsync(host);
        var block = updated.Single(item => item.Id == paragraph.Id);

        Assert.Equal("New body.", block.Text);
        Assert.Equal(WordBlockKind.Quote, block.Kind);
    }

    [Fact]
    public async Task UpdateWordContent_TrackedFindReplace_RecordsRevisions()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);

        await host.InvokeAsync(new UpdateWordContentTool(), new { document = "doc", find = "Alpha", replace = "Omega" });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);
        Assert.Contains("Omega body.", Text(bytes), StringComparison.Ordinal);

        // With tracking on, the next replacement is a revision.
        using (var package = WordPackage.Open(bytes))
        {
            WordSchemaOrder.Set(package.GetOrCreateSettings(), new TrackRevisions());
            await host.UploadAsync("tracked.docx", package.Save());
        }

        await host.InvokeAsync(new UpdateWordContentTool(), new { document = "tracked.docx", find = "Omega", replace = "Gamma" });

        var tracked = await host.ReadWorkingDocumentAsync("tracked");

        using var document = WordprocessingDocument.Open(new MemoryStream(tracked), isEditable: false);

        Assert.NotEmpty(document.MainDocumentPart.Document.Descendants<DeletedRun>());
        Assert.NotEmpty(document.MainDocumentPart.Document.Descendants<InsertedRun>());
        WordAuthoringToolsTests.AssertValid(tracked);
    }

    [Fact]
    public async Task MoveWordContent_MovesHeadingWithItsContent()
    {
        using var host = new WordToolTestHost();
        var blocks = await CreateAsync(host);
        var beta = blocks.First(block => block.Text == "Beta");
        var alpha = blocks.First(block => block.Text == "Alpha");

        await host.InvokeAsync(new MoveWordContentTool(), new { document = "doc", heading_with_content = beta.Id, before = alpha.Id });

        var texts = (await ReadAsync(host)).Select(block => block.Text).Where(text => text.Length > 0).ToList();

        Assert.True(texts.IndexOf("Beta body.") < texts.IndexOf("Alpha"), string.Join(" | ", texts));
        Assert.Equal(texts.IndexOf("Beta") + 1, texts.IndexOf("Beta body."));
    }

    [Fact]
    public async Task RemoveWordContent_RemovesHeadingWithContentAndDocument()
    {
        using var host = new WordToolTestHost();
        var blocks = await CreateAsync(host);

        await host.InvokeAsync(new RemoveWordContentTool(), new { document = "doc", heading_with_content = blocks.First(block => block.Text == "Alpha").Id });

        var texts = (await ReadAsync(host)).Select(block => block.Text).ToList();

        Assert.DoesNotContain("Alpha body.", texts);
        Assert.Contains("Beta body.", texts);
        WordAuthoringToolsTests.AssertValid(await host.ReadWorkingDocumentAsync("doc"));

        var answer = await host.InvokeAsync(new RemoveWordContentTool(), new { document = "doc", scope = "document" });

        Assert.Contains("Deleted working document", answer, StringComparison.Ordinal);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);
    }

    [Fact]
    public async Task AddWordSection_CreatesLandscapeSectionAndRemovingItKeepsDocumentValid()
    {
        using var host = new WordToolTestHost();
        var blocks = await CreateAsync(host);

        await host.InvokeAsync(new AddWordSectionTool(), new { document = "doc", before = blocks.First(block => block.Text == "Beta").Id, page_setup = new { orientation = "landscape" } });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using (var package = WordPackage.Open(bytes))
        {
            var sections = AI.Documents.Word.Editing.WordSections.All(package);

            Assert.Equal(2, sections.Count);
            Assert.True(AI.Documents.Word.Editing.WordSections.PageSize(sections[1]).Width > AI.Documents.Word.Editing.WordSections.PageSize(sections[0]).Width);
        }

        await host.InvokeAsync(new RemoveWordContentTool(), new { document = "doc", section = 2 });

        var removed = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(removed);
        Assert.DoesNotContain("Beta", Text(removed), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddWordToc_InsertsEntriesWithPageNumbers()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);

        var answer = await host.InvokeAsync(new AddWordTocTool(), new { document = "doc" });

        Assert.Contains("Inserted a table of contents", answer, StringComparison.Ordinal);
        Assert.Contains("2 entries", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var toc = document.MainDocumentPart.Document.Body.Elements<SdtBlock>().Single();
        var text = WordText.Of(toc);

        // The table takes the first page, so the headings follow on pages 2 and 3.
        Assert.Contains("Alpha\t2", text, StringComparison.Ordinal);
        Assert.Contains("Beta\t3", text, StringComparison.Ordinal);

        var again = await host.InvokeAsync(new AddWordTocTool(), new { document = "doc" });

        Assert.Contains("Refreshed", again, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddWordIndex_ListsMarkedTermsWithPages()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);

        await host.InvokeAsync(new AddWordIndexTool(), new { document = "doc", heading = "Index", entries = new object[] { new { term = "Alpha" }, new { term = "Beta", subentry = "body" } } });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        var text = Text(bytes);

        Assert.Contains("Alpha, 1", text, StringComparison.Ordinal);
        Assert.Contains("body, 2", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddWordCaptionAndCrossReference_ReferenceShowsCaptionNumber()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "paragraph", text = "See the table." },
                new { type = "table", columns = new[] { "A", "B" }, rows = new object[] { new object[] { "1", "2" } } },
            },
        });

        var blocks = await ReadAsync(host);
        var table = blocks.Single(block => block.Kind == WordBlockKind.Table);

        await host.InvokeAsync(new AddWordCaptionTool(), new { document = "doc", target = table.Id, text = "Values" });

        var caption = (await ReadAsync(host)).Single(block => block.Kind == WordBlockKind.Caption);

        Assert.Equal("Table 1: Values", caption.Text);

        var reference = await host.InvokeAsync(new AddWordCrossReferenceTool(), new { document = "doc", id = blocks[0].Id, target = caption.Id, prefix = " (" , suffix = ")" });

        Assert.Contains("See the table. (Table 1)", reference, StringComparison.Ordinal);
        WordAuthoringToolsTests.AssertValid(await host.ReadWorkingDocumentAsync("doc"));
    }

    [Fact]
    public async Task AddWordBookmarkAndHyperlink_LinkPointsAtBookmark()
    {
        using var host = new WordToolTestHost();
        var blocks = await CreateAsync(host);
        var beta = blocks.First(block => block.Text == "Beta");
        var body = blocks.First(block => block.Text == "Alpha body.");

        await host.InvokeAsync(new AddWordBookmarkTool(), new { document = "doc", name = "beta section", id = beta.Id });

        var list = await host.InvokeAsync(new AddWordBookmarkTool(), new { document = "doc", action = "list" });

        Assert.Contains("\"beta_section\"", list, StringComparison.Ordinal);

        await host.InvokeAsync(new AddWordHyperlinkTool(), new { document = "doc", id = body.Id, text = "body", bookmark = "beta_section" });
        await host.InvokeAsync(new AddWordHyperlinkTool(), new { document = "doc", id = body.Id, append_text = " site", url = "https://example.com" });

        var refused = await host.InvokeAsync(new AddWordHyperlinkTool(), new { document = "doc", id = body.Id, append_text = "x", url = "javascript:alert(1)" });

        Assert.Contains("only https, http and mailto", refused, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var links = document.MainDocumentPart.Document.Descendants<Hyperlink>().ToList();

        Assert.Contains(links, link => link.Anchor?.Value == "beta_section" && link.InnerText == "body");
        Assert.Contains(links, link => link.Id is not null && link.InnerText == " site");
    }

    [Fact]
    public async Task GetWordDocumentOutline_ListsHeadingsWithPages()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);

        var answer = await host.InvokeAsync(new GetWordDocumentOutlineTool(), new { document = "doc", page_numbers = true });

        Assert.Contains("H1: Alpha (page 1)", answer, StringComparison.Ordinal);
        Assert.Contains("H1: Beta (page 2)", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddWordPageBreak_InsertsBreak()
    {
        using var host = new WordToolTestHost();
        var blocks = await CreateAsync(host);

        await host.InvokeAsync(new AddWordPageBreakTool(), new { document = "doc", after = blocks.First(block => block.Text == "Alpha").Id });

        Assert.Contains(await ReadAsync(host), block => block.Kind == WordBlockKind.PageBreak);
    }

    private static async Task<List<WordBlock>> CreateAsync(WordToolTestHost host)
    {
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "heading", text = "Alpha", level = 1 },
                new { type = "paragraph", text = "Alpha body." },
                new { type = "page_break" },
                new { type = "heading", text = "Beta", level = 1 },
                new { type = "paragraph", text = "Beta body." },
            },
        });

        return await ReadAsync(host);
    }

    private static async Task<List<WordBlock>> ReadAsync(WordToolTestHost host)
    {
        using var package = WordPackage.Open(await host.ReadWorkingDocumentAsync("doc"));

        return WordBlockReader.Read(package);
    }

    private static string Text(byte[] bytes)
    {
        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);

        return WordText.Of(document.MainDocumentPart.Document.Body);
    }
}
