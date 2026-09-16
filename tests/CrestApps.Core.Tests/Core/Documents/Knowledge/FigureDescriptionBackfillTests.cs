using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Knowledge;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Knowledge;

public sealed class FigureDescriptionBackfillTests
{
    private const string DataSourceId = "data-source-1";
    private const string VisionDeploymentName = "gpt-vision";
    private const string IndexerVisionDeploymentName = "indexer-vision";

    /// <summary>
    /// Verifies that a figure ingestion left pending is transcribed, becomes findable by what the
    /// transcription says, and is re-queued so the index learns about it.
    /// </summary>
    [Fact]
    public async Task Backfill_PendingFigure_DescribedAndRequeued()
    {
        var harness = new Harness();

        harness.Store.Seed(CreatePendingFigure("figure:key:1:0", "hash-1"));
        harness.Analysis.Result = ImageAnalysisResult.Succeeded(
            caption: null,
            description: "A bar chart of the measured values.",
            ocrText: "Steel 400",
            detectedEntities: null,
            rawAnalysis: null);

        var described = await harness.Service.BackfillAsync(CreateDataSource(), TestContext.Current.CancellationToken);

        Assert.Equal(1, described);

        var entry = Assert.Single(harness.Store.All);

        Assert.Equal(KnowledgeObjectStatus.Ready, entry.Status);
        Assert.True(entry.TryGet<FigureDetails>(out var details));
        Assert.Equal("A bar chart of the measured values.\nSteel 400", details.Description);
        Assert.Equal(VisionDeploymentName, details.DescriptionModel);
        Assert.Contains("Steel 400", entry.Content, StringComparison.Ordinal);
        Assert.Contains("Figure 1. The measurements.", entry.Content, StringComparison.Ordinal);

        Assert.Equal(["figure:key:1:0"], harness.Queue.Synced);
    }

    /// <summary>
    /// Verifies that a figure whose transcription threw is marked failed and left alone, because retrying a
    /// picture a model cannot read spends money on the same answer.
    /// </summary>
    [Fact]
    public async Task Backfill_AnalyzeThrows_MarksFailedNotRetried()
    {
        var harness = new Harness();

        harness.Store.Seed(CreatePendingFigure("figure:key:1:0", "hash-1"));
        harness.Analysis.Throw = new InvalidOperationException("The model refused the image.");

        var described = await harness.Service.BackfillAsync(CreateDataSource(), TestContext.Current.CancellationToken);

        Assert.Equal(0, described);

        var entry = Assert.Single(harness.Store.All);

        Assert.Equal(KnowledgeObjectStatus.Failed, entry.Status);
        Assert.True(entry.TryGet<FigureDetails>(out var details));
        Assert.Equal("The model refused the image.", details.Error);
        Assert.Null(details.Description);

        // A second pass finds nothing pending, so the failure is never retried on its own.
        harness.Analysis.Calls = 0;

        await harness.Service.BackfillAsync(CreateDataSource(), TestContext.Current.CancellationToken);

        Assert.Equal(0, harness.Analysis.Calls);
    }

    /// <summary>
    /// Verifies that a figure whose attempt never reached the model stays pending and is tried again.
    /// </summary>
    /// <remarks>
    /// A dropped connection or a timeout says nothing about whether the picture is readable, and a figure
    /// marked failed is never retried on its own. Retiring one over a transport fault loses it for good, so
    /// only an answer from the model retires a figure.
    /// </remarks>
    [Fact]
    public async Task Backfill_TransportFaultLeavesTheFigurePending()
    {
        var harness = new Harness();

        harness.Store.Seed(CreatePendingFigure("figure:key:1:0", "hash-1"));
        harness.Analysis.Throw = new HttpRequestException("The connection was reset.");

        var described = await harness.Service.BackfillAsync(CreateDataSource(), TestContext.Current.CancellationToken);

        Assert.Equal(0, described);

        var entry = Assert.Single(harness.Store.All);

        Assert.Equal(KnowledgeObjectStatus.PendingDescription, entry.Status);

        // Nothing was written, so nothing was queued for re-indexing either.
        Assert.Empty(harness.Queue.Synced);

        // The next pass finds it pending and tries again.
        harness.Analysis.Calls = 0;
        harness.Analysis.Throw = null;
        harness.Analysis.Result = ImageAnalysisResult.Succeeded(
            caption: null,
            description: "A bar chart of the measured values.",
            ocrText: null,
            detectedEntities: null,
            rawAnalysis: null);

        var retried = await harness.Service.BackfillAsync(CreateDataSource(), TestContext.Current.CancellationToken);

        Assert.Equal(1, retried);
        Assert.Equal(1, harness.Analysis.Calls);
        Assert.Equal(KnowledgeObjectStatus.Ready, Assert.Single(harness.Store.All).Status);
    }

    /// <summary>
    /// Verifies that an identical picture already transcribed under the current prompt is copied rather than
    /// paid for again.
    /// </summary>
    [Fact]
    public async Task Backfill_CacheHitByHash_NoModelCall()
    {
        var harness = new Harness();

        var described = CreatePendingFigure("figure:key:1:0", "hash-1");
        described.Status = KnowledgeObjectStatus.Ready;
        described.Put(new FigureDetails
        {
            Caption = "Figure 1. The measurements.",
            Description = "Already transcribed.",
            DescriptionPromptVersion = "1",
        });

        harness.Store.Seed(described);
        harness.Store.Seed(CreatePendingFigure("figure:other:1:0", "hash-1"));

        var count = await harness.Service.BackfillAsync(CreateDataSource(), TestContext.Current.CancellationToken);

        Assert.Equal(1, count);
        Assert.Equal(0, harness.Analysis.Calls);

        var entry = Assert.Single(harness.Store.All, item => item.CanonicalId == "figure:other:1:0");

        Assert.Equal(KnowledgeObjectStatus.Ready, entry.Status);
        Assert.True(entry.TryGet<FigureDetails>(out var details));
        Assert.Equal("Already transcribed.", details.Description);
    }

    /// <summary>
    /// Verifies that a host with no vision deployment is a supported setup: the figures stay pending, keep
    /// their captions, and nothing fails.
    /// </summary>
    [Fact]
    public async Task Backfill_NoVisionDeployment_LeavesFiguresPending()
    {
        var harness = new Harness
        {
            HasVisionDeployment = false,
        };

        harness.Store.Seed(CreatePendingFigure("figure:key:1:0", "hash-1"));

        var described = await harness.Service.BackfillAsync(CreateDataSource(), TestContext.Current.CancellationToken);

        Assert.Equal(0, described);
        Assert.Equal(0, harness.Analysis.Calls);
        Assert.Equal(KnowledgeObjectStatus.PendingDescription, Assert.Single(harness.Store.All).Status);
        Assert.Empty(harness.Queue.Synced);
    }

    /// <summary>
    /// Verifies that a figure an indexer produced is transcribed by the model that indexer was configured
    /// with, so a per-indexer choice takes effect for the one thing it exists to control.
    /// </summary>
    [Fact]
    public async Task Backfill_UsesIndexerVisionDeploymentWhenSet()
    {
        var harness = new Harness();

        var figure = CreatePendingFigure("figure:key:1:0", "hash-1");
        figure.IndexerId = "indexer-1";

        harness.Store.Seed(figure);
        harness.Analysis.Result = ImageAnalysisResult.Succeeded(null, "A figure.", null, null, null);

        await harness.Service.BackfillAsync(CreateDataSource(), TestContext.Current.CancellationToken);

        var entry = Assert.Single(harness.Store.All);

        Assert.True(entry.TryGet<FigureDetails>(out var details));
        Assert.Equal(IndexerVisionDeploymentName, details.DescriptionModel);
    }

    /// <summary>
    /// Verifies that a manual upload, which has no indexer, is transcribed by the host's own model.
    /// </summary>
    [Fact]
    public async Task Backfill_ManualUpload_UsesTheHostVisionDeployment()
    {
        var harness = new Harness();

        harness.Store.Seed(CreatePendingFigure("figure:key:1:0", "hash-1"));
        harness.Analysis.Result = ImageAnalysisResult.Succeeded(null, "A figure.", null, null, null);

        await harness.Service.BackfillAsync(CreateDataSource(), TestContext.Current.CancellationToken);

        var entry = Assert.Single(harness.Store.All);

        Assert.True(entry.TryGet<FigureDetails>(out var details));
        Assert.Equal(VisionDeploymentName, details.DescriptionModel);
    }

    /// <summary>
    /// Verifies that an indexer whose vision deployment was deleted still gets its figures transcribed, by
    /// the application's own model. A model choice that disappeared must cost a setting, not the work.
    /// </summary>
    [Fact]
    public async Task Backfill_DeletedIndexerVisionDeployment_FallsBackToTheSlot()
    {
        var harness = new Harness
        {
            IndexerVisionDeploymentExists = false,
        };

        var figure = CreatePendingFigure("figure:key:1:0", "hash-1");
        figure.IndexerId = "indexer-1";

        harness.Store.Seed(figure);
        harness.Analysis.Result = ImageAnalysisResult.Succeeded(null, "A figure.", null, null, null);

        var described = await harness.Service.BackfillAsync(CreateDataSource(), TestContext.Current.CancellationToken);

        Assert.Equal(1, described);

        var entry = Assert.Single(harness.Store.All);

        Assert.Equal(KnowledgeObjectStatus.Ready, entry.Status);
        Assert.True(entry.TryGet<FigureDetails>(out var details));
        Assert.Equal(VisionDeploymentName, details.DescriptionModel);
    }

    /// <summary>
    /// Verifies that a sweep only touches ingested data sources.
    /// </summary>
    [Fact]
    public async Task BackfillDue_SkipsDataSourcesThatAreNotIngested()
    {
        var harness = new Harness();

        harness.DataSources.Add(new AIDataSource
        {
            ItemId = "data-source-2",
            Source = AIDataSourceSourceTypes.Web,
            DisplayText = "Crawled",
        });

        harness.Store.Seed(CreatePendingFigure("figure:key:1:0", "hash-1"));
        harness.Analysis.Result = ImageAnalysisResult.Succeeded(null, "A figure.", null, null, null);

        var described = await harness.Service.BackfillDueAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, described);
        Assert.Equal(1, harness.Analysis.Calls);
    }

    private static AIDataSource CreateDataSource()
    {
        return new AIDataSource
        {
            ItemId = DataSourceId,
            Source = AIDataSourceSourceTypes.File,
            DisplayText = "Knowledge",
        };
    }

    private static KnowledgeObject CreatePendingFigure(string canonicalId, string contentHash)
    {
        var entry = new KnowledgeObject
        {
            ItemId = canonicalId,
            Source = DataSourceId,
            CanonicalId = canonicalId,
            ObjectType = KnowledgeContentTypes.Figure,
            RootId = "document:key",
            ParentId = "article:key:1",
            Title = "Figure 1. The measurements.",
            Content = "Figure 1. The measurements.",
            ContentHash = contentHash,
            MediaType = "image/png",
            StoragePath = $"figures/{canonicalId}.png",
            PageStart = 8,
            PageEnd = 8,
            Status = KnowledgeObjectStatus.PendingDescription,
        };

        entry.Put(new FigureDetails
        {
            Caption = "Figure 1. The measurements.",
            Tier = "describe",
        });

        return entry;
    }

    /// <summary>
    /// Wires the backfill service up with an in-memory store, a stub vision service and a recording queue.
    /// </summary>
    private sealed class Harness
    {
        public Harness()
        {
            DataSources.Add(CreateDataSource());

            FileStore.Saved["figures/figure:key:1:0.png"] = [1, 2, 3, 4];
            FileStore.Saved["figures/figure:other:1:0.png"] = [1, 2, 3, 4];
        }

        public List<AIDataSource> DataSources { get; } = [];

        public InMemoryKnowledgeObjectStore Store { get; } = new();

        public RecordingDocumentFileStore FileStore { get; } = new();

        public RecordingIndexingQueue Queue { get; } = new();

        public StubImageAnalysisService Analysis { get; } = new();

        public bool HasVisionDeployment { get; init; } = true;

        public bool IndexerVisionDeploymentExists { get; init; } = true;

        public DefaultFigureDescriptionBackfillService Service => CreateService();

        private DefaultFigureDescriptionBackfillService CreateService()
        {
            var dataSourceStore = new Mock<IAIDataSourceStore>();
            dataSourceStore
                .Setup(store => store.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => DataSources.ToArray());

            var deployment = new AIDeployment
            {
                ItemId = "deployment-1",
                Name = VisionDeploymentName,
            };

            deployment.Put(new AIDeploymentMetadata
            {
                Features = [AIDeploymentFeatureNames.ImageInput],
            });

            var indexerDeployment = new AIDeployment
            {
                ItemId = "deployment-2",
                Name = IndexerVisionDeploymentName,
            };

            indexerDeployment.Put(new AIDeploymentMetadata
            {
                Features = [AIDeploymentFeatureNames.ImageInput],
            });

            var deploymentManager = new Mock<IAIDeploymentManager>();
            deploymentManager
                .Setup(manager => manager.ResolveSlotAsync(
                    AIDeploymentSlotNames.Vision,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(HasVisionDeployment ? deployment : null);
            deploymentManager
                .Setup(manager => manager.FindByNameAsync(IndexerVisionDeploymentName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(IndexerVisionDeploymentExists ? indexerDeployment : null);

            return new DefaultFigureDescriptionBackfillService(
                dataSourceStore.Object,
                Store,
                FileStore,
                deploymentManager.Object,
                new StubVisionDeploymentResolver { DeploymentName = IndexerVisionDeploymentName },
                Analysis,
                Queue,
                Options.Create(new KnowledgeIngestionOptions()),
                NullLogger<DefaultFigureDescriptionBackfillService>.Instance);
        }
    }

    /// <summary>
    /// Answers with the deployment a test says an indexer chose.
    /// </summary>
    private sealed class StubVisionDeploymentResolver : IKnowledgeVisionDeploymentResolver
    {
        public string DeploymentName { get; init; }

        public Task<string> ResolveAsync(string indexerId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.IsNullOrEmpty(indexerId) ? null : DeploymentName);
        }
    }

    /// <summary>
    /// Answers with whatever a test set, and counts how often it was asked.
    /// </summary>
    private sealed class StubImageAnalysisService : IImageAnalysisService
    {
        public int Calls { get; set; }

        public ImageAnalysisResult Result { get; set; }

        public Exception Throw { get; set; }

        public Task<ImageAnalysisResult> AnalyzeAsync(
            Stream imageStream,
            string contentType,
            string fileName,
            string chatDeploymentName = null,
            CancellationToken cancellationToken = default)
        {
            return AnalyzeAsync(new ImageAnalysisRequest(), cancellationToken);
        }

        public Task<ImageAnalysisResult> AnalyzeAsync(ImageAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;

            if (Throw != null)
            {
                throw Throw;
            }

            return Task.FromResult(Result ?? ImageAnalysisResult.Failed("Nothing was configured."));
        }
    }
}
