using CrestApps.Core.AI;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Ingestion;
using CrestApps.Core.AI.Documents.Ingestion.Processors;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Ingestion;

public sealed class FigureDescriptionProcessorTests
{
    private const string VisionDeploymentName = "gpt-vision";

    /// <summary>
    /// Verifies that a host with no vision deployment configured keeps the document as text, says so once,
    /// and never reaches for the model.
    /// </summary>
    [Fact]
    public async Task Process_NoVisionDeployment_ReturnsTextOnlyAndLogsOnce()
    {
        var harness = new Harness(deployment: null);
        var image = Image("doc-p1-1", FigureTiers.Describe);

        await harness.RunAsync(Document(image), DocumentIngestionContext.Default);

        Assert.Equal(0, harness.Analysis.Calls);
        Assert.Null(image.AlternativeText);

        var entry = Assert.Single(harness.Logger.Entries);

        Assert.Equal(LogLevel.Information, entry.Level);
    }

    /// <summary>
    /// Verifies that a resolved deployment which does not accept images counts as no deployment at all. Slot
    /// resolution falls back to the first capable deployment, so a non-null answer is not proof of vision.
    /// </summary>
    [Fact]
    public async Task Process_DeploymentWithoutImageInput_TreatedAsUnavailable()
    {
        var harness = new Harness(CreateDeployment(supportsImageInput: false));
        var image = Image("doc-p1-1", FigureTiers.Describe);

        await harness.RunAsync(Document(image), DocumentIngestionContext.Default);

        Assert.Equal(0, harness.Analysis.Calls);
        Assert.Null(image.AlternativeText);
    }

    /// <summary>
    /// Verifies that turning figure processing off makes no model calls.
    /// </summary>
    [Fact]
    public async Task Process_ModeOff_NoCalls()
    {
        var harness = new Harness(CreateDeployment());
        var image = Image("doc-p1-1", FigureTiers.Describe);

        await harness.RunAsync(
            Document(image),
            new DocumentIngestionContext
            {
                FigureMode = FigureProcessingMode.Off,
            });

        Assert.Equal(0, harness.Analysis.Calls);
    }

    /// <summary>
    /// Verifies that the indexing path describes nothing inline. Vision latency must not gate how soon a
    /// document becomes searchable.
    /// </summary>
    [Fact]
    public async Task Process_InlineFalse_NoCalls()
    {
        var harness = new Harness(CreateDeployment());
        var image = Image("doc-p1-1", FigureTiers.Describe);

        await harness.RunAsync(
            Document(image),
            new DocumentIngestionContext
            {
                DescribeFiguresInline = false,
            });

        Assert.Equal(0, harness.Analysis.Calls);
        Assert.Null(image.AlternativeText);
    }

    /// <summary>
    /// Verifies that the caption, the surrounding text and the document language all reach the model, and
    /// that the transcription and the text read off the figure both land on the element.
    /// </summary>
    [Fact]
    public async Task Process_DescribeTier_CallsAnalyzeWithCaptionContextAndLanguage()
    {
        var harness = new Harness(CreateDeployment());
        var image = Image("doc-p1-1", FigureTiers.Describe);

        image.Metadata[FigureMetadataKeys.Caption] = "Figure 1. Measured against the model.";
        image.Metadata[FigureMetadataKeys.Context] = "The trend line in Figure 1 shows the seasonal drop.";

        await harness.RunAsync(
            Document(image),
            new DocumentIngestionContext
            {
                Language = "hu-HU",
            });

        var request = Assert.Single(harness.Analysis.Requests);

        Assert.Equal("Figure 1. Measured against the model.", request.Caption);
        Assert.Equal("The trend line in Figure 1 shows the seasonal drop.", request.Context);
        Assert.Equal("hu-HU", request.Language);
        Assert.Equal(AITemplateIds.FigureTranscription, request.TemplateId);
        Assert.Equal("image/png", request.ContentType);

        Assert.Equal("A scatter plot.\nR2 = 0,9412", image.AlternativeText);
        Assert.Equal("vision", image.GetMetadataString(FigureMetadataKeys.DescriptionSource));
        Assert.Equal(VisionDeploymentName, image.GetMetadataString(FigureMetadataKeys.DescriptionModel));
        Assert.Equal(
            FigureDescriptionProcessor.FigureTranscriptionPromptVersion,
            image.GetMetadataString(FigureMetadataKeys.DescriptionPromptVersion));
    }

    /// <summary>
    /// Verifies that a figure salience kept but did not promote costs nothing.
    /// </summary>
    [Fact]
    public async Task Process_CaptionOnlyTier_NoCall()
    {
        var harness = new Harness(CreateDeployment());
        var image = Image("doc-p1-1", FigureTiers.CaptionOnly);

        await harness.RunAsync(Document(image), DocumentIngestionContext.Default);

        Assert.Equal(0, harness.Analysis.Calls);
    }

    /// <summary>
    /// Verifies that the same artwork is transcribed once however often it appears.
    /// </summary>
    [Fact]
    public async Task Process_SameHashTwice_OneCall()
    {
        var harness = new Harness(CreateDeployment());
        var first = Image("doc-p1-1", FigureTiers.Describe, contentHash: "shared-hash");
        var second = Image("doc-p2-1", FigureTiers.Describe, contentHash: "shared-hash");

        await harness.RunAsync(Document(first, second), DocumentIngestionContext.Default);

        Assert.Equal(1, harness.Analysis.Calls);
        Assert.Equal(first.AlternativeText, second.AlternativeText);
    }

    /// <summary>
    /// Verifies that a description produced by an older prompt is not served for the current one. Hiding a
    /// prompt change behind a cache would make a template edit look like it did nothing.
    /// </summary>
    [Fact]
    public async Task Process_PromptVersionChange_MissesCache()
    {
        var harness = new Harness(CreateDeployment());

        await harness.Cache.SetAsync("shared-hash", "an-older-prompt-version", "stale description", TestContext.Current.CancellationToken);

        var image = Image("doc-p1-1", FigureTiers.Describe, contentHash: "shared-hash");

        await harness.RunAsync(Document(image), DocumentIngestionContext.Default);

        Assert.Equal(1, harness.Analysis.Calls);
        Assert.DoesNotContain("stale", image.AlternativeText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that one failing figure costs only that figure. The others are still transcribed and the
    /// document still ingests.
    /// </summary>
    [Fact]
    public async Task Process_AnalyzeThrows_DemotesToCaptionOnlyAndContinues()
    {
        var harness = new Harness(CreateDeployment());

        harness.Analysis.ThrowForFileName = "doc-p2-1";

        var first = Image("doc-p1-1", FigureTiers.Describe, contentHash: "hash-1");
        var failing = Image("doc-p2-1", FigureTiers.Describe, contentHash: "hash-2");
        var third = Image("doc-p3-1", FigureTiers.Describe, contentHash: "hash-3");

        await harness.RunAsync(Document(first, failing, third), DocumentIngestionContext.Default);

        Assert.NotNull(first.AlternativeText);
        Assert.NotNull(third.AlternativeText);
        Assert.Null(failing.AlternativeText);
        Assert.Equal(FigureTiers.CaptionOnly, failing.GetMetadataString(FigureMetadataKeys.Tier));
        Assert.Contains(harness.Logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    /// <summary>
    /// Verifies that the per-document budget is a hard stop on calls, not merely a hint the earlier scoring
    /// was supposed to honour.
    /// </summary>
    [Fact]
    public async Task Process_BudgetExceeded_StopsCalling()
    {
        var harness = new Harness(CreateDeployment());
        var first = Image("doc-p1-1", FigureTiers.Describe, contentHash: "hash-1");
        var second = Image("doc-p2-1", FigureTiers.Describe, contentHash: "hash-2");

        await harness.RunAsync(
            Document(first, second),
            new DocumentIngestionContext
            {
                MaxFigureDescriptionsPerDocument = 1,
            });

        Assert.Equal(1, harness.Analysis.Calls);
        Assert.Equal(FigureTiers.CaptionOnly, second.GetMetadataString(FigureMetadataKeys.Tier));
    }

    private static AIDeployment CreateDeployment(bool supportsImageInput = true)
    {
        var deployment = new AIDeployment
        {
            ItemId = "deployment-vision",
            Name = VisionDeploymentName,
        };

        deployment.Put(new AIDeploymentMetadata
        {
            Features = supportsImageInput
                ? [AIDeploymentFeatureNames.ImageInput]
                : [AIDeploymentFeatureNames.TextGeneration],
        });

        return deployment;
    }

    private static IngestionDocument Document(params IngestionDocumentImage[] images)
    {
        var document = new IngestionDocument("report.pdf");

        foreach (var image in images)
        {
            var section = new IngestionDocumentSection
            {
                PageNumber = image.PageNumber,
            };

            section.Elements.Add(image);
            document.Sections.Add(section);
        }

        return document;
    }

    private static IngestionDocumentImage Image(string figureId, string tier, string contentHash = "hash-1")
    {
        var image = new IngestionDocumentImage($"![]({figureId})")
        {
            Content = new byte[] { 1, 2, 3, 4 },
            MediaType = "image/png",
            PageNumber = 1,
        };

        image.Metadata[FigureMetadataKeys.Id] = figureId;
        image.Metadata[FigureMetadataKeys.ContentHash] = contentHash;
        image.Metadata[FigureMetadataKeys.Tier] = tier;

        return image;
    }

    /// <summary>
    /// Wires the processor up with a deployment manager, a recording analysis service and a real memory cache.
    /// </summary>
    private sealed class Harness
    {
        private readonly FigureDescriptionProcessor _processor;

        /// <summary>
        /// Initializes a new instance of the <see cref="Harness"/> class.
        /// </summary>
        /// <param name="deployment">The deployment the vision slot resolves to, or <see langword="null"/>.</param>
        public Harness(AIDeployment deployment)
        {
            var deploymentManager = new Mock<IAIDeploymentManager>();
            deploymentManager
                .Setup(manager => manager.ResolveSlotAsync(
                    AIDeploymentSlotNames.Vision,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(deployment);

            Cache = new MemoryFigureDescriptionCache(new MemoryCache(Options.Create(new MemoryCacheOptions())));

            _processor = new FigureDescriptionProcessor(
                deploymentManager.Object,
                Analysis,
                Cache,
                Logger);
        }

        /// <summary>
        /// Gets the recording analysis service.
        /// </summary>
        public RecordingImageAnalysisService Analysis { get; } = new();

        /// <summary>
        /// Gets the description cache.
        /// </summary>
        public MemoryFigureDescriptionCache Cache { get; }

        /// <summary>
        /// Gets the captured log entries.
        /// </summary>
        public CapturingLogger<FigureDescriptionProcessor> Logger { get; } = new();

        /// <summary>
        /// Runs the processor.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <param name="context">The per-run options.</param>
        public Task<IngestionDocument> RunAsync(IngestionDocument document, DocumentIngestionContext context)
        {
            return _processor.ProcessAsync(document, context, TestContext.Current.CancellationToken);
        }
    }

    private sealed class RecordingImageAnalysisService : IImageAnalysisService
    {
        public List<ImageAnalysisRequest> Requests { get; } = [];

        public int Calls => Requests.Count;

        public string ThrowForFileName { get; set; }

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
            Requests.Add(request);

            if (string.Equals(request.FileName, ThrowForFileName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The vision call failed.");
            }

            return Task.FromResult(ImageAnalysisResult.Succeeded(
                caption: "A scatter plot",
                description: "A scatter plot.",
                ocrText: "R2 = 0,9412",
                detectedEntities: "x axis, y axis",
                rawAnalysis: "{}"));
        }
    }
}
