using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Structure;
using CrestApps.Core.AI.Documents.Word.Tools;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.Tests.Core.Documents.Word;

public sealed class WordFieldsAndEditingTests
{
    [Fact]
    public void Refresh_TocDirectlyInBody_RebuildsEntriesFromHeadings()
    {
        using var package = WordPackage.Create(new WordDesign());

        AddMultiParagraphField(package, "TOC \\o \"1-3\" \\h \\z \\u", "Stale entry");
        Add(package, Heading(package, "Alpha", 1));
        Add(package, new Paragraph(new Run(new Text("Alpha body."))));
        Add(package, Heading(package, "Beta", 2));

        WordDocumentRefresher.Refresh(package, services: null);

        var toc = TocParagraphs(package);
        var text = string.Join("\n", toc.Select(paragraph => WordText.Of(paragraph)));

        Assert.Contains("Alpha\t1", text, StringComparison.Ordinal);
        Assert.Contains("Beta\t1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("No table of contents entries", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Stale entry", text, StringComparison.Ordinal);
        AssertValid(package);
    }

    [Fact]
    public void Refresh_TableOfFigures_KeepsItsEntries()
    {
        using var package = WordPackage.Create(new WordDesign());

        AddMultiParagraphField(package, "TOC \\h \\z \\c \"Figure\"", "Figure 1: Revenue by region\t4");
        Add(package, Heading(package, "Alpha", 1));

        WordDocumentRefresher.Refresh(package, services: null);

        var text = WordText.Of(package.Body);

        Assert.Contains("Figure 1: Revenue by region\t4", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Alpha\t", text, StringComparison.Ordinal);

        // The table was left for Word, so the file asks Word to update it.
        Assert.True(package.MainPart.DocumentSettingsPart?.Settings?.GetFirstChild<UpdateFieldsOnOpen>()?.Val?.Value);
    }

    [Fact]
    public void Refresh_EverythingRefreshed_DoesNotAskWordToUpdateFields()
    {
        using var package = WordPackage.Create(new WordDesign());

        AddMultiParagraphField(package, "TOC \\o \"1-3\" \\h \\z \\u", string.Empty);
        Add(package, Heading(package, "Alpha", 1));

        WordDocumentRefresher.Refresh(package, services: null);

        Assert.Null(package.MainPart.DocumentSettingsPart?.Settings?.GetFirstChild<UpdateFieldsOnOpen>());
    }

    [Fact]
    public async Task AddWordToc_DocumentWithOnlyTableOfFigures_InsertsTableOfContents()
    {
        using var host = new WordToolTestHost();
        using var package = WordPackage.Create(new WordDesign());

        AddMultiParagraphField(package, "TOC \\h \\z \\c \"Figure\"", "Figure 1: Revenue\t1");
        Add(package, Heading(package, "Alpha", 1));
        await host.UploadAsync("figures.docx", package.Save());

        var answer = await host.InvokeAsync(new AddWordTocTool(), new { document = "figures.docx" });

        Assert.Contains("Inserted a table of contents", answer, StringComparison.Ordinal);
        Assert.Contains("1 entry", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("figures");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var result = WordPackage.Open(bytes);

        Assert.Contains("Figure 1: Revenue\t1", WordText.Of(result.Body), StringComparison.Ordinal);
    }

    [Fact]
    public void Update_RefWithParagraphNumberSwitch_KeepsWordsResult()
    {
        using var package = WordPackage.Create(new WordDesign());

        Add(package, new Paragraph(
            new BookmarkStart { Name = "_Ref1", Id = "1" },
            new Run(new Text("Market overview")),
            new BookmarkEnd { Id = "1" }));

        var numbered = new Paragraph(new Run(new Text("See ")));
        var text = new Paragraph(new Run(new Text("Also ")));

        WordFieldWriter.Append(numbered, "REF _Ref1 \\r \\h", "2.3");
        WordFieldWriter.Append(text, "REF _Ref1 \\h", "old");
        Add(package, numbered);
        Add(package, text);

        WordReferenceUpdater.Update(package, services: null);

        Assert.Equal("See 2.3", WordText.Of(numbered));
        Assert.Equal("Also Market overview", WordText.Of(text));
    }

    [Fact]
    public void Renumber_NumberFormatsAndChapters_FollowWordsSwitches()
    {
        using var package = WordPackage.Create(new WordDesign());

        Add(package, Heading(package, "One", 1));
        var first = Caption(package, "SEQ Figure \\* ROMAN");
        var second = Caption(package, "SEQ Figure \\* ROMAN");
        var listing = Caption(package, "SEQ \"Code Listing\" \\* alphabetic");
        var chapter = Caption(package, "SEQ Table \\* ARABIC \\s 1");

        Add(package, Heading(package, "Two", 1));
        var nextListing = Caption(package, "SEQ \"Code Listing\" \\* alphabetic");
        var nextChapter = Caption(package, "SEQ Table \\* ARABIC \\s 1");
        var third = Caption(package, "SEQ Figure \\* ROMAN");

        var counters = WordCaptions.Renumber(package);

        Assert.Equal("I", Result(first));
        Assert.Equal("II", Result(second));
        Assert.Equal("III", Result(third));
        Assert.Equal("a", Result(listing));
        Assert.Equal("b", Result(nextListing));
        Assert.Equal("1", Result(chapter));
        Assert.Equal("1", Result(nextChapter));
        Assert.Equal(2, counters["Code Listing"]);
    }

    [Fact]
    public void ArgumentOf_QuotedName_ReadsTheWholeName()
    {
        Assert.Equal("Code Listing", WordFieldScanner.ArgumentOf("SEQ \"Code Listing\" \\* ARABIC"));
        Assert.Equal("_Ref12", WordFieldScanner.ArgumentOf(" REF _Ref12 \\h "));
        Assert.Equal(string.Empty, WordFieldScanner.ArgumentOf("TOC \\o \"1-3\""));
        Assert.True(WordFieldScanner.HasSwitch("TOC \\o \"1-3\" \\h", 'h'));
        Assert.False(WordFieldScanner.HasSwitch("SEQ \"a \\c b\"", 'c'));
    }

    [Fact]
    public void RefreshAll_IndexEndingASection_KeepsTheSectionBreak()
    {
        using var package = WordPackage.Create(new WordDesign());
        var marked = new Paragraph(new Run(new Text("Revenue grew.")));

        foreach (var run in WordIndex.CreateEntry("Revenue", subentry: null))
        {
            marked.Append(run);
        }

        Add(package, marked);

        var index = WordIndex.Create(package, columns: 2);

        index[^1].ParagraphProperties = new ParagraphProperties(new SectionProperties(new SectionType { Val = SectionMarkValues.Continuous }, new Columns { ColumnCount = 2 }));

        foreach (var paragraph in index)
        {
            Add(package, paragraph);
        }

        Add(package, new Paragraph(new Run(new Text("After the index."))));

        var sections = WordSections.All(package).Count;

        WordIndex.RefreshAll(package, services: null);

        Assert.Equal(sections, WordSections.All(package).Count);
        Assert.Contains("Revenue, 1", WordText.Of(package.Body), StringComparison.Ordinal);
        AssertValid(package);
    }

    [Fact]
    public void Entries_EntryWithSeeAlsoSwitch_ReadsTheFirstArgument()
    {
        using var package = WordPackage.Create(new WordDesign());
        var paragraph = new Paragraph(new Run(new Text("Revenue")));

        WordFieldWriter.Append(paragraph, "XE \"Revenue\" \\t \"See Sales\"", string.Empty);
        Add(package, paragraph);

        var entry = Assert.Single(WordIndex.Entries(package));

        Assert.Equal("Revenue", entry.Key);
    }

    [Fact]
    public async Task AddWordIndex_TermInTableOfContents_IsNotMarkedThere()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "heading", text = "Alpha", level = 1 },
                new { type = "paragraph", text = "Alpha body." },
                new { type = "heading", text = "Beta", level = 1 },
                new { type = "heading", text = "Gamma", level = 1 },
            },
        });

        await host.InvokeAsync(new AddWordTocTool(), new { document = "doc", page_break_after = false });
        await host.InvokeAsync(new AddWordIndexTool(), new { document = "doc", entries = new object[] { new { term = "Alpha" }, new { term = "Beta" } } });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var toc = document.MainDocumentPart.Document.Body.Elements<SdtBlock>().Single();

        Assert.DoesNotContain(toc.Descendants<FieldCode>(), code => code.Text.Contains("XE", StringComparison.Ordinal));
        Assert.Contains(document.MainDocumentPart.Document.Body.Descendants<FieldCode>(), code => code.Text.Contains("XE \"Beta\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemoveWordContent_ParagraphInTableCell_RemovesOnlyThatParagraph()
    {
        using var host = new WordToolTestHost();
        using var package = WordPackage.Create(new WordDesign());
        var kept = Plain("Keep me");
        var removed = Plain("Remove me");
        var only = Plain("Only one");

        Add(package, Table(new TableCell(kept, removed), new TableCell(only)));
        Add(package, Plain("After"));
        await host.UploadAsync("cells.docx", package.Save());

        await host.InvokeAsync(new RemoveWordContentTool(), new { document = "cells.docx", ids = new[] { removed.ParagraphId.Value, only.ParagraphId.Value } });

        var bytes = await host.ReadWorkingDocumentAsync("cells");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var result = WordPackage.Open(bytes);
        var table = Assert.Single(result.Body.Elements<Table>());
        var cells = table.Descendants<TableCell>().ToList();

        Assert.Equal("Keep me", WordText.OfCell(cells[0]));
        Assert.DoesNotContain("Remove me", WordText.Of(result.Body), StringComparison.Ordinal);

        // A cell whose only paragraph was removed keeps an empty one, as Word requires.
        Assert.IsType<Paragraph>(cells[1].LastChild);
    }

    [Fact]
    public async Task MoveWordContent_ParagraphInTableCell_IsRefused()
    {
        using var host = new WordToolTestHost();
        using var package = WordPackage.Create(new WordDesign());
        var inCell = Plain("In a cell");
        var after = Plain("After");

        Add(package, Table(new TableCell(inCell)));
        Add(package, after);
        await host.UploadAsync("cells.docx", package.Save());

        var answer = await host.InvokeAsync(new MoveWordContentTool(), new { document = "cells.docx", ids = new[] { inCell.ParagraphId.Value }, after = after.ParagraphId.Value });

        Assert.Contains("inside a table cell", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddWordContent_TableAfterLastParagraphInCell_CellEndsWithParagraph()
    {
        using var host = new WordToolTestHost();
        using var package = WordPackage.Create(new WordDesign());
        var inCell = Plain("Cell text");

        Add(package, Table(new TableCell(inCell)));
        await host.UploadAsync("cells.docx", package.Save());

        await host.InvokeAsync(new AddWordContentTool(), new
        {
            document = "cells.docx",
            after = inCell.ParagraphId.Value,
            content = new object[] { new { type = "table", columns = new[] { "A" }, rows = new object[] { new object[] { "1" } } } },
        });

        var bytes = await host.ReadWorkingDocumentAsync("cells");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var result = WordPackage.Open(bytes);
        var outer = Assert.Single(result.Body.Elements<Table>());
        var cell = outer.Elements<TableRow>().Single().Elements<TableCell>().Single();

        Assert.Single(cell.Elements<Table>());
        Assert.IsType<Paragraph>(cell.LastChild);
    }

    [Fact]
    public async Task UpdateWordContent_BlockReplacesParagraphEndingSection_KeepsTheSectionBreak()
    {
        using var host = new WordToolTestHost();
        using var package = WordPackage.Create(new WordDesign());
        var ending = EndsSection(package, Plain("End of section one"));

        Add(package, ending);
        Add(package, Plain("Section two"));
        await host.UploadAsync("sections.docx", package.Save());

        await host.InvokeAsync(new UpdateWordContentTool(), new
        {
            document = "sections.docx",
            id = ending.ParagraphId.Value,
            block = new { type = "table", columns = new[] { "A" }, rows = new object[] { new object[] { "1" } } },
        });

        var bytes = await host.ReadWorkingDocumentAsync("sections");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var result = WordPackage.Open(bytes);
        var children = result.Body.ChildElements.ToList();
        var table = children.OfType<Table>().Single();
        var breakAt = children.FindIndex(child => child is Paragraph { ParagraphProperties.SectionProperties: not null });

        Assert.Equal(2, WordSections.All(result).Count);
        Assert.True(children.IndexOf(table) < breakAt);
        Assert.DoesNotContain("End of section one", WordText.Of(result.Body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoveWordContent_ParagraphEndingSection_LeavesTheSectionBreak()
    {
        using var host = new WordToolTestHost();
        using var package = WordPackage.Create(new WordDesign());
        var intro = Plain("Intro");
        var ending = EndsSection(package, Plain("End of section one"));
        var next = Plain("Section two");

        Add(package, intro);
        Add(package, ending);
        Add(package, next);
        await host.UploadAsync("sections.docx", package.Save());

        await host.InvokeAsync(new MoveWordContentTool(), new { document = "sections.docx", ids = new[] { ending.ParagraphId.Value }, after = next.ParagraphId.Value });

        var bytes = await host.ReadWorkingDocumentAsync("sections");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var result = WordPackage.Open(bytes);
        var breaks = result.Body.Elements<Paragraph>().Where(paragraph => paragraph.ParagraphProperties?.SectionProperties is not null).ToList();

        Assert.Equal("Intro", WordText.Of(Assert.Single(breaks)));
        Assert.Equal(["Intro", "Section two", "End of section one"], result.Body.Elements<Paragraph>().Select(paragraph => WordText.Of(paragraph)));
    }

    [Fact]
    public async Task RemoveWordContent_ParagraphEndingSectionAfterTable_KeepsTheBreakBelowTheTable()
    {
        using var host = new WordToolTestHost();
        using var package = WordPackage.Create(new WordDesign());
        var ending = EndsSection(package, Plain("End of section one"));

        Add(package, Plain("Intro"));
        Add(package, Table(new TableCell(Plain("Cell"))));
        Add(package, ending);
        Add(package, Plain("Section two"));
        await host.UploadAsync("sections.docx", package.Save());

        await host.InvokeAsync(new RemoveWordContentTool(), new { document = "sections.docx", ids = new[] { ending.ParagraphId.Value } });

        var bytes = await host.ReadWorkingDocumentAsync("sections");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var result = WordPackage.Open(bytes);
        var children = result.Body.ChildElements.ToList();
        var breakAt = children.FindIndex(child => child is Paragraph { ParagraphProperties.SectionProperties: not null });

        Assert.Equal(2, WordSections.All(result).Count);
        Assert.True(children.FindIndex(child => child is Table) < breakAt);
    }

    [Fact]
    public async Task AddWordHyperlink_TextInTrackedInsertion_KeepsTheInsertionInsideTheLink()
    {
        using var host = new WordToolTestHost();
        using var package = WordPackage.Create(new WordDesign());
        var paragraph = new Paragraph(
            new Run(new Text("Read the ") { Space = SpaceProcessingModeValues.Preserve }),
            new InsertedRun(new Run(new Text("full report") { Space = SpaceProcessingModeValues.Preserve })) { Id = "5", Author = "Reviewer", Date = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Run(new Text(" now.") { Space = SpaceProcessingModeValues.Preserve }));

        Add(package, paragraph);
        await host.UploadAsync("tracked.docx", package.Save());

        await host.InvokeAsync(new AddWordHyperlinkTool(), new { document = "tracked.docx", id = paragraph.ParagraphId.Value, text = "full", url = "https://example.com/" });

        var bytes = await host.ReadWorkingDocumentAsync("tracked");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var result = WordPackage.Open(bytes);
        var updated = result.Body.Elements<Paragraph>().First();
        var link = Assert.Single(updated.Elements<Hyperlink>());
        var insertions = updated.Descendants<InsertedRun>().ToList();

        Assert.Equal("Read the full report now.", WordText.Of(updated));
        Assert.Equal("full", WordText.Of(Assert.Single(link.Elements<InsertedRun>())));
        Assert.DoesNotContain(updated.Descendants<InsertedRun>(), insertion => insertion.Elements<Hyperlink>().Any());
        Assert.Equal(2, insertions.Count);
        Assert.All(insertions, insertion => Assert.Equal("Reviewer", insertion.Author.Value));
        Assert.NotEqual(insertions[0].Id.Value, insertions[1].Id.Value);
    }

    [Fact]
    public void Replace_AcrossCommentReference_KeepsTheReference()
    {
        var paragraph = new Paragraph(
            new Run(new Text("Hello")),
            new Run(new CommentReference { Id = "0" }),
            new Run(new TabChar(), new Text(" world") { Space = SpaceProcessingModeValues.Preserve }));

        var count = WordTextEditor.Replace(paragraph, "Hello world", "Hi", matchCase: true, wholeWord: false, revisions: null);

        Assert.Equal(1, count);
        Assert.Equal("Hi", WordText.Of(paragraph).Replace("\t", string.Empty, StringComparison.Ordinal));
        Assert.Single(paragraph.Descendants<CommentReference>());
        Assert.Single(paragraph.Descendants<TabChar>());
    }

    [Fact]
    public void Replace_TrackedAcrossCommentReference_DoesNotDeleteTheReference()
    {
        using var package = WordPackage.Create(new WordDesign());
        var paragraph = new Paragraph(
            new Run(new Text("Hello")),
            new Run(new CommentReference { Id = "0" }),
            new Run(new Text(" world") { Space = SpaceProcessingModeValues.Preserve }));

        Add(package, paragraph);

        var revisions = WordRevisions.For(package, "Reviewer", new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc));

        WordTextEditor.Replace(paragraph, "Hello world", "Hi", matchCase: true, wholeWord: false, revisions);

        var reference = Assert.Single(paragraph.Descendants<CommentReference>());

        Assert.Null(reference.Ancestors<DeletedRun>().FirstOrDefault());
        Assert.Equal("Hi", WordText.Of(paragraph));
    }

    [Fact]
    public void ReplaceParagraph_NestedMarkersAndPicture_KeepsMarkersAndReportsThePicture()
    {
        using var package = WordPackage.Create(new WordDesign());
        var paragraph = new Paragraph(
            new Hyperlink(
                new BookmarkStart { Name = "_Ref9", Id = "9" },
                new Run(new Text("Old link text")),
                new BookmarkEnd { Id = "9" }) { Anchor = "_Ref9" },
            new InsertedRun(new Run(new FootnoteReference { Id = 1 })) { Id = "3", Author = "Reviewer" },
            new Run(new Picture()));

        Add(package, paragraph);

        var revisions = WordRevisions.For(package, "Reviewer", new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc));
        var dropped = WordTextEditor.ReplaceParagraph(paragraph, "New text", package.MainPart, revisions);

        Assert.Contains("1 picture or drawing", dropped, StringComparison.Ordinal);
        Assert.Contains("1 link", dropped, StringComparison.Ordinal);
        Assert.Equal("New text", WordText.Of(paragraph));

        var start = Assert.Single(paragraph.Descendants<BookmarkStart>());
        var reference = Assert.Single(paragraph.Descendants<FootnoteReference>());

        Assert.Null(start.Ancestors<DeletedRun>().FirstOrDefault());
        Assert.Null(reference.Ancestors<DeletedRun>().FirstOrDefault());
        Assert.Single(paragraph.Descendants<BookmarkEnd>());
    }

    [Fact]
    public void ReplaceParagraph_TextOnly_ReportsNothing()
    {
        using var package = WordPackage.Create(new WordDesign());
        var paragraph = Plain("Old");

        Add(package, paragraph);

        Assert.Null(WordTextEditor.ReplaceParagraph(paragraph, "New", package.MainPart, revisions: null));
    }

    [Fact]
    public void Fit_SizeLargerThanAnyPage_IsCappedAtTwentyTwoInches()
    {
        var info = new WordImageInfo("image/png", ".png", 1000, 500, 96, 96);

        var (width, height) = WordImageWriter.Fit(info, 1_000_000, null, double.MaxValue, double.MaxValue);

        Assert.Equal(1584, width, precision: 6);
        Assert.Equal(792, height, precision: 6);
    }

    [Fact]
    public async Task CreateWordDocument_ChartWithControlCharactersAndHugeHeight_IsValid()
    {
        using var host = new WordToolTestHost();

        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "chart", chart_type = "column", title = "Rev\u0001enue", labels = new[] { "No\u0002rth", "South" }, series = new[] { new { name = "Sales\u0003", values = new[] { 1.0, 2.0 } } }, height = 100000 },
            },
        });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var package = WordPackage.Open(bytes);
        var extent = package.Body.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().Single();
        var chart = AI.Documents.Word.Charts.WordChartReader.Read(package.MainPart.ChartParts.Single());

        Assert.True(extent.Cy.Value <= WordUnits.ToEmus(1584));
        Assert.Equal("Revenue", chart.Title);
        Assert.Equal("North", chart.Labels[0]);
        Assert.Equal("Sales", chart.Series[0].Name);
    }

    [Fact]
    public void Read_ChartWithDocumentTypeDefinition_IsRefused()
    {
        using var package = WordPackage.Create(new WordDesign());
        var part = package.MainPart.AddNewPart<ChartPart>("rIdChart1");
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE c:chartSpace [<!ENTITY x \"Expanded\">]>" +
            "<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart><c:plotArea><c:barChart><c:ser><c:tx><c:v>&x;</c:v></c:tx></c:ser></c:barChart></c:plotArea></c:chart></c:chartSpace>";

        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)))
        {
            part.FeedData(stream);
        }

        Assert.Null(AI.Documents.Word.Charts.WordChartReader.Read(part));
    }

    internal static void Add(WordPackage package, OpenXmlElement element)
    {
        package.Ids.Assign(element);
        WordSections.EnsureBodySection(package.Body).InsertBeforeSelf(element);
    }

    internal static Paragraph Plain(string text)
    {
        return new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    internal static Table Table(params TableCell[] cells)
    {
        return new Table(
            new TableProperties(new TableWidth { Type = TableWidthUnitValues.Auto, Width = "0" }),
            new TableGrid(cells.Select(_ => new GridColumn { Width = "2000" })),
            new TableRow(cells));
    }

    private static Paragraph EndsSection(WordPackage package, Paragraph paragraph)
    {
        var section = (SectionProperties)WordSections.EnsureBodySection(package.Body).CloneNode(true);

        paragraph.PrependChild(new ParagraphProperties(section));

        return paragraph;
    }

    internal static Paragraph Heading(WordPackage package, string text, int level)
    {
        var style = WordStyleSheet.Ensure(package.MainPart, WordStyleSheet.Heading(level), new WordDesign());

        return new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = style }), new Run(new Text(text)));
    }

    internal static void AssertValid(WordPackage package)
    {
        WordAuthoringToolsTests.AssertValid(package.Save());
    }

    private static void AddMultiParagraphField(WordPackage package, string instruction, string result)
    {
        // The field begins in the first paragraph, as Word writes a table of contents, and ends in a second one.
        var runs = WordFieldWriter.CreateRuns(instruction, result);

        Add(package, new Paragraph(runs[0], runs[1], runs[2], runs[3]));
        Add(package, new Paragraph(runs[4]));
    }

    private static List<Paragraph> TocParagraphs(WordPackage package)
    {
        var field = WordFieldScanner.Scan(package.Body).Single(field => field.Type == "TOC");

        return WordTableOfContents.ParagraphsOf(field);
    }

    private static Paragraph Caption(WordPackage package, string instruction)
    {
        var paragraph = new Paragraph(new Run(new Text("Caption ") { Space = SpaceProcessingModeValues.Preserve }));

        WordFieldWriter.Append(paragraph, instruction, "0");
        Add(package, paragraph);

        return paragraph;
    }

    private static string Result(Paragraph caption)
    {
        return WordFieldScanner.Scan(caption).Single().Result;
    }
}
