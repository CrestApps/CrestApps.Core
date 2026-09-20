using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Knowledge.Structure;
using CrestApps.Core.DataIngestion;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Ingestion.Structure;

/// <summary>
/// Covers dividing a document by the heading levels it states, whichever format stated them.
/// </summary>
/// <remarks>
/// A Word paragraph styled <c>Heading 2</c> and an <c>h2</c> are the same fact written two ways. One key
/// carries both, so one strategy divides both, and these tests hold that equivalence in place.
/// </remarks>
public sealed class StatedHeadingStructureTests
{
    private const string WordMediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    [Fact]
    public void Html_MarksHeadingsWithTheirLevel()
    {
        var document = HtmlIngestionDocumentReader.Read(
            "<html><body><h1>Guide</h1><p>Intro.</p><h2>Install</h2><p>Steps.</p></body></html>",
            "https://example.test/guide");

        var levels = document.Sections
            .SelectMany(section => section.Elements)
            .Select(GetHeadingLevel)
            .ToList();

        Assert.Equal([1, 0, 2, 0], levels);
    }

    [Fact]
    public void Html_DividesAPageByItsHeadings()
    {
        var structure = Analyze(HtmlIngestionDocumentReader.Read(
            "<html><body><h1>Guide</h1><p>Intro.</p><h2>Install</h2><p>Steps.</p><h2>Upgrade</h2><p>More.</p></body></html>",
            "https://example.test/guide"));

        Assert.Equal(["Guide", "Install", "Upgrade"], structure.Articles.Select(article => article.Title));
        Assert.Equal([1, 2, 2], structure.Articles.Select(article => article.Depth));

        var guide = structure.Articles[0];

        Assert.Equal(0, guide.ParentOrdinal);
        Assert.Equal(guide.Ordinal, structure.Articles[1].ParentOrdinal);
        Assert.Equal(guide.Ordinal, structure.Articles[2].ParentOrdinal);
    }

    [Fact]
    public void Html_KeepsEachDivisionsOwnTextApart()
    {
        // Every heading here is on one page. A page-bounded answer would merge all three and store the whole
        // page under whichever came last.
        var document = HtmlIngestionDocumentReader.Read(
            "<html><body><h1>Guide</h1><p>Intro.</p><h2>Install</h2><p>Steps.</p><h2>Upgrade</h2><p>More.</p></body></html>",
            "https://example.test/guide");

        var structure = Analyze(document);
        var elements = document.Sections.SelectMany(section => section.Elements).ToList();
        var ordinals = elements.Select(element => (int)element.Metadata[ElementMetadataKeys.ArticleOrdinal]).ToList();

        var guide = structure.Articles[0].Ordinal;
        var install = structure.Articles[1].Ordinal;
        var upgrade = structure.Articles[2].Ordinal;

        // Guide, Intro. | Install, Steps. | Upgrade, More.
        Assert.Equal([guide, guide, install, install, upgrade, upgrade], ordinals);
    }

    [Fact]
    public async Task Word_DividesByHeadingStyles()
    {
        var docx = CreateWord(
            ("Manual", "Heading1"),
            ("Welcome to the manual.", null),
            ("Getting started", "Heading2"),
            ("Plug it in.", null),
            ("Troubleshooting", "Heading2"),
            ("Turn it off and on.", null));

        var structure = Analyze(await ReadWordAsync(docx));

        Assert.Equal(
            ["Manual", "Getting started", "Troubleshooting"],
            structure.Articles.Select(article => article.Title));

        Assert.Equal([1, 2, 2], structure.Articles.Select(article => article.Depth));
    }

    [Fact]
    public async Task Word_ReadsTheOutlineLevelWhenAStyleIsCustom()
    {
        // A custom style built on Heading 2 carries the level without carrying the name. Reading only the
        // style identifier would treat this document as having no headings at all.
        var docx = CreateWord(
            ("House Style Title", "MyBigTitle", 0),
            ("Body.", null, -1),
            ("House Style Section", "MySection", 1),
            ("More body.", null, -1));

        var structure = Analyze(await ReadWordAsync(docx));

        Assert.Equal(["House Style Title", "House Style Section"], structure.Articles.Select(article => article.Title));
        Assert.Equal([1, 2], structure.Articles.Select(article => article.Depth));
    }

    [Fact]
    public async Task Word_WithoutHeadingsStaysOneDivision()
    {
        var docx = CreateWord(
            ("Just some text.", null),
            ("And some more.", null));

        var structure = Analyze(await ReadWordAsync(docx));

        Assert.Single(structure.Articles);
    }

    [Fact]
    public void Html_WithASingleHeadingStaysOneDivision()
    {
        // One heading is a title. Dividing at it produces one division covering everything, which is the
        // answer a document with no structure already gets.
        var structure = Analyze(HtmlIngestionDocumentReader.Read(
            "<html><body><h1>Only a title</h1><p>Body.</p></body></html>",
            "https://example.test/one"));

        Assert.Single(structure.Articles);
    }

    private static DocumentStructure Analyze(IngestionDocument document)
    {
        return new TocSeededStructureAnalyzer(NullLogger<TocSeededStructureAnalyzer>.Instance).Analyze(document);
    }

    private static int GetHeadingLevel(IngestionDocumentElement element)
    {
        return element.HasMetadata && element.Metadata.TryGetValue(ElementMetadataKeys.HeadingLevel, out var value)
            ? (int)value
            : 0;
    }

    private static async Task<IngestionDocument> ReadWordAsync(byte[] docx)
    {
        using var stream = new MemoryStream(docx, writable: false);

        return await new OpenXmlIngestionDocumentReader().ReadAsync(stream, "manual.docx", WordMediaType);
    }

    private static byte[] CreateWord(params (string Text, string StyleId)[] paragraphs)
    {
        return CreateWord([.. paragraphs.Select(paragraph => (paragraph.Text, paragraph.StyleId, -1))]);
    }

    private static byte[] CreateWord(params (string Text, string StyleId, int OutlineLevel)[] paragraphs)
    {
        using var buffer = new MemoryStream();

        using (var document = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document, autoSave: true))
        {
            var body = document.AddMainDocumentPart().Document = new Document(new Body());

            foreach (var (text, styleId, outlineLevel) in paragraphs)
            {
                var paragraph = new Paragraph(new Run(new Text(text)));

                if (styleId is not null || outlineLevel >= 0)
                {
                    var properties = new ParagraphProperties();

                    if (styleId is not null)
                    {
                        properties.ParagraphStyleId = new ParagraphStyleId { Val = styleId };
                    }

                    if (outlineLevel >= 0)
                    {
                        properties.OutlineLevel = new OutlineLevel { Val = outlineLevel };
                    }

                    paragraph.ParagraphProperties = properties;
                }

                body.Body.AppendChild(paragraph);
            }
        }

        return buffer.ToArray();
    }
}
