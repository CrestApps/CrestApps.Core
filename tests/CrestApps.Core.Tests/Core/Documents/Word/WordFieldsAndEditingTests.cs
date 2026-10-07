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

    internal static void Add(WordPackage package, OpenXmlElement element)
    {
        WordSections.EnsureBodySection(package.Body).InsertBeforeSelf(element);
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
