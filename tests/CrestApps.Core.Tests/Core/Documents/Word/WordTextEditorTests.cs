using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.Tests.Core.Documents.Word;

public sealed class WordTextEditorTests
{
    [Fact]
    public void Replace_AcrossRuns_KeepsFirstRunFormatting()
    {
        var paragraph = new Paragraph(
            new Run(new RunProperties(new Bold()), new Text("Total rev") { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }),
            new Run(new Text("enue grew.")));

        var count = WordTextEditor.Replace(paragraph, "revenue", "sales", matchCase: true, wholeWord: false, revisions: null);

        Assert.Equal(1, count);
        Assert.Equal("Total sales grew.", WordText.Of(paragraph));
        Assert.Contains(paragraph.Elements<Run>(), run => run.InnerText == "sales" && run.RunProperties?.Bold is not null);
    }

    [Fact]
    public void Replace_InsideOneRun_SplitsTheRun()
    {
        var paragraph = new Paragraph(new Run(new Text("one two three")));

        WordTextEditor.Replace(paragraph, "two", "2", matchCase: false, wholeWord: true, revisions: null);

        Assert.Equal("one 2 three", WordText.Of(paragraph));
    }

    [Fact]
    public void Replace_Tracked_RecordsDeletionAndInsertion()
    {
        using var package = WordPackage.Create(new WordDesign());
        var paragraph = new Paragraph(new Run(new Text("The old wording stays.")));

        package.Body.PrependChild(paragraph);

        var revisions = WordRevisions.For(package, "Reviewer", new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc));

        WordTextEditor.Replace(paragraph, "old", "new", matchCase: true, wholeWord: true, revisions);

        var deleted = Assert.Single(paragraph.Descendants<DeletedRun>());
        var inserted = Assert.Single(paragraph.Descendants<InsertedRun>());

        Assert.Equal("old", string.Concat(deleted.Descendants<DeletedText>().Select(text => text.Text)));
        Assert.Equal("new", inserted.InnerText);
        Assert.Equal("Reviewer", inserted.Author.Value);
        Assert.Equal("The new wording stays.", WordText.Of(paragraph));
        Assert.Equal("The old wording stays.", WordText.Of(paragraph, includeDeleted: true).Replace("new", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void ReplaceParagraph_KeepsBookmarks()
    {
        using var package = WordPackage.Create(new WordDesign());
        var paragraph = new Paragraph(
            new BookmarkStart { Name = "_Toc1", Id = "1" },
            new Run(new Text("Old heading")),
            new BookmarkEnd { Id = "1" });

        package.Body.PrependChild(paragraph);

        WordTextEditor.ReplaceParagraph(paragraph, "New **heading**", package.MainPart, revisions: null);

        Assert.Equal("New heading", WordText.Of(paragraph));
        Assert.Single(paragraph.Elements<BookmarkStart>());
        Assert.Single(paragraph.Elements<BookmarkEnd>());
        Assert.Contains(paragraph.Elements<Run>(), run => run.InnerText == "heading" && run.RunProperties?.Bold is not null);
    }

    [Fact]
    public void ToMarkdown_WritesEmphasisAndLinks()
    {
        using var package = WordPackage.Create(new WordDesign());
        var paragraph = new Paragraph();

        WordInlineWriter.AppendMarkdown(paragraph, "Read **this** and *that* at [the site](https://example.com/).", package.MainPart);
        package.Body.PrependChild(paragraph);

        Assert.Equal("Read **this** and *that* at [the site](https://example.com/).", WordTextEditor.ToMarkdown(paragraph, package.MainPart));
    }
}
