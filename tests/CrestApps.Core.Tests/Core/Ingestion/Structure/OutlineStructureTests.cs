using CrestApps.Core.AI.Ingestion.Knowledge.Structure;
using CrestApps.Core.AI.Ingestion.Pdf.Services;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Writer;

namespace CrestApps.Core.Tests.Core.Ingestion.Structure;

/// <summary>
/// Covers reading a document's own outline and turning it into the divisions it describes.
/// </summary>
/// <remarks>
/// A manual, a report or a book states its structure in an outline. Everything else the analyzer reads —
/// a contents page, type size, where a heading sits — is inference, and the point of these tests is that the
/// stated answer is taken in preference to the inferred one, with the nesting intact.
/// </remarks>
public sealed class OutlineStructureTests
{
    private const string PdfMediaType = "application/pdf";

    [Fact]
    public async Task Analyze_TakesNestingFromTheOutline()
    {
        // A six page manual: two chapters, each with sections beneath it.
        var pdf = CreateOutlinedPdf(
            pageCount: 6,
            new OutlineFixture("Chapter 1", 1, 1,
                new OutlineFixture("Section 1.1", 2, 2),
                new OutlineFixture("Section 1.2", 2, 3)),
            new OutlineFixture("Chapter 2", 1, 4,
                new OutlineFixture("Section 2.1", 2, 5)));

        var structure = await AnalyzeAsync(pdf);

        Assert.True(structure.IsInferred);
        Assert.Equal(5, structure.Articles.Count);

        Assert.Equal(
            ["Chapter 1", "Section 1.1", "Section 1.2", "Chapter 2", "Section 2.1"],
            structure.Articles.Select(article => article.Title));

        Assert.Equal([1, 2, 2, 1, 2], structure.Articles.Select(article => article.Depth));
    }

    [Fact]
    public async Task Analyze_NestsSectionsUnderTheirChapter()
    {
        var pdf = CreateOutlinedPdf(
            pageCount: 6,
            new OutlineFixture("Chapter 1", 1, 1,
                new OutlineFixture("Section 1.1", 2, 2),
                new OutlineFixture("Section 1.2", 2, 3)),
            new OutlineFixture("Chapter 2", 1, 4,
                new OutlineFixture("Section 2.1", 2, 5)));

        var structure = await AnalyzeAsync(pdf);
        var byTitle = structure.Articles.ToDictionary(article => article.Title, StringComparer.Ordinal);

        Assert.Equal(0, byTitle["Chapter 1"].ParentOrdinal);
        Assert.Equal(byTitle["Chapter 1"].Ordinal, byTitle["Section 1.1"].ParentOrdinal);
        Assert.Equal(byTitle["Chapter 1"].Ordinal, byTitle["Section 1.2"].ParentOrdinal);
        Assert.Equal(0, byTitle["Chapter 2"].ParentOrdinal);
        Assert.Equal(byTitle["Chapter 2"].Ordinal, byTitle["Section 2.1"].ParentOrdinal);
    }

    [Fact]
    public async Task Analyze_SpansAChapterAcrossItsSections()
    {
        var pdf = CreateOutlinedPdf(
            pageCount: 6,
            new OutlineFixture("Chapter 1", 1, 1,
                new OutlineFixture("Section 1.1", 2, 2),
                new OutlineFixture("Section 1.2", 2, 3)),
            new OutlineFixture("Chapter 2", 1, 4,
                new OutlineFixture("Section 2.1", 2, 5)));

        var structure = await AnalyzeAsync(pdf);
        var byTitle = structure.Articles.ToDictionary(article => article.Title, StringComparer.Ordinal);

        // A chapter runs until the next chapter, not until its own first section.
        Assert.Equal(1, byTitle["Chapter 1"].PageStart);
        Assert.Equal(3, byTitle["Chapter 1"].PageEnd);

        Assert.Equal(4, byTitle["Chapter 2"].PageStart);
        Assert.Equal(6, byTitle["Chapter 2"].PageEnd);

        // A section runs until the next entry at its own level or shallower.
        Assert.Equal(2, byTitle["Section 1.1"].PageStart);
        Assert.Equal(2, byTitle["Section 1.1"].PageEnd);
        Assert.Equal(3, byTitle["Section 1.2"].PageStart);
        Assert.Equal(3, byTitle["Section 1.2"].PageEnd);
    }

    [Fact]
    public async Task Analyze_GivesAPageToItsInnermostDivision()
    {
        var pdf = CreateOutlinedPdf(
            pageCount: 6,
            new OutlineFixture("Chapter 1", 1, 1,
                new OutlineFixture("Section 1.1", 2, 2),
                new OutlineFixture("Section 1.2", 2, 3)),
            new OutlineFixture("Chapter 2", 1, 4,
                new OutlineFixture("Section 2.1", 2, 5)));

        var document = await ReadAsync(pdf);
        new TocSeededStructureAnalyzer(NullLogger<TocSeededStructureAnalyzer>.Instance).Analyze(document);

        var structure = await AnalyzeAsync(pdf);
        var byTitle = structure.Articles.ToDictionary(article => article.Title, StringComparer.Ordinal);

        // Page 2 belongs to both Chapter 1 and Section 1.1. Storing its text against both would store it
        // twice, so the innermost division owns it.
        var page2 = document.Sections.Single(section => section.PageNumber == 2);
        var ordinal = Assert.IsType<int>(page2.Metadata[CrestApps.Core.AI.Ingestion.ElementMetadataKeys.ArticleOrdinal]);

        Assert.Equal(byTitle["Section 1.1"].Ordinal, ordinal);
    }

    [Fact]
    public async Task Analyze_KeepsFrontMatterInTheFirstDivision()
    {
        // The outline says nothing about pages 1 and 2. An element owned by no division is an element whose
        // text is never stored, so the first division reaches back to cover them.
        var pdf = CreateOutlinedPdf(
            pageCount: 5,
            new OutlineFixture("Chapter 1", 1, 3));

        var structure = await AnalyzeAsync(pdf);
        var chapter = Assert.Single(structure.Articles);

        Assert.Equal(1, chapter.PageStart);
        Assert.Equal(5, chapter.PageEnd);
    }

    [Fact]
    public async Task Analyze_FallsBackWhenThereIsNoOutline()
    {
        var pdf = CreatePdf(pageCount: 3, bookmarks: null);

        var structure = await AnalyzeAsync(pdf);

        // No outline and nothing else to go on is one article, which is what it was before any of this.
        Assert.Single(structure.Articles);
        Assert.Equal(1, structure.Articles[0].Depth);
    }

    private static async Task<DocumentStructure> AnalyzeAsync(byte[] pdf)
    {
        var document = await ReadAsync(pdf);

        return new TocSeededStructureAnalyzer(NullLogger<TocSeededStructureAnalyzer>.Instance).Analyze(document);
    }

    private static async Task<Microsoft.Extensions.DataIngestion.IngestionDocument> ReadAsync(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf, writable: false);

        return await new PdfIngestionDocumentReader().ReadAsync(stream, "manual.pdf", PdfMediaType);
    }

    private static byte[] CreateOutlinedPdf(int pageCount, params OutlineFixture[] roots)
    {
        return CreatePdf(pageCount, new Bookmarks([.. roots.Select(root => root.ToNode())]));
    }

    private static byte[] CreatePdf(int pageCount, Bookmarks bookmarks)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        for (var number = 1; number <= pageCount; number++)
        {
            var page = builder.AddPage(PageSize.A4);

            page.AddText($"Body text for page {number}.", 10, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        }

        if (bookmarks is not null)
        {
            builder.Bookmarks = bookmarks;
        }

        return builder.Build();
    }

    /// <summary>
    /// One outline entry to write into a fixture, and the entries beneath it.
    /// </summary>
    private sealed record OutlineFixture(string Title, int Level, int PageNumber, params OutlineFixture[] Children)
    {
        public DocumentBookmarkNode ToNode()
        {
            return new DocumentBookmarkNode(
                Title,
                Level,
                new ExplicitDestination(PageNumber, ExplicitDestinationType.FitPage, new ExplicitDestinationCoordinates(null)),
                [.. Children.Select(child => child.ToNode())]);
        }
    }
}
