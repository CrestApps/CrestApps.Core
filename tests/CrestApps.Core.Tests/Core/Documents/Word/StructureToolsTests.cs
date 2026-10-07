using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Rendering;
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

        // The page number sits at the right tab on the entry's own line.
        using (var package = WordPackage.Open(bytes))
        {
            var page = WordPreview.PageText(WordPreview.Layout(package, host.Services).Pages[0]);

            Assert.Contains("\nAlpha 2\n", page, StringComparison.Ordinal);
        }

        var again = await host.InvokeAsync(new AddWordTocTool(), new { document = "doc" });

        Assert.Contains("Refreshed", again, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddWordToc_GoesAfterTheTitleAndItsCoverPage()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            title = "Report",
            content = new object[]
            {
                new { type = "page_break" },
                new { type = "heading", text = "Alpha", level = 1 },
                new { type = "paragraph", text = "Alpha body." },
            },
        });

        await host.InvokeAsync(new AddWordTocTool(), new { document = "doc" });

        var kinds = (await ReadAsync(host)).Select(block => block.Kind).ToList();

        Assert.Equal([WordBlockKind.Title, WordBlockKind.PageBreak, WordBlockKind.TableOfContents, WordBlockKind.PageBreak, WordBlockKind.Heading], kinds.Take(5));
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
    public async Task AddWordCrossReference_LabelWrittenBeforeReference_IsNotRepeated()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "table", caption = "Budget by phase", columns = new[] { "Item", "Planned" }, rows = new object[] { new object[] { "Design", "12000" } } },
                new { type = "paragraph", text = "Refer to Table" },
                new { type = "paragraph", text = "Costs are in" },
            },
        });

        var blocks = await ReadAsync(host);
        var caption = blocks.Single(block => block.Kind == WordBlockKind.Caption);
        var trailing = blocks.Single(block => block.Text == "Refer to Table");
        var plain = blocks.Single(block => block.Text == "Costs are in");

        var fromText = await host.InvokeAsync(new AddWordCrossReferenceTool(), new { document = "doc", id = trailing.Id, target = caption.Id, suffix = " for details." });
        var fromPrefix = await host.InvokeAsync(new AddWordCrossReferenceTool(), new { document = "doc", id = plain.Id, target = caption.Id, prefix = " the summary in Table ", suffix = "." });

        Assert.Contains("\"Refer to Table 1 for details.\"", fromText, StringComparison.Ordinal);
        Assert.Contains("was left out", fromText, StringComparison.Ordinal);
        Assert.Contains("\"Costs are in the summary in Table 1.\"", fromPrefix, StringComparison.Ordinal);
        WordAuthoringToolsTests.AssertValid(await host.ReadWorkingDocumentAsync("doc"));
    }

    [Fact]
    public async Task AddWordCrossReferenceAndCaption_CaptionedTableByItsId_UsesItsCaption()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "table", caption = "Budget by phase", columns = new[] { "Item", "Planned", "Variance" }, formats = new[] { "", "currency", "percent" }, rows = new object[] { new object[] { "Design", 12000, -0.042 } } },
                new { type = "paragraph", text = "Costs are summarized." },
            },
        });

        var blocks = await ReadAsync(host);
        var table = blocks.Single(block => block.Kind == WordBlockKind.Table);
        var sentence = blocks.Single(block => block.Text == "Costs are summarized.");

        var captioned = await host.InvokeAsync(new AddWordCaptionTool(), new { document = "doc", target = table.Id, text = "Budget by phase and quarter" });
        var reference = await host.InvokeAsync(new AddWordCrossReferenceTool(), new { document = "doc", id = sentence.Id, target = table.Id, prefix = "See ", suffix = "." });

        Assert.Contains("already captioned", captioned, StringComparison.Ordinal);
        Assert.Contains("\"Costs are summarized. See Table 1.\"", reference, StringComparison.Ordinal);

        var after = await ReadAsync(host);
        var caption = Assert.Single(after, block => block.Kind == WordBlockKind.Caption);

        Assert.Equal("Table 1: Budget by phase and quarter", caption.Text);

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);
        Assert.Contains("$12,000.00", Text(bytes), StringComparison.Ordinal);
        Assert.Contains("-4.2%", Text(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddWordHyperlinkAndCrossReference_WordsAlreadyWritten_AreNotWrittenTwice()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "toc" },
                new { type = "page_break" },
                new { type = "heading", text = "Budget", level = 1 },
                new { type = "table", caption = "Budget by phase", columns = new[] { "Item" }, rows = new object[] { new object[] { "Design" } } },
                new { type = "paragraph", text = "As detailed in " },
                new { type = "paragraph", text = "For more details, visit the CrestApps website." },
            },
        });

        var blocks = await ReadAsync(host);
        var caption = blocks.Single(block => block.Kind == WordBlockKind.Caption);
        var detailed = blocks.Single(block => block.Text.StartsWith("As detailed", StringComparison.Ordinal));
        var visit = blocks.Single(block => block.Text.StartsWith("For more", StringComparison.Ordinal));

        var reference = await host.InvokeAsync(new AddWordCrossReferenceTool(), new { document = "doc", id = detailed.Id, target = caption.Id, prefix = "As detailed in " });
        var link = await host.InvokeAsync(new AddWordHyperlinkTool(), new { document = "doc", id = visit.Id, append_text = "CrestApps website", url = "https://crestapps.com" });

        Assert.Contains("\"As detailed in Table 1\"", reference, StringComparison.Ordinal);
        Assert.Contains("already in the paragraph", link, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var body = document.MainDocumentPart.Document.Body;

        Assert.Equal("For more details, visit the CrestApps website.", WordText.Of(body.Elements<Paragraph>().Last()));
        Assert.Equal("CrestApps website", body.Descendants<Hyperlink>().Single(item => item.Id is not null).InnerText);

        // The page break written after the contents is the one the contents already end with.
        Assert.Single(body.Descendants<Break>(), item => item.Type?.Value == BreakValues.Page);
    }

    [Fact]
    public async Task AddWordContent_SubPointsInItemTextAndUnitsInHeaders_NestsAndFormats()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "numbered_list", items = new[] { "Launch", "Integrate analytics.\n\n- Better decisions.\n- Real-time reporting.", "Grow" } },
                new { type = "table", columns = new[] { "Item", "Planned ($)", "Variance (%)", "Score (%)" }, rows = new object[] { new object[] { "Design", 12000, -0.042, 85 } } },
            },
        });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var body = document.MainDocumentPart.Document.Body;
        var items = body.Elements<Paragraph>()
            .Where(paragraph => paragraph.ParagraphProperties?.NumberingProperties is not null)
            .Select(paragraph => (Text: WordText.Of(paragraph), Level: paragraph.ParagraphProperties.NumberingProperties.NumberingLevelReference.Val.Value))
            .ToList();

        Assert.Equal(["Launch", "Integrate analytics.", "Better decisions.", "Real-time reporting.", "Grow"], items.Select(item => item.Text));
        Assert.Equal([0, 0, 1, 1, 0], items.Select(item => item.Level));

        var cells = body.Descendants<TableRow>().Last().Elements<TableCell>().Select(cell => WordText.Of(cell)).ToList();

        // A fraction under "(%)" is a percentage; 85 already is one, so it stays as written.
        Assert.Equal(["Design", "$12,000.00", "-4.2%", "85"], cells);
    }

    [Fact]
    public async Task GetWordDocumentAndAddWordContent_TableRowsAndListItem_ListsRowsAndContinuesTheList()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "table", columns = new[] { "Item", "Actual" }, rows = new object[] { new object[] { "Design", 1 }, new object[] { "Build", 2 }, new object[] { "Testing", 3 } } },
                new { type = "numbered_list", items = new[] { "First", "Second", "Third" } },
            },
        });

        var listing = await host.InvokeAsync(new GetWordDocumentTool(), new { document = "doc" });

        Assert.Contains("rows 2 \"Design\", 3 \"Build\", 4 \"Testing\"", listing, StringComparison.Ordinal);

        var third = (await ReadAsync(host)).Single(block => block.Text == "Third");

        await host.InvokeAsync(new AddWordContentTool(), new { document = "doc", after = third.Id, content = new object[] { new { type = "numbered_list", items = new[] { "Fourth" } } } });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var ids = document.MainDocumentPart.Document.Body.Elements<Paragraph>()
            .Where(paragraph => paragraph.ParagraphProperties?.NumberingProperties is not null)
            .Select(paragraph => paragraph.ParagraphProperties.NumberingProperties.NumberingId.Val.Value)
            .ToList();

        Assert.Equal(4, ids.Count);
        Assert.Single(ids.Distinct());
    }

    [Fact]
    public async Task UpdateWordTable_PlainNumbersInFormattedColumns_TakeTheColumnsLook()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "table", columns = new[] { "Item", "Actual", "Variance", "Note" }, formats = new[] { "", "currency", "percent", "" }, rows = new object[] { new object[] { "Design", 11500, -0.042, "On track" }, new object[] { "Testing", 7600, -0.05, "Late" } } },
            },
        });

        var table = (await ReadAsync(host)).Single(block => block.Kind == WordBlockKind.Table);

        await host.InvokeAsync(new UpdateWordTableTool(), new
        {
            document = "doc",
            table = table.Id,
            cells = new object[] { new { row = "Testing", column = 2, text = "9500" }, new { row = "Testing", column = 3, text = "0.188" }, new { row = "Testing", column = 4, text = "1200" } },
            add_rows = new object[] { new[] { "Contingency", "2000", "-1", "None" } },
        });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var rows = document.MainDocumentPart.Document.Body.Descendants<TableRow>()
            .Select(row => row.Elements<TableCell>().Select(cell => WordText.Of(cell)).ToList())
            .ToList();

        Assert.Equal(["Testing", "$9,500.00", "18.8%", "1200"], rows[2]);
        Assert.Equal(["Contingency", "$2,000.00", "-100.0%", "None"], rows[3]);
    }

    [Fact]
    public async Task AddWordContent_ListWrittenAgainWithANewItem_AddsOnlyTheNewItemToTheList()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "bullet_list", items = new[] { "Resource delays.", "Data migration", "Regulation" } },
                new { type = "paragraph", text = "Next steps." },
            },
        });

        var answer = await host.InvokeAsync(new AddWordContentTool(), new
        {
            document = "doc",
            content = new object[] { new { type = "bullet_list", items = new[] { "Resource delays", "Data migration.", "Regulation", "Vendor delays" } } },
        });

        Assert.Contains("only the new item(s) were added", answer, StringComparison.Ordinal);

        var texts = (await ReadAsync(host)).Select(block => block.Text).ToList();

        Assert.Equal(["Resource delays.", "Data migration", "Regulation", "Vendor delays", "Next steps."], texts);

        var again = await host.InvokeAsync(new AddWordContentTool(), new
        {
            document = "doc",
            content = new object[] { new { type = "bullet_list", items = new[] { "Resource delays", "Data migration", "Regulation", "Vendor delays" } } },
        });

        Assert.Contains("already the list", again, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddWordContent_ListItemsWithTheirOwnMarkers_NestsThemAndDropsTheMarkers()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "numbered_list", items = new[] { "Grow", "Ship, including:", "- Beta", "- Launch", "Hire" } },
            },
        });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var items = document.MainDocumentPart.Document.Body.Elements<Paragraph>()
            .Where(paragraph => paragraph.ParagraphProperties?.NumberingProperties is not null)
            .Select(paragraph => (Text: WordText.Of(paragraph), Level: paragraph.ParagraphProperties.NumberingProperties.NumberingLevelReference.Val.Value, Id: paragraph.ParagraphProperties.NumberingProperties.NumberingId.Val.Value))
            .ToList();

        Assert.Equal(["Grow", "Ship, including:", "Beta", "Launch", "Hire"], items.Select(item => item.Text));
        Assert.Equal([0, 0, 1, 1, 0], items.Select(item => item.Level));
        Assert.NotEqual(items[0].Id, items[2].Id);
        Assert.Equal(items[0].Id, items[4].Id);
    }

    [Fact]
    public async Task CreateWordDocument_TitleRepeatedAndTocBlock_WritesTitleOnceAndFillsTheContents()
    {
        using var host = new WordToolTestHost();

        var answer = await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            title = "Project Falcon",
            content = new object[]
            {
                new { type = "title", text = "Project  Falcon" },
                new { type = "page_break" },
                new { type = "toc" },
                new { type = "page_break" },
                new { type = "heading", text = "Summary", level = 1 },
                new { type = "paragraph", text = "Body." },
                new { type = "heading", text = "Goals", level = 1 },
                new
                {
                    type = "numbered_list",
                    items = new object[]
                    {
                        "Grow",
                        new { text = "Ship", items_type = "bullet", items = new[] { "Beta", "Launch" } },
                        "Hire",
                    },
                },
            },
        });

        Assert.Contains("left out", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var body = document.MainDocumentPart.Document.Body;
        var paragraphs = body.Descendants<Paragraph>().ToList();

        Assert.Single(paragraphs, paragraph => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Title");

        var contents = WordText.Of(body.Descendants<DocumentFormat.OpenXml.Wordprocessing.SdtBlock>().First());

        Assert.Contains("Summary", contents, StringComparison.Ordinal);
        Assert.Contains("Goals", contents, StringComparison.Ordinal);

        // The nested items are bullets of their own list, not the numbered list's second level.
        var numbering = document.MainDocumentPart.NumberingDefinitionsPart.Numbering;
        string FormatOf(string text)
        {
            var paragraph = paragraphs.Single(item => WordText.Of(item) == text);
            var properties = paragraph.ParagraphProperties.NumberingProperties;
            var numberId = properties.NumberingId.Val.Value;
            var level = properties.NumberingLevelReference.Val.Value;
            var abstractId = numbering.Elements<NumberingInstance>().Single(instance => instance.NumberID.Value == numberId).AbstractNumId.Val.Value;
            var definition = numbering.Elements<AbstractNum>().Single(item => item.AbstractNumberId.Value == abstractId);

            return definition.Elements<Level>().Single(item => item.LevelIndex.Value == level).NumberingFormat.Val.ToString();
        }

        Assert.Equal("decimal", FormatOf("Ship"));
        Assert.Equal("bullet", FormatOf("Beta"));
        Assert.Equal("decimal", FormatOf("Hire"));
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
