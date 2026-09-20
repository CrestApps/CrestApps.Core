using CrestApps.Core.AI.Ingestion.Knowledge.Structure;
using CrestApps.Core.AI.Ingestion.Pdf.Services;
using CrestApps.Core.Tests.Support;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CrestApps.Core.Tests.Core.Ingestion.Structure;

/// <summary>
/// Covers two things real magazines do that synthetic fixtures never did.
/// </summary>
/// <remarks>
/// Both were found by running actual magazines through the pipeline rather than by reasoning about it. A
/// headline that wraps was being read as two articles, and display type set twice to fake a heavier weight
/// was being read as every word repeated.
/// </remarks>
public sealed class RealDocumentHeadingTests
{
    private const string PdfMediaType = "application/pdf";

    private static readonly string[] BodyLines =
    [
        "The body of the article begins here and runs on for a while.",
        "It continues with a second sentence of ordinary prose.",
        "A third line keeps the body the commonest size on the page.",
        "And a fourth settles the matter beyond any doubt.",
    ];

    [Fact]
    public async Task Analyze_ReadsAWrappedHeadlineAsOneArticle()
    {
        // The first article's headline runs over two lines. Reading the second line as a headline of its own
        // splits the article in two, titles the first half with half a sentence, and gives the article's
        // pages to the half that says least.
        var pdf = CreateIssue(
            new Article(["The consequences of the regulatory changes", "for the energy rating of dwellings"]),
            new Article(["Summer comfort in dwellings"]),
            new Article(["Electrostatic filtration indoors"]));

        var structure = await AnalyzeAsync(pdf);

        Assert.Equal(3, structure.Articles.Count);

        Assert.Equal(
            "The consequences of the regulatory changes for the energy rating of dwellings",
            structure.Articles[0].Title);

        // And the merged headline keeps the article's pages, rather than its second line taking them.
        Assert.Equal(1, structure.Articles[0].PageStart);
        Assert.Equal(1, structure.Articles[0].PageEnd);
        Assert.Equal("Summer comfort in dwellings", structure.Articles[1].Title);
    }

    [Fact]
    public async Task Analyze_KeepsTwoHeadlinesSideBySideApart()
    {
        // Two headlines in adjacent columns are the same size and the same height. They are two headlines,
        // and a rule that merged on size and proximity alone would join them into one.
        var pdf = CreateIssue(
            new Article(["Council approves budget"], SecondColumnHeadline: "Bridge reopens today"),
            new Article(["Library extends its hours"]));

        var structure = await AnalyzeAsync(pdf);

        Assert.Equal(3, structure.Articles.Count);

        Assert.Equal(
            ["Council approves budget", "Bridge reopens today", "Library extends its hours"],
            structure.Articles.Select(article => article.Title));
    }

    [Theory]
    [InlineData("COMMAND COMMAND & & CONQUER", "COMMAND & CONQUER")]
    [InlineData("Inkscape Inkscape - - Part Part 158", "Inkscape - Part 158")]
    [InlineData("Trading Trading Up Up To To Linux Linux Pt.6", "Trading Up To Linux Pt.6")]
    [InlineData("Nothing repeated here", "Nothing repeated here")]
    [InlineData("", "")]
    [InlineData("Single", "Single")]
    public void CollapseOverprint_RemovesTypeSetTwice(string printed, string expected)
    {
        // Display type set twice with a slight offset to fake a heavier weight reads as every word repeated.
        // Both runs are real text, so nothing downstream can tell this from a title.
        Assert.Equal(expected, DocumentStructureRungs.CollapseOverprint(printed));
    }

    private static async Task<DocumentStructure> AnalyzeAsync(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf, writable: false);
        var document = await new PdfIngestionDocumentReader().ReadAsync(stream, "issue.pdf", PdfMediaType);

        return StructureAnalyzers.Default().Analyze(document);
    }

    /// <summary>
    /// Builds an issue with one article per page.
    /// </summary>
    /// <param name="articles">The articles, in order.</param>
    /// <returns>The PDF bytes.</returns>
    private static byte[] CreateIssue(params Article[] articles)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var article in articles)
        {
            var page = builder.AddPage(PageSize.A4);
            var top = 780d;

            foreach (var line in article.HeadlineLines)
            {
                page.AddText(line, 22, new PdfPoint(40, top), font);
                top -= 28;
            }

            // Enough body text that the body size is the commonest size on the page. A page that is mostly
            // headline makes the headline the body, and then nothing on it reads as a heading at all.
            var body = top - 24;

            foreach (var line in BodyLines)
            {
                page.AddText(line, 10, new PdfPoint(40, body), font);
                body -= 14;
            }

            if (article.SecondColumnHeadline is not null)
            {
                page.AddText(article.SecondColumnHeadline, 22, new PdfPoint(320, 780), font);

                var beside = 742d;

                foreach (var line in BodyLines)
                {
                    page.AddText(line, 10, new PdfPoint(320, beside), font);
                    beside -= 14;
                }
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// One article to place on its own page.
    /// </summary>
    /// <param name="HeadlineLines">The headline, one entry per printed line.</param>
    /// <param name="SecondColumnHeadline">A second story beside it, when the page carries one.</param>
    private sealed record Article(string[] HeadlineLines, string SecondColumnHeadline = null);
}
