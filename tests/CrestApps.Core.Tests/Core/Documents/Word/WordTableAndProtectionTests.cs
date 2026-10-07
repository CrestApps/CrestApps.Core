using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Tools;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Documents.Word;

public sealed class WordTableAndProtectionTests
{
    [Fact]
    public async Task MergeCells_HorizontalAndVerticalRanges_MergesThemAndSplitsBack()
    {
        using var host = new WordToolTestHost();
        await CreateTableAsync(host);

        var answer = await host.InvokeAsync(new UpdateWordTableTool(), new
        {
            document = "doc",
            merge_cells = new object[]
            {
                new { row = "One", column = 2, to_column = 3 },
                new { row = "Two", column = 3, to_row = "Three" },
            },
        });

        Assert.Contains("merged 2 range(s)", answer, StringComparison.Ordinal);

        var merged = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(merged);

        using (var document = Open(merged))
        {
            var rows = Table(document).Elements<TableRow>().ToList();
            var one = rows[1].Elements<TableCell>().ToList();

            Assert.Equal(2, one.Count);
            Assert.Equal(2, one[1].TableCellProperties.GridSpan.Val.Value);
            Assert.Equal(["a", "b"], one[1].Elements<Paragraph>().Select(paragraph => WordText.Of(paragraph)).ToList());

            var restart = rows[2].Elements<TableCell>().Last();
            var continuation = rows[3].Elements<TableCell>().Last();

            Assert.Equal(MergedCellValues.Restart, restart.TableCellProperties.VerticalMerge.Val.Value);
            Assert.NotNull(continuation.TableCellProperties.VerticalMerge);
            Assert.Null(continuation.TableCellProperties.VerticalMerge.Val);
            Assert.Equal(["d", "f"], restart.Elements<Paragraph>().Select(paragraph => WordText.Of(paragraph)).ToList());
            Assert.Equal(string.Empty, WordText.OfCell(continuation));
        }

        // Each merged cell is named by a row and column it covers; the vertical one by its lower row.
        await host.InvokeAsync(new UpdateWordTableTool(), new
        {
            document = "doc",
            split_cells = new object[]
            {
                new { row = "One", column = 3 },
                new { row = 4, column = 3 },
            },
        });

        var split = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(split);

        using (var document = Open(split))
        {
            var table = Table(document);

            foreach (var row in table.Elements<TableRow>())
            {
                Assert.Equal(3, row.Elements<TableCell>().Count());
                Assert.All(row.Elements<TableCell>(), cell => Assert.Null(cell.TableCellProperties?.GridSpan));
                Assert.All(row.Elements<TableCell>(), cell => Assert.Null(cell.TableCellProperties?.VerticalMerge));
            }

            Assert.Equal(["One", "ab", string.Empty], Texts(table)[1]);
            Assert.Equal(["Three", "e", string.Empty], Texts(table)[3]);
        }
    }

    [Fact]
    public async Task SplitCells_UnmergedCell_AddsGridColumnsAndWidensTheOtherRows()
    {
        using var host = new WordToolTestHost();
        await CreateTableAsync(host);

        await host.InvokeAsync(new UpdateWordTableTool(), new { document = "doc", split_cells = new[] { new { row = "Two", column = 2, columns = 3 } } });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var document = Open(bytes);
        var table = Table(document);

        Assert.Equal(5, table.GetFirstChild<TableGrid>().Elements<GridColumn>().Count());
        Assert.All(table.Elements<TableRow>(), row => Assert.Equal(5, row.Elements<TableCell>().Sum(cell => cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1)));
        Assert.Equal(["Two", "c", string.Empty, string.Empty, "d"], Texts(table)[2]);
    }

    [Fact]
    public async Task Cells_RowWithMergedCells_SetsTheCellUnderTheColumn()
    {
        using var host = new WordToolTestHost();
        await CreateTableAsync(host);

        await host.InvokeAsync(new UpdateWordTableTool(), new
        {
            document = "doc",
            merge_cells = new object[]
            {
                new { row = 2, column = 1, to_column = 2 },
                new { row = 3, column = 3, to_row = 4 },
            },
        });

        // Column 3 of row 2 is its second cell; column 3 of row 4 continues the merged cell that starts in row 3.
        var answer = await host.InvokeAsync(new UpdateWordTableTool(), new
        {
            document = "doc",
            cells = new object[]
            {
                new { row = 2, column = 3, text = "B3" },
                new { row = 4, column = 3, text = "Merged" },
            },
        });

        Assert.Contains("set 2 cell(s)", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var document = Open(bytes);
        var rows = Texts(Table(document));

        Assert.Equal(["Onea", "B3"], rows[1]);
        Assert.Equal(["Two", "c", "Merged"], rows[2]);
        Assert.Equal(["Three", "e", string.Empty], rows[3]);
    }

    [Fact]
    public async Task AddAndRemoveColumn_TableWithGridSpan_KeepsEveryRowOnTheGrid()
    {
        using var host = new WordToolTestHost();
        await CreateTableAsync(host);

        await host.InvokeAsync(new UpdateWordTableTool(), new { document = "doc", merge_cells = new[] { new { row = 2, column = 1, to_column = 2 } } });

        var added = await host.InvokeAsync(new UpdateWordTableTool(), new
        {
            document = "doc",
            add_column = new { header = "New", values = new[] { "x", "y", "z" }, after_column = 1 },
        });

        Assert.Contains("× 4 columns", added, StringComparison.Ordinal);

        var widened = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(widened);

        using (var document = Open(widened))
        {
            var table = Table(document);

            AssertOnGrid(table, 4);
            Assert.Equal(["A", "New", "B", "C"], Texts(table)[0]);

            // The new column falls inside the merged cell of row 2, which widens instead of getting a cell.
            Assert.Equal(3, table.Elements<TableRow>().ElementAt(1).Elements<TableCell>().First().TableCellProperties.GridSpan.Val.Value);
            Assert.Equal(["Two", "y", "c", "d"], Texts(table)[2]);
        }

        await host.InvokeAsync(new UpdateWordTableTool(), new { document = "doc", remove_columns = new[] { 2 } });

        var narrowed = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(narrowed);

        using (var document = Open(narrowed))
        {
            var table = Table(document);

            AssertOnGrid(table, 3);
            Assert.Equal(["A", "B", "C"], Texts(table)[0]);
            Assert.Equal(2, table.Elements<TableRow>().ElementAt(1).Elements<TableCell>().First().TableCellProperties.GridSpan.Val.Value);
            Assert.Equal(["Three", "e", "f"], Texts(table)[3]);
        }
    }

    [Fact]
    public async Task FormatCells_RangeWithStyleAndWidths_AppliesTheLook()
    {
        using var host = new WordToolTestHost();
        await CreateTableAsync(host);

        await host.InvokeAsync(new UpdateWordTableTool(), new
        {
            document = "doc",
            format_cells = new[] { new { row = 1, column = 1, to_column = 3, fill = "#1F4E79", alignment = "center", vertical_alignment = "center" } },
            column_widths = new object[] { 2, 1, 1 },
            table_style = "light",
            banded = false,
        });

        var bytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(bytes);

        using var document = Open(bytes);
        var table = Table(document);
        var header = table.Elements<TableRow>().First().Elements<TableCell>().ToList();
        var widths = table.GetFirstChild<TableGrid>().Elements<GridColumn>().Select(column => int.Parse(column.Width.Value)).ToList();

        Assert.All(header, cell => Assert.Equal("1F4E79", cell.TableCellProperties.Shading.Fill.Value));
        Assert.All(header, cell => Assert.Equal(JustificationValues.Center, cell.Elements<Paragraph>().First().ParagraphProperties.Justification.Val.Value));
        Assert.InRange(widths[0], widths[1] * 2 - 4, widths[1] * 2 + 4);
        Assert.Equal(BorderValues.Nil, table.GetFirstChild<TableProperties>().TableBorders.InsideVerticalBorder.Val.Value);
        Assert.Equal(0x0200, Convert.ToInt32(table.GetFirstChild<TableProperties>().TableLook.Val.Value, 16) & 0x0200);
    }

    [Fact]
    public void LegacyKey_SpecificationExample_MatchesTheDocumentedKey()
    {
        // ISO/IEC 29500-1 §17.15.1.29 works the password "Example" through to the key 0x64CEED7E.
        Assert.Equal(0x64CEED7Eu, WordProtectionHash.LegacyKey("Example"));
        Assert.Equal(0u, WordProtectionHash.LegacyKey(string.Empty));
    }

    [Fact]
    public void Hash_SaltAndSpinCount_HashesTheReversedLegacyKeyAsUnicodeHex()
    {
        var salt = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();

        // The key 0x64CEED7E in reversed byte order is the text 7EEDCE64, hashed in UTF-16LE after the salt.
        var expected = SHA512.HashData([.. salt, .. Encoding.Unicode.GetBytes("7EEDCE64")]);

        for (var iteration = 0; iteration < 3; iteration++)
        {
            var counter = new byte[4];

            BinaryPrimitives.WriteInt32LittleEndian(counter, iteration);
            expected = SHA512.HashData([.. expected, .. counter]);
        }

        Assert.Equal(expected, WordProtectionHash.Hash("Example", salt, spinCount: 3));
    }

    [Fact]
    public async Task ManageWordProtection_SetWithPasswordThenRemove_WritesHashWithoutEchoingThePassword()
    {
        using var host = new WordToolTestHost();
        await CreateTableAsync(host);

        const string password = "Sup3r-Secret";

        var answer = await host.InvokeAsync(new ManageWordProtectionTool(), new { document = "doc", action = "set", restriction = "comments", password });

        Assert.Contains("only comments can be added", answer, StringComparison.Ordinal);
        Assert.DoesNotContain(password, answer, StringComparison.Ordinal);

        var protectedBytes = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(protectedBytes);

        using (var document = Open(protectedBytes))
        {
            var protection = document.MainDocumentPart.DocumentSettingsPart.Settings.GetFirstChild<DocumentProtection>();

            Assert.Equal(DocumentProtectionValues.Comments, protection.Edit.Value);
            Assert.True(protection.Enforcement.Value);
            Assert.Equal(14, protection.CryptographicAlgorithmSid.Value);
            Assert.Equal(100_000u, protection.CryptographicSpinCount.Value);
            Assert.Equal(Convert.ToBase64String(WordProtectionHash.Hash(password, Convert.FromBase64String(protection.Salt.Value))), protection.Hash.Value);
            Assert.Contains("w:cryptProviderType=\"rsaAES\"", document.MainDocumentPart.DocumentSettingsPart.Settings.OuterXml, StringComparison.Ordinal);
            Assert.DoesNotContain(password, document.MainDocumentPart.DocumentSettingsPart.Settings.OuterXml, StringComparison.Ordinal);
        }

        var described = await host.InvokeAsync(new ManageWordProtectionTool(), new { document = "doc" });

        Assert.Contains("only comments can be added, enforced, with a password", described, StringComparison.Ordinal);
        Assert.DoesNotContain(password, described, StringComparison.Ordinal);

        await host.InvokeAsync(new ManageWordProtectionTool(), new { document = "doc", action = "remove" });

        var unprotected = await host.ReadWorkingDocumentAsync("doc");

        WordAuthoringToolsTests.AssertValid(unprotected);

        using (var document = Open(unprotected))
        {
            Assert.Null(document.MainDocumentPart.DocumentSettingsPart.Settings.GetFirstChild<DocumentProtection>());
        }
    }

    [Fact]
    public async Task ImportWord_WorkingDocument_DuplicatesItAsASeparateCopy()
    {
        using var host = new WordToolTestHost();
        await CreateTableAsync(host);

        var answer = await host.InvokeAsync(new ImportWordTool(), new { document = "doc", name = "doc-v2" });

        Assert.Contains("as working document \"doc-v2\"", answer, StringComparison.Ordinal);
        Assert.Equal(await host.ReadWorkingDocumentAsync("doc"), await host.ReadWorkingDocumentAsync("doc-v2"));

        await host.InvokeAsync(new UpdateWordTableTool(), new { document = "doc-v2", cells = new[] { new { row = "One", column = 2, text = "changed" } } });

        using (var original = Open(await host.ReadWorkingDocumentAsync("doc")))
        {
            Assert.Equal("a", Texts(Table(original))[1][1]);
        }

        using (var copy = Open(await host.ReadWorkingDocumentAsync("doc-v2")))
        {
            Assert.Equal("changed", Texts(Table(copy))[1][1]);
        }
    }

    [Fact]
    public async Task ImportWord_DuplicateAtTheWorkingDocumentLimit_IsRefused()
    {
        using var host = new WordToolTestHost(configure: services => services.Configure<WordAgentOptions>(options => options.MaxWorkingDocuments = 1));
        await CreateTableAsync(host);

        var answer = await host.InvokeAsync(new ImportWordTool(), new { document = "doc" });

        Assert.Contains("the most it keeps", answer, StringComparison.Ordinal);
        Assert.Single((await host.LoadWorkspaceAsync()).Documents);
    }

    [Fact]
    public async Task ExportWord_Heading_ExportsOnlyThatSection()
    {
        using var host = new WordToolTestHost();

        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "report",
            content = new object[]
            {
                new { type = "heading", text = "Alpha", level = 1 },
                new { type = "paragraph", text = "Alpha body." },
                new { type = "page_break" },
                new { type = "heading", text = "Beta", level = 1 },
                new { type = "heading", text = "Beta detail", level = 2 },
                new { type = "paragraph", text = "Beta body." },
                new { type = "heading", text = "Gamma", level = 1 },
                new { type = "paragraph", text = "Gamma body." },
            },
        });

        List<WordBlock> blocks;

        using (var package = WordPackage.Open(await host.ReadWorkingDocumentAsync("report")))
        {
            blocks = WordBlockReader.Read(package);
        }

        // A comment on content left out must not stay behind in the excerpt.
        await host.InvokeAsync(new ManageWordCommentsTool(), new { document = "report", action = "add", id = blocks.First(block => block.Text == "Alpha body.").Id, comment = "Check this." });

        var original = await host.ReadWorkingDocumentAsync("report");

        using (var document = Open(original))
        {
            Assert.Single(document.MainDocumentPart.WordprocessingCommentsPart.Comments.Elements<Comment>());
        }

        var answer = await host.InvokeAsync(new ExportWordTool(), new { document = "report", headings = new[] { blocks.First(block => block.Text == "Beta").Id } });
        var marker = Regex.Match(answer, @"\[doc:\d+\]").Value;

        Assert.False(string.IsNullOrEmpty(marker), answer);

        var (stored, bytes) = await host.ReadMarkerAsync(marker);

        Assert.Equal("report-excerpt.docx", stored.FileName);
        WordAuthoringToolsTests.AssertValid(bytes);

        using (var document = Open(bytes))
        {
            var text = WordText.Of(document.MainDocumentPart.Document.Body);

            Assert.Contains("Beta detail", text, StringComparison.Ordinal);
            Assert.Contains("Beta body.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Alpha", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Gamma", text, StringComparison.Ordinal);
            Assert.Empty(document.MainDocumentPart.WordprocessingCommentsPart?.Comments?.Elements<Comment>() ?? []);
            Assert.NotNull(document.MainDocumentPart.Document.Body.GetFirstChild<SectionProperties>());
        }

        Assert.Equal(original, await host.ReadWorkingDocumentAsync("report"));
    }

    private static async Task CreateTableAsync(WordToolTestHost host)
    {
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "doc",
            content = new object[]
            {
                new { type = "paragraph", text = "Before the table." },
                new
                {
                    type = "table",
                    columns = new[] { "A", "B", "C" },
                    rows = new object[]
                    {
                        new object[] { "One", "a", "b" },
                        new object[] { "Two", "c", "d" },
                        new object[] { "Three", "e", "f" },
                    },
                },
            },
        });
    }

    private static WordprocessingDocument Open(byte[] bytes)
    {
        return WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
    }

    private static Table Table(WordprocessingDocument document)
    {
        return document.MainDocumentPart.Document.Body.Descendants<Table>().Single();
    }

    private static List<List<string>> Texts(Table table)
    {
        return [.. table.Elements<TableRow>().Select(row => row.Elements<TableCell>().Select(cell => WordText.OfCell(cell).Replace("\n", string.Empty, StringComparison.Ordinal)).ToList())];
    }

    private static void AssertOnGrid(Table table, int columns)
    {
        Assert.Equal(columns, table.GetFirstChild<TableGrid>().Elements<GridColumn>().Count());

        foreach (var row in table.Elements<TableRow>())
        {
            Assert.Equal(columns, row.Elements<TableCell>().Sum(cell => cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1));
        }
    }
}
