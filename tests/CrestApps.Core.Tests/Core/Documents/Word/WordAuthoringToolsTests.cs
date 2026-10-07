using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Tools;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.Tests.Core.Documents.Word;

public sealed partial class WordAuthoringToolsTests
{
    [Fact]
    public async Task CreateWordDocument_WithEveryBlockType_WritesValidDocument()
    {
        using var host = new WordToolTestHost();

        var answer = await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "quarterly-report",
            title = "Quarterly Report",
            subtitle = "Q3 2026",
            theme = new { preset = "professional" },
            page_setup = new { size = "A4", margins = "2cm" },
            properties = new { author = "Finance", subject = "Results" },
            content = new object[]
            {
                new { type = "heading", text = "Overview", level = 1 },
                new { type = "paragraph", text = "Revenue grew **12%** against plan; see [the dashboard](https://example.com)." },
                new { type = "bullet_list", items = new object[] { "North", new { text = "South", items = new[] { "Coast", "Inland" } } } },
                new { type = "numbered_list", items = new[] { "Re-forecast", "Confirm headcount" } },
                new
                {
                    type = "table",
                    columns = new object[] { "Region", new { header = "Revenue", format = "currency" }, new { header = "Growth", format = "percent" } },
                    rows = new object[] { new object[] { "North", 1200.5, 0.12 }, new object[] { "South", 980, 0.04 } },
                    caption = "Revenue by region",
                },
                new { type = "quote", text = "Provisional until the close." },
                new { type = "code", text = "SELECT region\nFROM sales" },
                new { type = "chart", chart_type = "column", title = "Revenue", labels = new[] { "North", "South" }, series = new[] { new { name = "Revenue", values = new[] { 1200.5, 980 } } }, alt_text = "Revenue by region" },
                new { type = "page_break" },
                new { type = "heading", text = "Details", level = 2 },
                new { type = "markdown", text = "## Notes\n\n- First\n  - Nested\n\n| A | B |\n| --- | --- |\n| 1 | 2 |" },
            },
        });

        Assert.Contains("Created working document \"quarterly-report\"", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingDocumentAsync("quarterly-report");

        AssertValid(bytes);

        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var body = document.MainDocumentPart.Document.Body;

        Assert.Contains(body.Elements<Paragraph>(), paragraph => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Title");
        Assert.Contains(body.Elements<Paragraph>(), paragraph => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Heading1");
        Assert.Contains(body.Descendants<NumberingProperties>(), numbering => numbering.NumberingLevelReference?.Val?.Value == 1);
        var visible = WordText.Of(body);

        Assert.Contains("$1,200.50", visible, StringComparison.Ordinal);
        Assert.Contains("12.0%", visible, StringComparison.Ordinal);
        Assert.Contains("Table 1: Revenue by region", visible, StringComparison.Ordinal);
        Assert.Single(document.MainDocumentPart.ChartParts);
        Assert.NotNull(document.MainDocumentPart.ChartParts.Single().EmbeddedPackagePart);
        Assert.Equal("Finance", document.PackageProperties.Creator);
    }

    [Fact]
    public async Task PreviewWord_DrawsPagesAsPictures()
    {
        using var host = new WordToolTestHost();

        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "memo",
            title = "Memo",
            content = new object[]
            {
                new { type = "paragraph", text = "Hello from the preview." },
                new { type = "page_break" },
                new { type = "paragraph", text = "Second page." },
            },
        });

        var answer = await host.InvokeAsync(new PreviewWordTool(), new { document = "memo" });
        var markers = MarkerPattern().Matches(answer).Select(match => match.Value).Distinct().ToList();

        Assert.Equal(2, markers.Count);

        var (_, svg) = await host.ReadMarkerAsync(markers[0]);
        var text = Encoding.UTF8.GetString(svg);

        Assert.StartsWith("<svg", text, StringComparison.Ordinal);
        Assert.Contains("Hello", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportWord_ReturnsDownloadOfValidDocument()
    {
        using var host = new WordToolTestHost();

        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "letter",
            content = new object[] { new { type = "paragraph", text = "Dear reader," } },
        });

        var answer = await host.InvokeAsync(new ExportWordTool(), new { document = "letter" });
        var marker = Regex.Match(answer, @"\[doc:\d+\]").Value;

        Assert.False(string.IsNullOrEmpty(marker), answer);

        var reference = host.Invocation.ToolReferences[marker];
        var stored = await host.Documents.FindByIdAsync(reference.ReferenceId, TestContext.Current.CancellationToken);

        Assert.Equal("letter.docx", stored.FileName);
        AssertValid(await host.ReadFileAsync(stored.StoredFilePath));
    }

    [Fact]
    public async Task AddWordContent_ToUpload_SavesWorkingCopyAndLeavesUploadUnchanged()
    {
        using var host = new WordToolTestHost();

        var original = await WriteGeneratedAsync("# Plan\n\nThe original text.");
        var upload = await host.UploadAsync("plan.docx", original);

        var answer = await host.InvokeAsync(new AddWordContentTool(), new
        {
            document = "plan.docx",
            content = new object[] { new { type = "paragraph", text = "An added paragraph." } },
        });

        Assert.Contains("\"plan\"", answer, StringComparison.Ordinal);
        Assert.Equal(original, await host.ReadFileAsync(upload.StoredFilePath));

        var copy = await host.ReadWorkingDocumentAsync("plan");

        using var document = WordprocessingDocument.Open(new MemoryStream(copy), isEditable: false);

        Assert.Contains("The original text.", document.MainDocumentPart.Document.Body.InnerText, StringComparison.Ordinal);
        Assert.Contains("An added paragraph.", document.MainDocumentPart.Document.Body.InnerText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetWordDocument_ListsElementsWithIds()
    {
        using var host = new WordToolTestHost();

        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "outline",
            content = new object[]
            {
                new { type = "heading", text = "Introduction", level = 1 },
                new { type = "paragraph", text = "Body text." },
            },
        });

        var answer = await host.InvokeAsync(new GetWordDocumentTool(), new { document = "outline" });

        Assert.Matches(@"\[[0-9A-F]{8}\] Heading 1: ""Introduction""", answer);
        Assert.Matches(@"\[[0-9A-F]{8}\] Paragraph: ""Body text.""", answer);
    }

    /// <summary>
    /// Validates a document against the current Office schema and fails with every error found.
    /// </summary>
    /// <param name="bytes">The document.</param>
    internal static void AssertValid(byte[] bytes)
    {
        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), isEditable: false);
        var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(document).ToList();

        Assert.True(errors.Count == 0, "The document is not valid: " + string.Join(Environment.NewLine, errors.Take(20).Select(error => $"{error.Path?.XPath}: {error.Description}")));
    }

    /// <summary>
    /// Writes a document the way generate_file does.
    /// </summary>
    /// <param name="markdown">The body.</param>
    /// <returns>The document.</returns>
    internal static async Task<byte[]> WriteGeneratedAsync(string markdown)
    {
        using var stream = new MemoryStream();

        await new WordGeneratedFileWriter().WriteAsync(new GeneratedFileContent { Text = markdown }, stream, TestContext.Current.CancellationToken);

        return stream.ToArray();
    }

    [GeneratedRegex(@"\[fig:\d+\]")]
    private static partial Regex MarkerPattern();
}
