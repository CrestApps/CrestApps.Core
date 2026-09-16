using CrestApps.Core.AI;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Ingestion;
using CrestApps.Core.AI.Documents.Ingestion.Processors;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Pdf.Services;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Services;

/// <summary>
/// Covers what the chat upload path does with the figures an ingested document carries.
/// </summary>
public sealed class DefaultAIDocumentProcessingFigureTests
{
    private const string Caption = "Figure 1. Measured values against the model.";

    private const string Description = "A bar chart of six measured series against the modelled baseline.";

    /// <summary>
    /// Verifies that a caption is printed once, with its figure, rather than a second time as a paragraph of
    /// its own.
    /// </summary>
    [Fact]
    public async Task ProcessFileAsync_CaptionParagraph_NotEmittedTwice()
    {
        var harness = new Harness();

        var result = await harness.ProcessAsync(CreateFigurePdf());

        Assert.True(result.Success, result.Error);

        var text = string.Join('\n', result.Chunks.Select(chunk => chunk.Content));

        Assert.Equal(1, CountOccurrences(text, Caption));
        Assert.Contains("[figure", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the figure bytes are stored and recorded on the document, so a later tool can show the
    /// picture without re-reading the upload.
    /// </summary>
    [Fact]
    public async Task ProcessFileAsync_StoresFigureBytesAndRecordsList()
    {
        var harness = new Harness();

        var result = await harness.ProcessAsync(CreateFigurePdf());

        Assert.True(result.Success, result.Error);
        Assert.True(result.Document.TryGet(out DocumentFigureList figures));

        var figure = Assert.Single(figures.Figures);

        Assert.Equal(1, figure.Page);
        Assert.Equal(Caption, figure.Caption);
        Assert.Equal("image/png", figure.MediaType);
        Assert.Contains("figures/", figure.StoragePath, StringComparison.Ordinal);

        var saved = Assert.Single(harness.FileStore.Saved);

        Assert.Equal(figure.StoragePath, saved.Key);
        Assert.NotEmpty(saved.Value);
    }

    /// <summary>
    /// Verifies that a transcribed figure survives chunking as one piece: the identifier, the page, the
    /// caption and the description all land in the same chunk, so a retrieval hit on a printed value carries
    /// the figure it came from.
    /// </summary>
    [Fact]
    public async Task ProcessFileAsync_DescribedFigure_FigureBlockSurvivesAsOneChunk()
    {
        var harness = new Harness(describeFigures: true);

        var result = await harness.ProcessAsync(CreateFigurePdf());

        Assert.True(result.Success, result.Error);

        var chunk = Assert.Single(result.Chunks, chunk => chunk.Content.Contains("[figure", StringComparison.Ordinal));

        Assert.Contains(Caption, chunk.Content, StringComparison.Ordinal);
        Assert.Contains(Description, chunk.Content, StringComparison.Ordinal);
        Assert.Contains("[/figure]", chunk.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a figure block split across a chunk boundary has its opening line repeated, so the
    /// continuation is not an orphaned fragment that names no figure.
    /// </summary>
    [Fact]
    public async Task ProcessFileAsync_SplitFigureBlock_ContinuationRepeatsHeader()
    {
        var harness = new Harness(textNormalizer: new LineSplittingTextNormalizer());

        var result = await harness.ProcessAsync(CreateFigurePdf());

        Assert.True(result.Success, result.Error);

        var closing = Assert.Single(result.Chunks, chunk => chunk.Content.Contains("[/figure]", StringComparison.Ordinal));

        Assert.StartsWith("[figure", closing.Content, StringComparison.Ordinal);
        Assert.Contains(Caption, closing.Content, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        return text.Split(value).Length - 1;
    }

    /// <summary>
    /// Builds a one page fixture holding a body paragraph, a chart and the caption printed under it.
    /// </summary>
    /// <returns>The PDF bytes.</returns>
    private static byte[] CreateFigurePdf()
    {
        var chart = TestImageFactory.BarChart(200, 200);

        return new PdfFixtureBuilder()
            .Page(page =>
            {
                for (var line = 0; line < 6; line++)
                {
                    page.Text($"Body line {line} of running prose that sets the page typography. ", 40, 760 - (line * 14), 11);
                }

                page.Png(chart, 40, 500, 200, 120);
                page.Text(Caption + " ", 40, 480, 9);
            })
            .BuildBytes();
    }

    /// <summary>
    /// Assembles the upload path with the PDF reader and both figure processors wired up.
    /// </summary>
    private sealed class Harness
    {
        private readonly DefaultAIDocumentProcessingService _service;

        /// <summary>
        /// Initializes a new instance of the <see cref="Harness"/> class.
        /// </summary>
        /// <param name="textNormalizer">The normalizer to chunk with, or <see langword="null"/> for the default.</param>
        /// <param name="describeFigures">Whether a stubbed vision model transcribes the figures.</param>
        public Harness(IAITextNormalizer textNormalizer = null, bool describeFigures = false)
        {
            var options = new ChatDocumentsOptions();
            options.Add(".pdf");

            var services = new ServiceCollection();
            services.AddSingleton<PdfIngestionDocumentReader>();
            services.AddKeyedSingleton<IngestionDocumentReader>(
                ".pdf",
                (sp, _) => sp.GetRequiredService<PdfIngestionDocumentReader>());

            var serviceProvider = services.BuildServiceProvider();
            var captionOptions = Options.Create(new CaptionPatternOptions());

            var processors = new List<IngestionDocumentProcessor>
            {
                new FigureCaptionProcessor(
                    new DefaultFigureCaptionCandidateDetector(captionOptions),
                    new DefaultFigureCaptionResolver(captionOptions),
                    captionOptions),
                new FigureSalienceProcessor(Options.Create(new FigureSalienceOptions()), captionOptions),
            };

            if (describeFigures)
            {
                processors.Add(new FigureDescriptionProcessor(
                    CreateDeploymentManager(),
                    new StubImageAnalysisService(),
                    new MemoryFigureDescriptionCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
                    NullLogger<FigureDescriptionProcessor>.Instance));
            }

            _service = new DefaultAIDocumentProcessingService(
                new DefaultAIDocumentIngestionPipeline(new DefaultIngestionDocumentReaderResolver(serviceProvider), processors),
                textNormalizer ?? new DefaultAITextNormalizer(),
                FileStore,
                Options.Create(options),
                TimeProvider.System,
                NullLogger<DefaultAIDocumentProcessingService>.Instance);
        }

        /// <summary>
        /// Gets the store the figure bytes are written to.
        /// </summary>
        public RecordingDocumentFileStore FileStore { get; } = new();

        /// <summary>
        /// Processes one uploaded PDF.
        /// </summary>
        /// <param name="content">The PDF bytes.</param>
        /// <returns>The processing result.</returns>
        public Task<DocumentProcessingResult> ProcessAsync(byte[] content)
        {
            var file = new FormFile(new MemoryStream(content), 0, content.Length, "file", "report.pdf")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/pdf",
            };

            return _service.ProcessFileAsync(file, "ref-1", AIReferenceTypes.Document.ChatInteraction, embeddingGenerator: null);
        }

        private static IAIDeploymentManager CreateDeploymentManager()
        {
            var deployment = new AIDeployment
            {
                ItemId = "deployment-vision",
                Name = "gpt-vision",
            };

            deployment.Put(new AIDeploymentMetadata
            {
                Features = [AIDeploymentFeatureNames.ImageInput],
            });

            var manager = new Mock<IAIDeploymentManager>();
            manager
                .Setup(instance => instance.ResolveSlotAsync(
                    AIDeploymentSlotNames.Vision,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(deployment);

            return manager.Object;
        }
    }

    /// <summary>
    /// Stands in for a vision model, returning a fixed transcription.
    /// </summary>
    private sealed class StubImageAnalysisService : IImageAnalysisService
    {
        public Task<ImageAnalysisResult> AnalyzeAsync(
            Stream imageStream,
            string contentType,
            string fileName,
            string chatDeploymentName = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The stream overload is not used by the figure description processor.");
        }

        public Task<ImageAnalysisResult> AnalyzeAsync(ImageAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ImageAnalysisResult.Succeeded(
                caption: "A bar chart",
                description: Description,
                ocrText: string.Empty,
                detectedEntities: "x axis, y axis",
                rawAnalysis: "{}"));
        }
    }

    /// <summary>
    /// Chunks on every line, which is the smallest chunk size that still makes sense and forces a figure
    /// block to straddle a boundary.
    /// </summary>
    private sealed class LineSplittingTextNormalizer : IAITextNormalizer
    {
        public Task<string> NormalizeContentAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(text);
        }

        public Task<List<string>> NormalizeAndChunkAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(text.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList());
        }

        public string NormalizeTitle(string title)
        {
            return title;
        }
    }
}
