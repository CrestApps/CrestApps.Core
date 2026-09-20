using CrestApps.Core.AI.Ingestion.Knowledge.Structure;
using CrestApps.Core.AI.Ingestion.Pdf.Services;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CrestApps.Core.Tests.Core.Ingestion.Structure;

/// <summary>
/// Covers a page that carries more than one story, which is what a newspaper is and what the analyzer used
/// to be unable to represent.
/// </summary>
/// <remarks>
/// Before this, one boundary per page was the most that could be found: the topmost headline opened an
/// article and every other story on the page was absorbed into it. The rule that replaced it keeps the old
/// guard for the first heading on a page — a pull quote halfway down a feature must not open anything — and
/// drops it for the rest, because every headline after the first on a newspaper page is below the fold.
/// </remarks>
public sealed class MultiStoryPageTests
{
    private const string PdfMediaType = "application/pdf";
    private const double PageHeight = 842;

    [Fact]
    public async Task Analyze_OpensAnArticleForEachHeadlineOnThePage()
    {
        var pdf = CreateNewsPage(
            new Story("Council approves the budget", 800),
            new Story("Bridge reopens after repairs", 520),
            new Story("Library extends its hours", 260));

        var structure = await AnalyzeAsync(pdf);

        Assert.Equal(3, structure.Articles.Count);
        Assert.Equal(
            ["Council approves the budget", "Bridge reopens after repairs", "Library extends its hours"],
            structure.Articles.Select(article => article.Title));
    }

    [Fact]
    public async Task Analyze_KeepsEachStorysOwnTextApart()
    {
        var pdf = CreateNewsPage(
            new Story("Council approves the budget", 800),
            new Story("Bridge reopens after repairs", 520),
            new Story("Library extends its hours", 260));

        var document = await ReadAsync(pdf);
        var structure = StructureAnalyzers.Default().Analyze(document);

        // Every story shares one page, so a page-bounded answer would file all of this under whichever
        // headline came last.
        var ordinals = document.Sections
            .SelectMany(section => section.Elements)
            .Select(element => (int)element.Metadata[CrestApps.Core.AI.Ingestion.ElementMetadataKeys.ArticleOrdinal])
            .Distinct()
            .ToList();

        Assert.Equal(structure.Articles.Count, ordinals.Count);
    }

    [Fact]
    public async Task Analyze_DoesNotOpenAnArticleForTypeBelowTheFoldOnItsOwn()
    {
        // One large line halfway down a page, with nothing above it, is a pull quote rather than a headline.
        // The first heading on a page still has to sit near the top.
        var pdf = CreateNewsPage(new Story("A line of large type", 400));

        var structure = await AnalyzeAsync(pdf);

        Assert.Single(structure.Articles);
    }

    private static async Task<DocumentStructure> AnalyzeAsync(byte[] pdf)
    {
        return StructureAnalyzers.Default().Analyze(await ReadAsync(pdf));
    }

    private static async Task<Microsoft.Extensions.DataIngestion.IngestionDocument> ReadAsync(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf, writable: false);

        return await new PdfIngestionDocumentReader().ReadAsync(stream, "gazette.pdf", PdfMediaType);
    }

    /// <summary>
    /// Builds a single page carrying several stories, each a headline over two lines of body text.
    /// </summary>
    /// <param name="stories">The stories, top to bottom.</param>
    /// <returns>The PDF bytes.</returns>
    private static byte[] CreateNewsPage(params Story[] stories)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);

        foreach (var story in stories)
        {
            page.AddText(story.Headline, 22, new PdfPoint(50, story.Top), font);

            // Two lines of body text, far enough below the headline that the segmenter keeps them apart and
            // near enough each other that it keeps them together.
            page.AddText(
                "The council met on Tuesday evening to consider the matter in detail.",
                10,
                new PdfPoint(50, story.Top - 30),
                font);

            page.AddText(
                "Several residents spoke, and a decision followed after a short debate.",
                10,
                new PdfPoint(50, story.Top - 44),
                font);
        }

        return builder.Build();
    }

    /// <summary>
    /// One story to place on the page.
    /// </summary>
    /// <param name="Headline">The headline text.</param>
    /// <param name="Top">The vertical position of the headline, measured from the bottom of the page.</param>
    private sealed record Story(string Headline, double Top);
}
