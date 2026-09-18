using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Processors;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Ingestion.Processors;

public sealed class FigureSalienceProcessorTests
{
    /// <summary>
    /// Verifies that a figure carrying a printed, numbered caption is worth transcribing. A label is the
    /// strongest cheap evidence that the artwork beside it is a real figure rather than decoration.
    /// </summary>
    [Fact]
    public async Task Process_PatternCaptionedFigure_ScoresDescribe()
    {
        var image = Image("doc-p1-1", caption: "Figure 1. Measured values.", captionSource: CaptionSources.Pattern);
        var document = Document(Page(1, image));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal(FigureTiers.Describe, image.GetMetadataString(FigureMetadataKeys.Tier));
    }

    /// <summary>
    /// Verifies that a caption recognized only by its typography keeps the figure but does not pay for a
    /// model call. Small type near a picture is suggestive, not conclusive.
    /// </summary>
    [Fact]
    public async Task Process_TypographyCaption_ScoresCaptionOnly()
    {
        var image = Image("doc-p1-1", caption: "A view of the test rig.", captionSource: CaptionSources.Typography);
        var document = Document(Page(1, image));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal(FigureTiers.CaptionOnly, image.GetMetadataString(FigureMetadataKeys.Tier));
    }

    /// <summary>
    /// Verifies that a figure nothing said anything about is not kept at all.
    /// </summary>
    [Fact]
    public async Task Process_UncaptionedFigure_ScoresSkip()
    {
        var image = Image("doc-p1-1");
        var section = Page(1, image);
        var document = Document(section);

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal(FigureTiers.Skip, image.GetMetadataString(FigureMetadataKeys.Tier));
        Assert.Empty(section.Elements);
    }

    /// <summary>
    /// Verifies that a figure the prose refers to by number is promoted, even when its caption alone would
    /// not have been enough.
    /// </summary>
    [Fact]
    public async Task Process_CitedInProse_IsPromoted()
    {
        var image = Image("doc-p1-1", caption: "A view of the test rig.", captionSource: CaptionSources.Typography, ordinal: 4);
        var document = Document(Page(
            1,
            Body("The trend line in Figure 4 shows the seasonal drop. Everything else held steady."),
            image));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal(FigureTiers.Describe, image.GetMetadataString(FigureMetadataKeys.Tier));
    }

    /// <summary>
    /// Verifies that artwork appearing on page after page is page furniture, even when something captioned it.
    /// </summary>
    [Fact]
    public async Task Process_RepeatedHashOnThreePages_ScoresSkip()
    {
        var images = new List<IngestionDocumentImage>();
        var sections = new List<IngestionDocumentSection>();

        for (var page = 1; page <= 3; page++)
        {
            var image = Image(
                $"doc-p{page}-1",
                caption: "Figure 1. The masthead artwork.",
                captionSource: CaptionSources.Pattern,
                contentHash: "shared-hash",
                pageNumber: page);

            images.Add(image);
            sections.Add(Page(page, image));
        }

        await CreateProcessor().ProcessAsync(Document([.. sections]), DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.All(images, image => Assert.Equal(FigureTiers.Skip, image.GetMetadataString(FigureMetadataKeys.Tier)));
    }

    /// <summary>
    /// Verifies that a picture too small to be a figure is dropped.
    /// </summary>
    [Fact]
    public async Task Process_TinyImage_IsSkipped()
    {
        var image = Image("doc-p1-1", pixelWidth: 40, pixelHeight: 40);
        var document = Document(Page(1, image));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal(FigureTiers.Skip, image.GetMetadataString(FigureMetadataKeys.Tier));
    }

    /// <summary>
    /// Verifies that dropping a figure gives its caption back to the prose rather than taking it along.
    /// </summary>
    /// <remarks>
    /// A caption is marked as belonging to its figure so it is emitted once, with the figure, instead of
    /// twice. If the figure is dropped and the mark is left behind, the caption is emitted with nothing and
    /// its words leave the document — and the caption is often the only text naming what was pictured.
    /// </remarks>
    [Fact]
    public async Task Process_SkippedFigure_ReleasesItsCaptionBackToTheText()
    {
        var image = Image(
            "doc-p1-1",
            caption: "Detail of the rotor assembly",
            captionSource: CaptionSources.Typography,
            pixelWidth: 40,
            pixelHeight: 40);

        var caption = Body("Detail of the rotor assembly");
        caption.Metadata[ElementMetadataKeys.IsCaptionFor] = "doc-p1-1";

        var document = Document(Page(1, image, caption));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal(FigureTiers.Skip, image.GetMetadataString(FigureMetadataKeys.Tier));
        Assert.DoesNotContain(document.Sections[0].Elements, element => ReferenceEquals(element, image));
        Assert.Null(caption.GetMetadataString(ElementMetadataKeys.IsCaptionFor));
    }

    /// <summary>
    /// Verifies that a caption whose figure survived keeps its mark, so it is still emitted with the figure
    /// rather than a second time on its own.
    /// </summary>
    [Fact]
    public async Task Process_KeptFigure_LeavesItsCaptionMarked()
    {
        var image = Image("doc-p1-1", caption: "Figure 1. The measurements.", ordinal: 1);
        var caption = Body("Figure 1. The measurements.");
        caption.Metadata[ElementMetadataKeys.IsCaptionFor] = "doc-p1-1";

        var document = Document(Page(1, image, caption));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.NotEqual(FigureTiers.Skip, image.GetMetadataString(FigureMetadataKeys.Tier));
        Assert.Equal("doc-p1-1", caption.GetMetadataString(ElementMetadataKeys.IsCaptionFor));
    }

    /// <summary>
    /// Verifies that a figure covering a page that says almost nothing, in a document that otherwise has
    /// text, is dropped. That is the shape of a full page advertisement, and a caption does not redeem it.
    /// </summary>
    [Fact]
    public async Task Process_FullBleedOnTextLessPage_ScoresSkip()
    {
        var image = Image(
            "doc-p2-1",
            caption: "Figure 1. The advertisement.",
            captionSource: CaptionSources.Pattern,
            pageNumber: 2,
            bounds: [0, 0, 595, 842]);

        var document = Document(
            Page(1, Body("The article opens with a long paragraph of body text that fills the first page of the issue.")),
            Page(2, image),
            Page(3, Body("The article continues with another long paragraph of body text on the third page.")));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal(FigureTiers.Skip, image.GetMetadataString(FigureMetadataKeys.Tier));
    }

    /// <summary>
    /// Verifies that a scanned document — every page one picture and no text layer at all — has its pages
    /// transcribed rather than dropped. Nothing captions a scan, so every other signal would skip it and the
    /// document would never be answerable.
    /// </summary>
    [Fact]
    public async Task Process_ScannedDocument_DescribesEveryPage()
    {
        var first = Image("scan-p1-1", pageNumber: 1, bounds: [0, 0, 595, 842]);
        var second = Image("scan-p2-1", contentHash: "hash-2", pageNumber: 2, bounds: [0, 0, 595, 842]);

        var document = Document(Page(1, first), Page(2, second));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal(FigureTiers.Describe, first.GetMetadataString(FigureMetadataKeys.Tier));
        Assert.Equal(FigureTiers.Describe, second.GetMetadataString(FigureMetadataKeys.Tier));
    }

    /// <summary>
    /// Verifies that turning figure processing off drops every figure, however good it looks.
    /// </summary>
    [Fact]
    public async Task Process_ModeOff_SkipsEverything()
    {
        var image = Image("doc-p1-1", caption: "Figure 1. A chart.", captionSource: CaptionSources.Pattern);
        var section = Page(1, image);

        await CreateProcessor().ProcessAsync(
            Document(section),
            new DocumentIngestionContext
            {
                FigureMode = FigureProcessingMode.Off,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(FigureTiers.Skip, image.GetMetadataString(FigureMetadataKeys.Tier));
        Assert.Empty(section.Elements);
    }

    /// <summary>
    /// Verifies that asking for everything promotes the figures that were merely kept, but does not resurrect
    /// the ones that were dropped.
    /// </summary>
    [Fact]
    public async Task Process_ModeAll_PromotesCaptionOnlyToDescribe_ButNotRepeats()
    {
        var sections = new List<IngestionDocumentSection>();
        var repeats = new List<IngestionDocumentImage>();

        for (var page = 1; page <= 3; page++)
        {
            var image = Image(
                $"doc-p{page}-1",
                caption: "Figure 1. The masthead artwork.",
                captionSource: CaptionSources.Pattern,
                contentHash: "shared-hash",
                pageNumber: page);

            repeats.Add(image);
            sections.Add(Page(page, image));
        }

        var modest = Image(
            "doc-p4-1",
            caption: "A view of the test rig.",
            captionSource: CaptionSources.Typography,
            contentHash: "own-hash",
            pageNumber: 4);

        sections.Add(Page(4, modest));

        await CreateProcessor().ProcessAsync(
            Document([.. sections]),
            new DocumentIngestionContext
            {
                FigureMode = FigureProcessingMode.All,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(FigureTiers.Describe, modest.GetMetadataString(FigureMetadataKeys.Tier));
        Assert.All(repeats, image => Assert.Equal(FigureTiers.Skip, image.GetMetadataString(FigureMetadataKeys.Tier)));
    }

    /// <summary>
    /// Verifies that the per-document budget demotes the weakest candidates rather than dropping them. A
    /// figure that lost a budget race is still a figure.
    /// </summary>
    [Fact]
    public async Task Process_Budget_DemotesLowestToCaptionOnly()
    {
        var strongest = Image("doc-p1-1", caption: "Figure 1. First.", captionSource: CaptionSources.Pattern, ordinal: 1, contentHash: "a", pageNumber: 1);
        var strong = Image("doc-p2-1", caption: "Figure 2. Second.", captionSource: CaptionSources.Pattern, ordinal: 2, contentHash: "b", pageNumber: 2);
        var weakest = Image("doc-p3-1", caption: "Figure 3. Third.", captionSource: CaptionSources.Pattern, ordinal: 3, contentHash: "c", pageNumber: 3);

        var document = Document(
            Page(1, Body("Figure 1 and Figure 2 are both discussed at length in the text."), strongest),
            Page(2, strong),
            Page(3, weakest));

        await CreateProcessor().ProcessAsync(
            document,
            new DocumentIngestionContext
            {
                MaxFigureDescriptionsPerDocument = 2,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(FigureTiers.Describe, strongest.GetMetadataString(FigureMetadataKeys.Tier));
        Assert.Equal(FigureTiers.Describe, strong.GetMetadataString(FigureMetadataKeys.Tier));
        Assert.Equal(FigureTiers.CaptionOnly, weakest.GetMetadataString(FigureMetadataKeys.Tier));
    }

    private static FigureSalienceProcessor CreateProcessor()
    {
        return new FigureSalienceProcessor(
            Options.Create(new FigureSalienceOptions()),
            Options.Create(new CaptionPatternOptions()));
    }

    private static IngestionDocument Document(params IngestionDocumentSection[] sections)
    {
        var document = new IngestionDocument("doc");

        foreach (var section in sections)
        {
            document.Sections.Add(section);
        }

        return document;
    }

    private static IngestionDocumentSection Page(int pageNumber, params IngestionDocumentElement[] elements)
    {
        var section = new IngestionDocumentSection
        {
            PageNumber = pageNumber,
        };

        section.Metadata[ElementMetadataKeys.PageWidth] = 595d;
        section.Metadata[ElementMetadataKeys.PageHeight] = 842d;

        foreach (var element in elements)
        {
            section.Elements.Add(element);
        }

        return section;
    }

    private static IngestionDocumentParagraph Body(string text)
    {
        return new IngestionDocumentParagraph(text)
        {
            Text = text,
            PageNumber = 1,
        };
    }

    private static IngestionDocumentImage Image(
        string figureId,
        string caption = null,
        string captionSource = null,
        string contentHash = "hash-1",
        int pixelWidth = 400,
        int pixelHeight = 300,
        int pageNumber = 1,
        int? ordinal = null,
        double[] bounds = null)
    {
        var image = new IngestionDocumentImage($"![]({figureId})")
        {
            Content = new byte[] { 1, 2, 3, 4 },
            MediaType = "image/png",
            PageNumber = pageNumber,
        };

        image.Metadata[FigureMetadataKeys.Id] = figureId;
        image.Metadata[FigureMetadataKeys.ContentHash] = contentHash;
        image.Metadata[FigureMetadataKeys.PixelWidth] = pixelWidth;
        image.Metadata[FigureMetadataKeys.PixelHeight] = pixelHeight;
        image.Metadata[ElementMetadataKeys.BoundingBox] = bounds ?? [40d, 400d, 300d, 550d];

        if (caption != null)
        {
            image.Metadata[FigureMetadataKeys.Caption] = caption;
            image.Metadata[FigureMetadataKeys.CaptionSource] = captionSource ?? CaptionSources.Pattern;
        }

        if (ordinal.HasValue)
        {
            image.Metadata[FigureMetadataKeys.Ordinal] = ordinal.Value;
        }

        return image;
    }
}
