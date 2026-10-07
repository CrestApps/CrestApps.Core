using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Tools;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Office2013.Word;
using DocumentFormat.OpenXml.Office2019.Word.Cid;
using DocumentFormat.OpenXml.Office2021.Word.CommentsExt;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.Tests.Core.Documents.Word;

public sealed partial class WordReviewToolsTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string Stamp = "w:author=\"Reviewer\" w:date=\"2024-01-01T00:00:00Z\"";

    [Fact]
    public async Task Reply_ToAReply_AttachesToTheThreadRoot()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);
        var body = await IdOfAsync(host, "Alpha body text.");

        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "add", id = body, text = "body", comment = "Is this right?" });
        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "reply", comment_id = "0", comment = "Yes." });
        var answer = await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "reply", comment_id = "1", comment = "Are you sure?" });

        Assert.Contains("thread of comment #0", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        using (var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false))
        {
            var comments = document.MainDocumentPart.WordprocessingCommentsPart.Comments.Elements<Comment>().ToList();
            var entries = document.MainDocumentPart.WordprocessingCommentsExPart.CommentsEx.Elements<CommentEx>().ToList();
            var root = ParagraphIdOf(comments[0]);

            Assert.Equal(root, entries.Single(entry => entry.ParaId.Value == ParagraphIdOf(comments[1])).ParaIdParent.Value);
            Assert.Equal(root, entries.Single(entry => entry.ParaId.Value == ParagraphIdOf(comments[2])).ParaIdParent.Value);
        }

        var list = await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "list" });

        Assert.Contains("1 comment thread(s)", list, StringComparison.Ordinal);
        Assert.Contains("reply #2", list, StringComparison.Ordinal);
        WordAuthoringToolsTests.AssertValid(bytes);
    }

    [Fact]
    public async Task Resolve_AReply_ResolvesItsThread()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);
        var body = await IdOfAsync(host, "Alpha body text.");

        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "add", id = body, comment = "Is this right?" });
        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "reply", comment_id = "0", comment = "Yes." });

        var before = await host.InvokeAsync(new CheckWordDocumentTool(), new { document = "doc", checks = new[] { "references" } });
        var answer = await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "resolve", comment_id = "1" });
        var list = await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "list" });
        var after = await host.InvokeAsync(new CheckWordDocumentTool(), new { document = "doc", checks = new[] { "references" } });

        Assert.Contains("1 open comment thread(s)", before, StringComparison.Ordinal);
        Assert.Contains("thread of comment #0", answer, StringComparison.Ordinal);
        Assert.Contains("#0 by", list, StringComparison.Ordinal);
        Assert.Contains("[resolved]", list, StringComparison.Ordinal);
        Assert.DoesNotContain("[open]", list, StringComparison.Ordinal);
        Assert.DoesNotContain("comment thread", after, StringComparison.Ordinal);
        WordAuthoringToolsTests.AssertValid(await host.ReadWorkingDocumentAsync("doc"));
    }

    [Fact]
    public async Task Delete_AReplyWithANestedReply_RemovesBothAndListShowsTheNesting()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);
        var body = await IdOfAsync(host, "Alpha body text.");

        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "add", id = body, comment = "Root." });
        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "reply", comment_id = "0", comment = "First reply." });
        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "reply", comment_id = "0", comment = "Second reply." });

        // Another editor may have written the second reply as an answer to the first.
        byte[] nested;

        using (var package = WordPackage.Open(await host.ReadWorkingDocumentAsync("doc")))
        {
            var comments = package.MainPart.WordprocessingCommentsPart.Comments.Elements<Comment>().ToList();

            package.MainPart.WordprocessingCommentsExPart.CommentsEx.Elements<CommentEx>().Single(entry => entry.ParaId.Value == ParagraphIdOf(comments[2])).ParaIdParent = ParagraphIdOf(comments[1]);
            nested = package.Save();
        }

        await host.UploadAsync("nested.docx", nested);

        var list = await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "nested.docx", action = "list" });
        var answer = await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "nested.docx", action = "delete", comment_id = "1" });

        Assert.Contains("1 comment thread(s)", list, StringComparison.Ordinal);
        Assert.Contains("reply #2 (to #1)", list, StringComparison.Ordinal);
        Assert.Contains("Deleted 2", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("nested");

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var left = document.MainDocumentPart.WordprocessingCommentsPart.Comments.Elements<Comment>().ToList();

        Assert.Equal("0", Assert.Single(left).Id.Value);
        Assert.Equal(ParagraphIdOf(left[0]), Assert.Single(document.MainDocumentPart.WordprocessingCommentsExPart.CommentsEx.Elements<CommentEx>()).ParaId.Value);
        Assert.All(document.MainDocumentPart.Document.Descendants<CommentReference>(), reference => Assert.Equal("0", reference.Id.Value));
        WordAuthoringToolsTests.AssertValid(bytes);
    }

    [Fact]
    public async Task AddAndDelete_WhenTheFileKeepsDurableIds_KeepTheDurableIdsAndPeopleInStep()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);

        byte[] prepared;

        using (var package = WordPackage.Open(await host.ReadWorkingDocumentAsync("doc")))
        {
            package.MainPart.AddNewPart<WordprocessingCommentsIdsPart>().CommentsIds = new CommentsIds();
            package.MainPart.AddNewPart<WordCommentsExtensiblePart>().CommentsExtensible = new CommentsExtensible();
            package.MainPart.AddNewPart<WordprocessingPeoplePart>().People = new People();
            prepared = package.Save();
        }

        await host.UploadAsync("durable.docx", prepared);

        var body = await IdOfAsync(host, "Alpha body text.", prepared);

        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "durable.docx", action = "add", id = body, comment = "Check." });

        var added = await host.ReadWorkingDocumentAsync("durable");

        using (var document = WordprocessingDocument.Open(new MemoryStream(added), isEditable: false))
        {
            var main = document.MainDocumentPart;
            var comment = main.WordprocessingCommentsPart.Comments.Elements<Comment>().Single();
            var commentId = Assert.Single(main.WordprocessingCommentsIdsPart.CommentsIds.Elements<CommentId>());

            Assert.Equal(ParagraphIdOf(comment), commentId.ParaId.Value);
            Assert.Equal(commentId.DurableId.Value, Assert.Single(main.WordCommentsExtensiblePart.CommentsExtensible.Elements<CommentExtensible>()).DurableId.Value);
            Assert.Equal(comment.Author.Value, Assert.Single(main.WordprocessingPeoplePart.People.Elements<Person>()).Author.Value);
        }

        WordAuthoringToolsTests.AssertValid(added);

        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "durable", action = "delete", comment_id = "all" });

        var deleted = await host.ReadWorkingDocumentAsync("durable");

        using (var document = WordprocessingDocument.Open(new MemoryStream(deleted), isEditable: false))
        {
            Assert.Empty(document.MainDocumentPart.WordprocessingCommentsIdsPart.CommentsIds.Elements<CommentId>());
            Assert.Empty(document.MainDocumentPart.WordCommentsExtensiblePart.CommentsExtensible.Elements<CommentExtensible>());
        }

        WordAuthoringToolsTests.AssertValid(deleted);
    }

    [Fact]
    public async Task Accept_TableRowRevisions_KeepsTheInsertedRowAndRemovesTheDeletedOne()
    {
        using var host = new WordToolTestHost();
        await host.UploadAsync("rows.docx", RowRevisionsDocument());

        var list = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "rows.docx", action = "list" });
        var check = await host.InvokeAsync(new CheckWordDocumentTool(), new { document = "rows.docx", checks = new[] { "references" } });
        var answer = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "rows.docx", action = "accept" });

        Assert.Contains("3 tracked change(s)", list, StringComparison.Ordinal);
        Assert.Contains("#2 inserted table row by Reviewer", list, StringComparison.Ordinal);
        Assert.Contains("#3 deleted table row by Reviewer", list, StringComparison.Ordinal);
        Assert.Contains("3 tracked change(s) not yet accepted", check, StringComparison.Ordinal);
        Assert.Contains("Accepted 3 tracked change(s); 0 remain.", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("rows");

        Assert.Equal(["Keep", "Added"], RowTexts(bytes));
        AssertNoRevisions(bytes);
        WordAuthoringToolsTests.AssertValid(bytes);
    }

    [Fact]
    public async Task Reject_TableRowRevisions_RemovesTheInsertedRowAndRestoresTheDeletedOne()
    {
        using var host = new WordToolTestHost();
        await host.UploadAsync("rows.docx", RowRevisionsDocument());

        var answer = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "rows.docx", action = "reject" });

        Assert.Contains("Rejected 3 tracked change(s); 0 remain.", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("rows");

        Assert.Equal(["Keep", "Gone"], RowTexts(bytes));
        AssertNoRevisions(bytes);
        WordAuthoringToolsTests.AssertValid(bytes);
    }

    [Fact]
    public async Task Reject_TheOnlyRowOfATableAsInserted_RemovesTheTable()
    {
        using var host = new WordToolTestHost();
        await host.UploadAsync("table.docx", Build(
            Paragraph("Before") +
            Table(Row($"<w:trPr><w:ins w:id=\"1\" {Stamp}/></w:trPr>", "Added")) +
            Paragraph("After")));

        await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "table.docx", action = "reject" });

        var bytes = await host.ReadWorkingDocumentAsync("table");

        using (var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false))
        {
            Assert.Empty(document.MainDocumentPart.Document.Body.Descendants<Table>());
            Assert.Equal("Before\nAfter", WordText.Of(document.MainDocumentPart.Document.Body));
        }

        WordAuthoringToolsTests.AssertValid(bytes);
    }

    [Fact]
    public async Task Reject_PropertyChanges_RestoresTheOldParagraphTableRowCellAndPageSetup()
    {
        using var host = new WordToolTestHost();
        await host.UploadAsync("formats.docx", PropertyChangesDocument());

        var list = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "formats.docx", action = "list" });
        var answer = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "formats.docx", action = "reject" });

        Assert.Contains("7 tracked change(s)", list, StringComparison.Ordinal);
        Assert.Contains("page setup change", list, StringComparison.Ordinal);
        Assert.Contains("Rejected 7 tracked change(s); 0 remain.", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("formats");

        using (var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false))
        {
            var body = document.MainDocumentPart.Document.Body;
            var paragraph = body.Elements<Paragraph>().First();
            var table = body.Elements<Table>().Single();
            var cell = table.Descendants<TableCell>().Single();
            var section = body.Elements<SectionProperties>().Single();

            Assert.Equal(JustificationValues.Left, paragraph.ParagraphProperties.Justification.Val.Value);
            Assert.Null(paragraph.ParagraphProperties.ParagraphMarkRunProperties.GetFirstChild<Bold>());
            Assert.Null(table.GetFirstChild<TableProperties>().TableJustification);
            Assert.Equal("2000", table.GetFirstChild<TableGrid>().Elements<GridColumn>().Single().Width.Value);
            Assert.Null(table.Descendants<TableRowProperties>().Single().GetFirstChild<CantSplit>());
            Assert.Equal("2000", cell.TableCellProperties.TableCellWidth.Width.Value);
            Assert.Null(cell.TableCellProperties.Shading);
            Assert.Equal(11906U, section.GetFirstChild<PageSize>().Width.Value);
        }

        WordAuthoringToolsTests.AssertValid(bytes);
    }

    [Theory]
    [InlineData("accept", "Middle\nMoved")]
    [InlineData("reject", "Moved\nMiddle")]
    public async Task AcceptOrReject_AMove_LeavesTheTextInOnePlaceWithoutRangeMarkers(string action, string expected)
    {
        using var host = new WordToolTestHost();
        await host.UploadAsync("move.docx", Build(
            $"<w:moveFromRangeStart w:id=\"20\" w:name=\"move1\" {Stamp}/>" +
            $"<w:p><w:pPr><w:rPr><w:moveFrom w:id=\"21\" {Stamp}/></w:rPr></w:pPr><w:moveFrom w:id=\"22\" {Stamp}><w:r><w:t>Moved</w:t></w:r></w:moveFrom></w:p>" +
            "<w:moveFromRangeEnd w:id=\"20\"/>" +
            Paragraph("Middle") +
            $"<w:moveToRangeStart w:id=\"23\" w:name=\"move1\" {Stamp}/>" +
            $"<w:p><w:pPr><w:rPr><w:moveTo w:id=\"24\" {Stamp}/></w:rPr></w:pPr><w:moveTo w:id=\"25\" {Stamp}><w:r><w:t>Moved</w:t></w:r></w:moveTo></w:p>" +
            "<w:moveToRangeEnd w:id=\"23\"/>"));

        var answer = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "move.docx", action });

        Assert.Contains("4 tracked change(s); 0 remain.", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("move");

        using (var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false))
        {
            var body = document.MainDocumentPart.Document.Body;

            Assert.Equal(expected, WordText.Of(body));
            Assert.DoesNotContain(body.Descendants(), element => element is MoveFromRangeStart or MoveFromRangeEnd or MoveToRangeStart or MoveToRangeEnd);
        }

        WordAuthoringToolsTests.AssertValid(bytes);
    }

    [Fact]
    public async Task Accept_WithEmptyRevisionIds_ChangesNothing()
    {
        using var host = new WordToolTestHost();
        await host.UploadAsync("rows.docx", RowRevisionsDocument());

        var empty = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "rows.docx", action = "accept", revision_ids = Array.Empty<string>() });
        var list = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "rows.docx", action = "list" });
        var one = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "rows.docx", action = "accept", revision_ids = new[] { "2" } });

        Assert.Contains("'revision_ids' is empty", empty, StringComparison.Ordinal);
        Assert.Contains("3 tracked change(s)", list, StringComparison.Ordinal);
        Assert.Contains("Accepted 1 tracked change(s); 2 remain.", one, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_WithAll_RunsEveryCheck()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);

        var all = await host.InvokeAsync(new CheckWordDocumentTool(), new { document = "doc", checks = new[] { "all" } });
        var unspecified = await host.InvokeAsync(new CheckWordDocumentTool(), new { document = "doc" });

        Assert.Contains("no title property", all, StringComparison.Ordinal);
        Assert.Equal(unspecified, all);
    }

    [Fact]
    public async Task Check_WithAnUnknownCheck_FailsAndNamesTheValidOnes()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);

        var answer = await host.InvokeAsync(new CheckWordDocumentTool(), new { document = "doc", checks = new[] { "accessibility", "spelling" } });

        Assert.Contains("Unknown check(s): \"spelling\"", answer, StringComparison.Ordinal);
        Assert.Contains("\"references\"", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("no title property", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compare_TwoAdjacentEditedParagraphs_ReportsTwoChanges()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "paragraph", text = "Opening line." },
                new { type = "paragraph", text = "One alpha." },
                new { type = "paragraph", text = "Two beta." },
                new { type = "paragraph", text = "Closing line." },
            },
        });
        await host.UploadAsync("original.docx", await host.ReadWorkingDocumentAsync("doc"));
        await host.InvokeAsync(new UpdateWordContentTool(), new { document = "doc", find = "alpha", replace = "ALPHA" });
        await host.InvokeAsync(new UpdateWordContentTool(), new { document = "doc", find = "beta", replace = "BETA" });

        var compare = await host.InvokeAsync(new CompareWordDocumentsTool(), new { original = "original.docx", revised = "doc" });

        Assert.Contains("0 added, 0 removed, 2 changed", compare, StringComparison.Ordinal);
        Assert.Contains("\"alpha.\" → \"ALPHA.\"", compare, StringComparison.Ordinal);
        Assert.Contains("\"beta.\" → \"BETA.\"", compare, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compare_TablesWhoseEmptyCellMoved_ReportsAChange()
    {
        using var host = new WordToolTestHost();
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "first",
            content = new object[] { new { type = "table", columns = new[] { "X", "Y", "Z" }, rows = new object[] { new object[] { "A", string.Empty, "C" } } } },
        });
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "second",
            content = new object[] { new { type = "table", columns = new[] { "X", "Y", "Z" }, rows = new object[] { new object[] { "A", "C", string.Empty } } } },
        });

        var compare = await host.InvokeAsync(new CompareWordDocumentsTool(), new { original = "first", revised = "second" });

        Assert.Contains("1 changed", compare, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractTables_AsCsv_KeepsNumbersEscapesFormulasAndSquaresMergedCells()
    {
        using var host = new WordToolTestHost();
        var nested = Table(Row(string.Empty, "n1", "n2"));

        await host.UploadAsync("grid.docx", Build(Table(
            "<w:tr>" + Cell("Wide", "<w:gridSpan w:val=\"2\"/>") + Cell("-5") + "</w:tr>",
            "<w:tr>" + Cell("=1+1") + Cell("+3.2") + Cell("Größe") + "</w:tr>",
            "<w:tr>" + Cell("M", "<w:vMerge w:val=\"restart\"/>") + Cell("x") + $"<w:tc><w:p><w:r><w:t>y</w:t></w:r></w:p>{nested}<w:p/></w:tc>" + "</w:tr>",
            "<w:tr>" + Cell(string.Empty, "<w:vMerge/>") + Cell("z") + "<w:sdt><w:sdtPr/><w:sdtContent>" + Cell("S") + "</w:sdtContent></w:sdt></w:tr>")));

        var answer = await host.InvokeAsync(new ExtractWordContentTool(), new { document = "grid.docx", what = "tables", save_as_file = true });
        var markdown = await host.InvokeAsync(new ExtractWordContentTool(), new { document = "grid.docx", what = "tables" });
        var (_, bytes) = await host.ReadMarkerAsync(MarkerPattern().Match(answer).Value);

        Assert.Equal(Encoding.UTF8.Preamble.ToArray(), bytes[..3]);

        var csv = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).ReplaceLineEndings("\n");

        Assert.Equal("Wide,,-5\n\"'=1+1\",+3.2,Größe\nM,x,y\n,z,S\n\nn1,n2", csv);
        Assert.Contains("| --- | --- | --- |", markdown, StringComparison.Ordinal);
        Assert.Contains("nested in table [", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accept_DeletedMarkEndingASection_MergesTheSectionsUnderTheFollowingOne()
    {
        using var host = new WordToolTestHost();
        await host.UploadAsync("sections.docx", Build(
            $"<w:p><w:pPr><w:rPr><w:del w:id=\"1\" {Stamp}/></w:rPr>" +
            "<w:sectPr><w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"landscape\"/><w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>" +
            "</w:pPr><w:r><w:t>First</w:t></w:r></w:p>" +
            Paragraph("Second")));

        var answer = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "sections.docx", action = "accept" });

        Assert.Contains("Accepted 1 tracked change(s); 0 remain.", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("sections");

        using (var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false))
        {
            var body = document.MainDocumentPart.Document.Body;
            var section = Assert.Single(body.Descendants<SectionProperties>());

            Assert.Same(body, section.Parent);
            Assert.Equal(12240U, section.GetFirstChild<PageSize>().Width.Value);
            Assert.Equal("FirstSecond", WordText.Of(body));
        }

        WordAuthoringToolsTests.AssertValid(bytes);
    }

    [Fact]
    public async Task Accept_OneHalfOfAMove_AppliesBothHalvesAndLeavesOtherMoves()
    {
        using var host = new WordToolTestHost();
        await host.UploadAsync("moves.docx", Build(
            "<w:p>" +
            $"<w:moveFromRangeStart w:id=\"30\" w:name=\"move1\" {Stamp}/><w:moveFrom w:id=\"31\" {Stamp}><w:r><w:t>One</w:t></w:r></w:moveFrom><w:moveFromRangeEnd w:id=\"30\"/>" +
            "<w:r><w:t xml:space=\"preserve\"> stays </w:t></w:r>" +
            $"<w:moveFromRangeStart w:id=\"32\" w:name=\"move2\" {Stamp}/><w:moveFrom w:id=\"33\" {Stamp}><w:r><w:t>Two</w:t></w:r></w:moveFrom><w:moveFromRangeEnd w:id=\"32\"/>" +
            "</w:p>" +
            "<w:p>" +
            $"<w:moveToRangeStart w:id=\"34\" w:name=\"move2\" {Stamp}/><w:moveTo w:id=\"35\" {Stamp}><w:r><w:t>Two</w:t></w:r></w:moveTo><w:moveToRangeEnd w:id=\"34\"/>" +
            $"<w:moveToRangeStart w:id=\"36\" w:name=\"move1\" {Stamp}/><w:moveTo w:id=\"37\" {Stamp}><w:r><w:t>One</w:t></w:r></w:moveTo><w:moveToRangeEnd w:id=\"36\"/>" +
            "</w:p>"));

        var list = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "moves.docx", action = "list" });
        var answer = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "moves.docx", action = "accept", revision_ids = new[] { "31" } });

        Assert.Contains("#31 moved away by Reviewer, 2024-01-01 00:00 (one move with #37)", list, StringComparison.Ordinal);
        Assert.Contains("#35 moved here by Reviewer, 2024-01-01 00:00 (one move with #33)", list, StringComparison.Ordinal);
        Assert.Contains("Accepted 2 tracked change(s), 1 of them the other half of a move; 2 remain.", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("moves");

        using (var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false))
        {
            var body = document.MainDocumentPart.Document.Body;

            Assert.Equal(["33", "35"], body.Descendants().Where(element => element is MoveFromRun or MoveToRun).Select(element => element.GetAttribute("id", W).Value));
            Assert.DoesNotContain(body.Descendants<MoveToRangeStart>(), start => start.Name.Value == "move1");
            Assert.Equal("One", WordText.Of(body.Elements<Paragraph>().Last().Elements<Run>().Single()));
        }

        WordAuthoringToolsTests.AssertValid(bytes);
    }

    [Theory]
    [InlineData("accept", true)]
    [InlineData("reject", false)]
    public async Task AcceptOrReject_NumberingAdded_KeepsOrRemovesTheNumbering(string action, bool numbered)
    {
        using var host = new WordToolTestHost();
        await host.UploadAsync("numbering.docx", Build(
            $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/><w:ins w:id=\"5\" {Stamp}/></w:numPr></w:pPr><w:r><w:t>Item</w:t></w:r></w:p>"));

        var list = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "numbering.docx", action = "list" });
        var answer = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "numbering.docx", action });

        Assert.Contains("#5 numbering added by Reviewer", list, StringComparison.Ordinal);
        Assert.Contains("1 tracked change(s); 0 remain.", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("numbering");

        using (var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false))
        {
            var numbering = document.MainDocumentPart.Document.Body.Descendants<NumberingProperties>().SingleOrDefault();

            Assert.Equal(numbered, numbering is not null);
            Assert.Empty(document.MainDocumentPart.Document.Body.Descendants<Inserted>());
        }

        WordAuthoringToolsTests.AssertValid(bytes);
    }

    [Fact]
    public async Task Accept_DeletionHoldingACommentReference_RemovesTheCommentAndItsReplies()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);
        var body = await IdOfAsync(host, "Alpha body text.");

        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "add", id = body, text = "body", comment = "Is this right?" });
        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "reply", comment_id = "0", comment = "Yes." });

        // The commented text and the comment's reference are deleted as one tracked change.
        byte[] deleted;

        using (var package = WordPackage.Open(await host.ReadWorkingDocumentAsync("doc")))
        {
            var reference = package.Body.Descendants<CommentReference>().Single(item => item.Id.Value == "0").Parent;
            var deletion = new DeletedRun { Id = "50", Author = "Reviewer", Date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

            reference.InsertBeforeSelf(deletion);
            reference.Remove();
            deletion.Append(reference);
            deleted = package.Save();
        }

        await host.UploadAsync("deleted.docx", deleted);

        var answer = await host.InvokeAsync(new ManageWordRevisionsTool(), new { document = "deleted.docx", action = "accept" });

        Assert.Contains("Removed 2 comment(s) and reply(ies) whose anchor went with the removed text.", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("deleted");

        using (var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false))
        {
            var main = document.MainDocumentPart;

            Assert.Empty(main.WordprocessingCommentsPart.Comments.Elements<Comment>());
            Assert.Empty(main.WordprocessingCommentsExPart.CommentsEx.Elements<CommentEx>());
            Assert.DoesNotContain(main.Document.Body.Descendants(), element => element is CommentRangeStart or CommentRangeEnd or CommentReference);
        }

        WordAuthoringToolsTests.AssertValid(bytes);
    }

    [Fact]
    public async Task Delete_CommentWhoseReferenceIsTheOnlyTrackedInsertion_RemovesTheEmptyInsertion()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);
        var body = await IdOfAsync(host, "Alpha body text.");

        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "add", id = body, comment = "Check." });

        byte[] inserted;

        using (var package = WordPackage.Open(await host.ReadWorkingDocumentAsync("doc")))
        {
            var reference = package.Body.Descendants<CommentReference>().Single().Parent;
            var insertion = new InsertedRun { Id = "50", Author = "Reviewer", Date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

            reference.InsertBeforeSelf(insertion);
            reference.Remove();
            insertion.Append(reference);
            inserted = package.Save();
        }

        await host.UploadAsync("inserted.docx", inserted);
        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "inserted.docx", action = "delete", comment_id = "0" });

        var bytes = await host.ReadWorkingDocumentAsync("inserted");

        using (var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false))
        {
            Assert.Empty(document.MainDocumentPart.Document.Body.Descendants<InsertedRun>());
        }

        WordAuthoringToolsTests.AssertValid(bytes);
    }

    [Fact]
    public async Task List_CommentSharingItsParagraphIdWithTheBody_KeepsItsResolvedState()
    {
        using var host = new WordToolTestHost();
        await CreateAsync(host);
        var body = await IdOfAsync(host, "Alpha body text.");

        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "add", id = body, comment = "Check." });
        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "doc", action = "resolve", comment_id = "0" });

        // Another editor wrote the comment's paragraph id on a body paragraph too.
        byte[] shared;

        using (var package = WordPackage.Open(await host.ReadWorkingDocumentAsync("doc")))
        {
            var comment = package.MainPart.WordprocessingCommentsPart.Comments.Elements<Comment>().Single();

            package.Body.Elements<Paragraph>().First().ParagraphId = ParagraphIdOf(comment);
            shared = package.Save();
        }

        await host.UploadAsync("shared.docx", shared);

        // An edit gives the paragraphs that share an id new ones; the comment's is the one kept.
        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "shared.docx", action = "reply", comment_id = "0", comment = "Done." });

        var list = await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "shared", action = "list" });

        Assert.Contains("[resolved]", list, StringComparison.Ordinal);
        Assert.Contains("reply #1", list, StringComparison.Ordinal);
        WordAuthoringToolsTests.AssertValid(await host.ReadWorkingDocumentAsync("shared"));
    }

    private static async Task CreateAsync(WordToolTestHost host)
    {
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "heading", text = "Alpha", level = 1 },
                new { type = "paragraph", text = "Alpha body text." },
                new { type = "paragraph", text = "Beta body." },
            },
        });
    }

    private static async Task<string> IdOfAsync(WordToolTestHost host, string text, byte[] bytes = null)
    {
        using var package = WordPackage.Open(bytes ?? await host.ReadWorkingDocumentAsync("doc"));

        return WordBlockReader.Read(package).First(block => block.Text == text).Id;
    }

    private static string ParagraphIdOf(Comment comment)
    {
        return comment.Elements<Paragraph>().Last().ParagraphId.Value;
    }

    // A table whose second row was inserted and third row deleted, with its text, as Word tracks them.
    private static byte[] RowRevisionsDocument()
    {
        return Build(
            Paragraph("Before") +
            Table(
                Row(string.Empty, "Keep"),
                Row($"<w:trPr><w:ins w:id=\"2\" {Stamp}/></w:trPr>", "Added"),
                $"<w:tr><w:trPr><w:del w:id=\"3\" {Stamp}/></w:trPr><w:tc><w:p><w:del w:id=\"4\" {Stamp}><w:r><w:delText>Gone</w:delText></w:r></w:del></w:p></w:tc></w:tr>") +
            Paragraph("After"));
    }

    // A paragraph, a table, a row, a cell and the page setup, each with a formatting change.
    private static byte[] PropertyChangesDocument()
    {
        return Build(
            "<w:p><w:pPr><w:jc w:val=\"center\"/>" +
            $"<w:rPr><w:b/><w:rPrChange w:id=\"10\" {Stamp}><w:rPr/></w:rPrChange></w:rPr>" +
            $"<w:pPrChange w:id=\"11\" {Stamp}><w:pPr><w:jc w:val=\"left\"/></w:pPr></w:pPrChange></w:pPr>" +
            "<w:r><w:t>Centered</w:t></w:r></w:p>" +
            "<w:tbl>" +
            $"<w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/><w:jc w:val=\"center\"/><w:tblPrChange w:id=\"12\" {Stamp}><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr></w:tblPrChange></w:tblPr>" +
            "<w:tblGrid><w:gridCol w:w=\"4000\"/><w:tblGridChange w:id=\"13\"><w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid></w:tblGridChange></w:tblGrid>" +
            $"<w:tr><w:trPr><w:cantSplit/><w:trPrChange w:id=\"14\" {Stamp}><w:trPr/></w:trPrChange></w:trPr>" +
            "<w:tc><w:tcPr><w:tcW w:w=\"4000\" w:type=\"dxa\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"FF0000\"/>" +
            $"<w:tcPrChange w:id=\"15\" {Stamp}><w:tcPr><w:tcW w:w=\"2000\" w:type=\"dxa\"/></w:tcPr></w:tcPrChange></w:tcPr>" +
            "<w:p><w:r><w:t>Cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
            Paragraph("After"),
            "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/>" +
            $"<w:sectPrChange w:id=\"16\" {Stamp}><w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:sectPrChange></w:sectPr>");
    }

    private static List<string> RowTexts(byte[] bytes)
    {
        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);

        return [.. document.MainDocumentPart.Document.Body.Descendants<TableRow>().Select(row => WordText.Of(row))];
    }

    private static void AssertNoRevisions(byte[] bytes)
    {
        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var body = document.MainDocumentPart.Document.Body;

        Assert.Empty(body.Descendants<InsertedRun>());
        Assert.Empty(body.Descendants<DeletedRun>());
        Assert.DoesNotContain(body.Descendants<TableRowProperties>().SelectMany(properties => properties.ChildElements), child => child is Inserted or Deleted);
    }

    private static byte[] Build(string bodyXml, string sectionXml = null)
    {
        using var stream = new MemoryStream();

        sectionXml ??= "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>";

        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            document.AddMainDocumentPart().Document = new Document($"<w:document xmlns:w=\"{W}\"><w:body>{bodyXml}{sectionXml}</w:body></w:document>");
        }

        return stream.ToArray();
    }

    private static string Paragraph(string text)
    {
        return $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";
    }

    private static string Table(params string[] rows)
    {
        return $"<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/></w:tblGrid>{string.Concat(rows)}</w:tbl>";
    }

    private static string Row(string rowProperties, params string[] texts)
    {
        return $"<w:tr>{rowProperties}{string.Concat(texts.Select(text => Cell(text)))}</w:tr>";
    }

    private static string Cell(string text, string cellProperties = "")
    {
        var paragraph = text.Length == 0 ? "<w:p/>" : Paragraph(text);

        return $"<w:tc><w:tcPr><w:tcW w:w=\"2000\" w:type=\"dxa\"/>{cellProperties}</w:tcPr>{paragraph}</w:tc>";
    }

    [GeneratedRegex(@"\[doc:\d+\]")]
    private static partial Regex MarkerPattern();
}
