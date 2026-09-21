using System.Text.RegularExpressions;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Processors;
using CrestApps.Core.Ingestion;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Ingestion.Processors;

public sealed class FigureCaptionProcessorTests
{
    /// <summary>
    /// Verifies that the shipped caption patterns recognize the common European and East Asian spellings.
    /// </summary>
    /// <remarks>
    /// This is not a cosmetic list. A caption that matches no pattern falls back to the typography
    /// heuristic, which scores below the threshold a figure has to clear to be transcribed at all — so a
    /// missing language means every figure in that language is stored with a caption and no description.
    /// </remarks>
    [Theory]
    [InlineData("Figure 4. The measured deflection.", CaptionBuckets.Figure)]
    [InlineData("Fig. 4 The measured deflection.", CaptionBuckets.Figure)]
    [InlineData("Exhibit 2 - quarterly totals", CaptionBuckets.Figure)]
    [InlineData("Abbildung 3: Die gemessene Durchbiegung.", CaptionBuckets.Figure)]
    [InlineData("Abb. 3 Die gemessene Durchbiegung.", CaptionBuckets.Figure)]
    [InlineData("Figura 5. La deflessione misurata.", CaptionBuckets.Figure)]
    [InlineData("Graphique 6 - la deformation mesuree", CaptionBuckets.Figure)]
    [InlineData("Afbeelding 7", CaptionBuckets.Figure)]
    [InlineData("Rysunek 8 - zmierzone ugiecie", CaptionBuckets.Figure)]
    [InlineData("Рисунок 9. Измеренный прогиб.", CaptionBuckets.Figure)]
    [InlineData("Рис. 9 Измеренный прогиб.", CaptionBuckets.Figure)]
    [InlineData("図 10 たわみの測定", CaptionBuckets.Figure)]
    [InlineData("그림 11", CaptionBuckets.Figure)]
    [InlineData("1. ábra A mert lehajlas.", CaptionBuckets.Figure)]
    [InlineData("Table 2. Mechanical properties.", CaptionBuckets.Table)]
    [InlineData("Tabelle 2: Mechanische Eigenschaften.", CaptionBuckets.Table)]
    [InlineData("Tableau 3 - proprietes mecaniques", CaptionBuckets.Table)]
    [InlineData("Tabla 4", CaptionBuckets.Table)]
    [InlineData("Tabulka 5", CaptionBuckets.Table)]
    [InlineData("Таблица 6. Механические свойства.", CaptionBuckets.Table)]
    [InlineData("表 7", CaptionBuckets.Table)]
    [InlineData("2. táblázat", CaptionBuckets.Table)]
    public void DefaultPatterns_RecognizeCaptionsInEveryShippedLanguage(string caption, string expectedBucket)
    {
        var options = new CaptionPatternOptions();

        var match = options.Patterns.FirstOrDefault(pattern => pattern.Expression.IsMatch(caption));

        Assert.NotNull(match);
        Assert.Equal(expectedBucket, match.Bucket);
    }

    /// <summary>
    /// Verifies that ordinary prose opening with one of those words is not mistaken for a caption. Every
    /// pattern requires a number, which is what keeps "Table of contents" out of the table family.
    /// </summary>
    [Theory]
    [InlineData("Table of contents")]
    [InlineData("Figures are drawn to scale throughout this report.")]
    [InlineData("Abbildungen sind nicht massstabsgetreu.")]
    public void DefaultPatterns_DoNotMatchProse(string text)
    {
        var options = new CaptionPatternOptions();

        Assert.DoesNotContain(options.Patterns, pattern => pattern.Expression.IsMatch(text));
    }

    /// <summary>
    /// Verifies that a numbered caption printed under a figure is assigned to it.
    /// </summary>
    [Fact]
    public async Task Process_CaptionBelowFigure_IsAssigned()
    {
        var image = Image("doc-p1-1", 1, 40, 500, 300, 650);
        var document = Document(Page(
            1,
            Body("Ordinary body text that sets the page typography for everything else on it.", 1, 40, 700, 300, 780),
            image,
            Caption("Figure 1. A chart of the measured values.", 1, 40, 480, 300, 492)));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal("Figure 1. A chart of the measured values.", image.GetMetadataString(FigureMetadataKeys.Caption));
        Assert.Equal(CaptionSources.Pattern, image.GetMetadataString(FigureMetadataKeys.CaptionSource));
        Assert.Equal(CaptionBuckets.Figure, image.GetMetadataString(FigureMetadataKeys.Bucket));
        Assert.Equal(1, image.Metadata[FigureMetadataKeys.Ordinal]);
    }

    /// <summary>
    /// Verifies that a table caption printed above what it captions is assigned, and recorded as a table
    /// rather than a figure.
    /// </summary>
    [Fact]
    public async Task Process_CaptionAboveTable_IsAssignedUsingTableBucket()
    {
        var image = Image("doc-p1-1", 1, 40, 400, 300, 550);
        var document = Document(Page(
            1,
            Body("Ordinary body text that sets the page typography for everything else on it.", 1, 40, 700, 300, 780),
            Caption("Table 2. Material properties.", 1, 40, 560, 300, 572),
            image));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal("Table 2. Material properties.", image.GetMetadataString(FigureMetadataKeys.Caption));
        Assert.Equal(CaptionBuckets.Table, image.GetMetadataString(FigureMetadataKeys.Bucket));
        Assert.Equal(2, image.Metadata[FigureMetadataKeys.Ordinal]);
    }

    /// <summary>
    /// Verifies that a paragraph of prose lying between a figure and a caption rules the pairing out. A
    /// caption never has body text between it and what it captions.
    /// </summary>
    [Fact]
    public async Task Process_BodyTextBetweenImageAndCandidate_Disqualifies()
    {
        var image = Image("doc-p1-1", 1, 40, 500, 300, 650);
        var document = Document(Page(
            1,
            Body("Ordinary body text that sets the page typography for everything else on it.", 1, 40, 700, 300, 780),
            image,
            Body("A second paragraph of running prose that sits between the artwork and the label below.", 1, 40, 400, 300, 480),
            Caption("Figure 1. A chart of the measured values.", 1, 40, 300, 300, 312)));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Null(image.GetMetadataString(FigureMetadataKeys.Caption));
    }

    /// <summary>
    /// Verifies that five confident samples teach the processor which side this document puts figure captions
    /// on, overriding the configured default when an ambiguous case comes up.
    /// </summary>
    [Fact]
    public async Task Process_LearnedDirection_ConvergesWithFiveSamples()
    {
        var ambiguous = await RunAmbiguousFigureAsync(confidentSamples: 5, defaultDirection: CaptionDirection.Above);

        Assert.Equal("Figure 99. The caption below.", ambiguous.GetMetadataString(FigureMetadataKeys.Caption));
    }

    /// <summary>
    /// Verifies that four samples are not enough. Guessing a document's habit from a handful of cases is
    /// worse than keeping the configured default.
    /// </summary>
    [Fact]
    public async Task Process_LearnedDirection_FallsBackBelowFiveSamples()
    {
        var ambiguous = await RunAmbiguousFigureAsync(confidentSamples: 4, defaultDirection: CaptionDirection.Above);

        Assert.Equal("Figure 99. The caption above.", ambiguous.GetMetadataString(FigureMetadataKeys.Caption));
    }

    /// <summary>
    /// Verifies that the two caption families learn separately: the same document can put figure captions
    /// below and table captions above, which is exactly why a single fixed rule cannot work.
    /// </summary>
    [Fact]
    public async Task Process_TwoBucketsLearnIndependently()
    {
        var options = CreateOptions();
        options.DefaultFigureDirection = CaptionDirection.Unknown;
        options.DefaultTableDirection = CaptionDirection.Unknown;

        var sections = new List<IngestionDocumentSection>();

        for (var index = 1; index <= 5; index++)
        {
            var page = index;

            sections.Add(Page(
                page,
                Body("Ordinary body text that sets the page typography for everything else on it.", page, 40, 700, 300, 780),
                Image($"doc-p{page}-1", page, 40, 500, 300, 650),
                Caption($"Figure {index}. Printed below the artwork.", page, 40, 480, 300, 492),
                Caption($"Table {index}. Printed above the grid.", page, 40, 420, 300, 432),
                Image($"doc-p{page}-2", page, 40, 250, 300, 400, ordinal: 2)));
        }

        var ambiguousFigure = Image("doc-p6-1", 6, 40, 400, 300, 550);
        var ambiguousTable = Image("doc-p7-1", 7, 40, 400, 300, 550);

        sections.Add(Page(
            6,
            Body("Ordinary body text that sets the page typography for everything else on it.", 6, 40, 700, 300, 780),
            Caption("Figure 99. The caption above.", 6, 40, 570, 300, 582),
            ambiguousFigure,
            Caption("Figure 98. The caption below.", 6, 40, 368, 300, 380)));

        sections.Add(Page(
            7,
            Body("Ordinary body text that sets the page typography for everything else on it.", 7, 40, 700, 300, 780),
            Caption("Table 99. The caption above.", 7, 40, 570, 300, 582),
            ambiguousTable,
            Caption("Table 98. The caption below.", 7, 40, 368, 300, 380)));

        var document = Document([.. sections]);

        await CreateProcessor(options).ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal("Figure 98. The caption below.", ambiguousFigure.GetMetadataString(FigureMetadataKeys.Caption));
        Assert.Equal("Table 99. The caption above.", ambiguousTable.GetMetadataString(FigureMetadataKeys.Caption));
    }

    /// <summary>
    /// Verifies that a figure printed without a caption, but carrying the number the document printed on it,
    /// picks up the sentence in the prose that refers to it by that number.
    /// </summary>
    /// <remarks>
    /// The figure is the first image on its page and is labelled Figure 3, so the sentence can only be found
    /// through the printed number.
    /// </remarks>
    [Fact]
    public async Task Process_NoCaption_UsesInTextReference()
    {
        var image = Image("doc-p1-1", 1, 40, 400, 300, 550, ordinal: 1, figureNumber: 3);
        var document = Document(Page(
            1,
            Body("The trend line in Figure 3 shows the seasonal drop. Everything else held steady.", 1, 40, 700, 300, 780),
            image));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal(CaptionSources.InTextReference, image.GetMetadataString(FigureMetadataKeys.CaptionSource));
        Assert.Contains("Figure 3", image.GetMetadataString(FigureMetadataKeys.Context), StringComparison.Ordinal);
        Assert.Equal(3, image.Metadata[FigureMetadataKeys.Ordinal]);
    }

    /// <summary>
    /// Verifies that a figure's position among the images on its page is never matched against a numbered
    /// reference in the prose.
    /// </summary>
    /// <remarks>
    /// The single image on a later page is image one there, so treating that position as a figure number
    /// matches the sentence introducing Figure 1 at the front of the document and attaches a description of
    /// an entirely different picture. A context line reads as authoritative, so none is attached at all.
    /// </remarks>
    [Fact]
    public async Task Process_NoCaption_PageOrdinalIsNotMatchedAgainstInTextReference()
    {
        var image = Image("doc-p12-1", 12, 40, 400, 300, 550);
        var document = Document(
            Page(
                1,
                Body("Figure 1 shows the overall arrangement of the parts described further on.", 1, 40, 700, 300, 780)),
            Page(
                12,
                Body("Ordinary body text that sets the page typography for everything else on it.", 12, 40, 700, 300, 780),
                image));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal(CaptionSources.None, image.GetMetadataString(FigureMetadataKeys.CaptionSource));
        Assert.False(image.Metadata.ContainsKey(FigureMetadataKeys.Ordinal));

        // The figure still keeps the text printed around it, which is what its own page actually said.
        Assert.Equal(
            "Ordinary body text that sets the page typography for everything else on it.",
            image.GetMetadataString(FigureMetadataKeys.Context));
    }

    /// <summary>
    /// Verifies that an unnumbered caption is still recognized when it is set in smaller type than the body
    /// around it.
    /// </summary>
    [Fact]
    public async Task Process_TypographicallyDistinctUnnumberedCaption_IsCandidate()
    {
        var image = Image("doc-p1-1", 1, 40, 500, 300, 650);
        var document = Document(Page(
            1,
            Body("Ordinary body text that sets the page typography for everything else on it.", 1, 40, 700, 300, 780),
            image,
            Caption("A view of the test rig during commissioning.", 1, 40, 480, 300, 490, size: 7)));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal("A view of the test rig during commissioning.", image.GetMetadataString(FigureMetadataKeys.Caption));
        Assert.Equal(CaptionSources.Typography, image.GetMetadataString(FigureMetadataKeys.CaptionSource));
    }

    /// <summary>
    /// Verifies that the caption paragraph is marked as belonging to its figure, so flattening emits it once
    /// with the figure rather than twice.
    /// </summary>
    [Fact]
    public async Task Process_CaptionParagraph_IsMarkedIsCaptionFor()
    {
        var image = Image("doc-p1-1", 1, 40, 500, 300, 650);
        var caption = Caption("Figure 1. A chart of the measured values.", 1, 40, 480, 300, 492);
        var document = Document(Page(
            1,
            Body("Ordinary body text that sets the page typography for everything else on it.", 1, 40, 700, 300, 780),
            image,
            caption));

        await CreateProcessor().ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal("doc-p1-1", caption.GetMetadataString(ElementMetadataKeys.IsCaptionFor));
    }

    /// <summary>
    /// Verifies that a host can teach the processor another language by configuring a pattern, without
    /// replacing anything.
    /// </summary>
    [Fact]
    public async Task Process_HostPattern_IsHonoured()
    {
        var options = CreateOptions();
        options.Patterns.Add(new CaptionPattern
        {
            Expression = new Regex(@"^Abbildung\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
            Bucket = CaptionBuckets.Figure,
        });

        var image = Image("doc-p1-1", 1, 40, 500, 300, 650);
        var document = Document(Page(
            1,
            Body("Ordinary body text that sets the page typography for everything else on it.", 1, 40, 700, 300, 780),
            image,
            Caption("Abbildung 3. Ein Diagramm der Messwerte.", 1, 40, 480, 300, 492)));

        await CreateProcessor(options).ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal("Abbildung 3. Ein Diagramm der Messwerte.", image.GetMetadataString(FigureMetadataKeys.Caption));
        Assert.Equal(CaptionSources.Pattern, image.GetMetadataString(FigureMetadataKeys.CaptionSource));
        Assert.Equal(3, image.Metadata[FigureMetadataKeys.Ordinal]);
    }

    /// <summary>
    /// Builds a document whose first pages all print figure captions below the artwork, then one page where a
    /// figure sits exactly between a caption above and a caption below, and returns that ambiguous figure.
    /// </summary>
    /// <param name="confidentSamples">How many unambiguous below-captions the document shows first.</param>
    /// <param name="defaultDirection">The configured direction, which learning is expected to override.</param>
    /// <returns>The ambiguous figure, after processing.</returns>
    private static async Task<IngestionDocumentImage> RunAmbiguousFigureAsync(int confidentSamples, CaptionDirection defaultDirection)
    {
        var options = CreateOptions();
        options.DefaultFigureDirection = defaultDirection;

        var sections = new List<IngestionDocumentSection>();

        for (var index = 1; index <= confidentSamples; index++)
        {
            var page = index;

            sections.Add(Page(
                page,
                Body("Ordinary body text that sets the page typography for everything else on it.", page, 40, 700, 300, 780),
                Image($"doc-p{page}-1", page, 40, 500, 300, 650),
                Caption($"Figure {index}. Printed below the artwork.", page, 40, 480, 300, 492)));
        }

        var ambiguousPage = confidentSamples + 1;
        var ambiguous = Image($"doc-p{ambiguousPage}-1", ambiguousPage, 40, 400, 300, 550);

        sections.Add(Page(
            ambiguousPage,
            Body("Ordinary body text that sets the page typography for everything else on it.", ambiguousPage, 40, 700, 300, 780),
            Caption("Figure 99. The caption above.", ambiguousPage, 40, 570, 300, 582),
            ambiguous,
            Caption("Figure 99. The caption below.", ambiguousPage, 40, 368, 300, 380)));

        var document = Document([.. sections]);

        await CreateProcessor(options).ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        return ambiguous;
    }

    private static FigureCaptionProcessor CreateProcessor(CaptionPatternOptions options = null)
    {
        var wrapped = Options.Create(options ?? CreateOptions());

        return new FigureCaptionProcessor(
            new DefaultFigureCaptionCandidateDetector(wrapped),
            new DefaultFigureCaptionResolver(wrapped),
            wrapped);
    }

    private static CaptionPatternOptions CreateOptions()
    {
        return new CaptionPatternOptions();
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

    private static IngestionDocumentParagraph Body(
        string text,
        int pageNumber,
        double left,
        double bottom,
        double right,
        double top)
    {
        return Text(text, pageNumber, left, bottom, right, top, 10, "BodyFont");
    }

    private static IngestionDocumentParagraph Caption(
        string text,
        int pageNumber,
        double left,
        double bottom,
        double right,
        double top,
        double size = 10)
    {
        return Text(text, pageNumber, left, bottom, right, top, size, "BodyFont");
    }

    private static IngestionDocumentParagraph Text(
        string text,
        int pageNumber,
        double left,
        double bottom,
        double right,
        double top,
        double size,
        string fontName)
    {
        var paragraph = new IngestionDocumentParagraph(text)
        {
            Text = text,
            PageNumber = pageNumber,
        };

        paragraph.Metadata[ElementMetadataKeys.BoundingBox] = new[] { left, bottom, right, top };
        paragraph.Metadata[ElementMetadataKeys.ModalPointSize] = size;
        paragraph.Metadata[ElementMetadataKeys.ModalFontName] = fontName;

        return paragraph;
    }

    private static IngestionDocumentImage Image(
        string figureId,
        int pageNumber,
        double left,
        double bottom,
        double right,
        double top,
        int ordinal = 1,
        int? figureNumber = null)
    {
        var image = new IngestionDocumentImage($"![]({figureId})")
        {
            PageNumber = pageNumber,
        };

        image.Metadata[FigureMetadataKeys.Id] = figureId;
        image.Metadata[ElementMetadataKeys.BoundingBox] = new[] { left, bottom, right, top };
        image.Metadata[FigureMetadataKeys.ImageOrdinal] = ordinal;

        // The number the document printed on the figure, which a reader records only when it finds a label.
        // It is a different number from the ordinal above, which counts images on one page.
        if (figureNumber.HasValue)
        {
            image.Metadata[FigureMetadataKeys.Ordinal] = figureNumber.Value;
        }

        return image;
    }
}
