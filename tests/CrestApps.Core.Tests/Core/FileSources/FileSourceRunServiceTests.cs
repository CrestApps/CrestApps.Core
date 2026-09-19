using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.FileSources;
using CrestApps.Core.AI.FileSources.Connectors;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Services;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.FileSources;

/// <summary>
/// Covers what a run records, how much of a source one run takes on, and how the next one picks up where it
/// left off.
/// </summary>
public sealed class FileSourceRunServiceTests : IDisposable
{
    private const string DataSourceId = "data-source-1";

    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSourceRunServiceTests"/> class.
    /// </summary>
    public FileSourceRunServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "crestapps-runservice-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// Removes the temporary folder.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A file left open by the run under test is not a test failure.
        }
    }

    /// <summary>
    /// Verifies that what a run did is recorded on the source, because an unattended job that says nothing
    /// about itself cannot be operated.
    /// </summary>
    [Fact]
    public async Task RunSummary_IsPersistedOnSource()
    {
        await WriteAsync("first.txt", "The first file.");

        var harness = new Harness(_root);
        var source = harness.CreateFileSource();

        await harness.Service.RunAsync(source, TestContext.Current.CancellationToken);

        var saved = harness.Saved[^1];

        Assert.True(saved.TryGet<FileSourceRunSummary>(out var summary));
        Assert.Equal(FileSourceRunStatus.Succeeded, summary.Status);
        Assert.Equal(1, summary.ItemsDiscovered);
        Assert.Equal(1, summary.ItemsIndexed);
        Assert.True(summary.DiscoveryCompleted);
        Assert.NotNull(summary.CompletedUtc);
    }

    /// <summary>
    /// Verifies that a run which could not start says so, rather than looking like one that found nothing.
    /// </summary>
    [Fact]
    public async Task RunSummary_NoConnector_IsRecordedAsFailed()
    {
        var harness = new Harness(_root)
        {
            ResolveConnector = false,
        };

        var summary = await harness.Service.RunAsync(harness.CreateFileSource(), TestContext.Current.CancellationToken);

        Assert.Equal(FileSourceRunStatus.Failed, summary.Status);
        Assert.Contains("connector", summary.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that a run which listed only part of its source is recorded as partial, which is also what
    /// stops it removing anything.
    /// </summary>
    [Fact]
    public async Task RunSummary_PartialListing_IsRecordedAsPartiallyCompleted()
    {
        for (var index = 0; index < 5; index++)
        {
            await WriteAsync($"file-{index}.txt", $"The body of file {index}.");
        }

        var harness = new Harness(_root);
        var source = harness.CreateFileSource(maxItems: 2);

        var summary = await harness.Service.RunAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(FileSourceRunStatus.PartiallyCompleted, summary.Status);
        Assert.False(summary.DiscoveryCompleted);
        Assert.Equal(0, summary.ItemsDeleted);
        Assert.False(string.IsNullOrWhiteSpace(summary.DiscoveryCursor));
    }

    /// <summary>
    /// Verifies that a source larger than one run will take on is worked through across runs, and that
    /// nothing is read twice or skipped.
    /// </summary>
    [Fact]
    public async Task Run_LargeSource_RespectsTheBudgetAndResumesFromTheCursor()
    {
        const int Files = 25;

        for (var index = 0; index < Files; index++)
        {
            await WriteAsync($"file-{index:D3}.txt", $"The body of file {index}.");
        }

        var harness = new Harness(_root);
        var source = harness.CreateFileSource(maxItems: 10);
        var indexed = 0;

        for (var run = 0; run < 4; run++)
        {
            var summary = await harness.Service.RunAsync(source, TestContext.Current.CancellationToken);

            indexed += summary.ItemsIndexed;

            Assert.True(summary.ItemsIndexed <= 10, "A run must never take on more than its budget.");
            Assert.Equal(0, summary.ItemsDeleted);
        }

        // Every file was read exactly once: one document object per file.
        Assert.Equal(Files, indexed);
        Assert.Equal(Files, harness.Store.All.Count(entry => entry.ObjectType == KnowledgeObjectTypes.Document));
    }

    /// <summary>
    /// Verifies that a file whose content changed replaces the document it produced last time. A document's
    /// identifier comes from its bytes, so a revised file is a new document, and the old one would otherwise
    /// stay searchable beside it forever.
    /// </summary>
    [Fact]
    public async Task Run_ChangedFile_RemovesTheDocumentItProducedBefore()
    {
        await WriteAsync("report.txt", "The first version of the report.");

        var harness = new Harness(_root);
        var source = harness.CreateFileSource();

        await harness.Service.RunAsync(source, TestContext.Current.CancellationToken);

        var firstRoot = Assert.Single(harness.Store.All, entry => entry.ObjectType == KnowledgeObjectTypes.Document).RootId;

        // A later write with different bytes; the modified time moves too, so the change token changes.
        await Task.Delay(20, TestContext.Current.CancellationToken);
        await WriteAsync("report.txt", "The second, revised version of the report with more words in it.");

        var summary = await harness.Service.RunAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.ItemsIndexed);

        var documents = harness.Store.All.Where(entry => entry.ObjectType == KnowledgeObjectTypes.Document).ToList();

        Assert.Single(documents);
        Assert.NotEqual(firstRoot, documents[0].RootId);
        Assert.DoesNotContain(harness.Store.All, entry => entry.RootId == firstRoot);
    }

    /// <summary>
    /// Verifies that moving a file without changing a byte keeps the document it produced.
    /// </summary>
    /// <remarks>
    /// A document's identity comes from the bytes, so the new path produces exactly the document the old
    /// path produced. The run removes a document only when the last item producing it is gone, and it reads
    /// that from its own snapshot of per-item state — so an item first seen on this run has to join the
    /// snapshot. Without that, the old path looks like the last producer and the run deletes the document it
    /// has just ingested, leaving the data source empty and reporting success.
    /// </remarks>
    [Fact]
    public async Task Run_MovedFile_KeepsTheDocumentItStillProduces()
    {
        await WriteAsync("report.txt", "The only version of the report.");

        var harness = new Harness(_root);
        var source = harness.CreateFileSource();

        await harness.Service.RunAsync(source, TestContext.Current.CancellationToken);

        var firstRoot = Assert.Single(harness.Store.All, entry => entry.ObjectType == KnowledgeObjectTypes.Document).RootId;

        // The same bytes under a different name, which is what a move looks like to a folder listing.
        File.Delete(Path.Combine(_root, "report.txt"));
        await WriteAsync("archive-report.txt", "The only version of the report.");

        await harness.Service.RunAsync(source, TestContext.Current.CancellationToken);

        var documents = harness.Store.All.Where(entry => entry.ObjectType == KnowledgeObjectTypes.Document).ToList();

        Assert.Single(documents);
        Assert.Equal(firstRoot, documents[0].RootId);
    }

    /// <summary>
    /// Verifies that a file source pointed at a data source of another kind does not run. The objects a run
    /// produces are read back only by the Ingested source handler; filling a Web data source with them would
    /// store knowledge nothing ever indexes.
    /// </summary>
    [Fact]
    public async Task Run_DataSourceIsNotIngested_IsRecordedAsFailed()
    {
        await WriteAsync("first.txt", "The first file.");

        var harness = new Harness(_root)
        {
            DataSourceType = AIDataSourceSourceTypes.Web,
        };

        var summary = await harness.Service.RunAsync(harness.CreateFileSource(), TestContext.Current.CancellationToken);

        Assert.Equal(FileSourceRunStatus.Failed, summary.Status);
        Assert.Contains("Ingested", summary.Error, StringComparison.Ordinal);
        Assert.Empty(harness.Store.All);
    }

    private async Task WriteAsync(string name, string content)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, name), content, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Runs the real local-folder connector and ingestion pipeline against an in-memory store, recording what
    /// gets written back onto the source.
    /// </summary>
    private sealed class Harness
    {
        private readonly string _root;

        public Harness(string root)
        {
            _root = root;

            var options = new FileSystemConnectorOptions();

            options.AllowedRoots.Add(root);

            Connector = new FileSystemIngestionConnector(
                Options.Create(new FileSourceOptions()),
                Options.Create(options),
                NullLogger<FileSystemIngestionConnector>.Instance);
        }

        public FileSystemIngestionConnector Connector { get; }

        public InMemoryKnowledgeObjectStore Store { get; } = new();

        public InMemoryIngestionItemStateStore StateStore { get; } = new();

        public List<FileSource> Saved => [.. SourceProvider.Saved.OfType<FileSource>()];

        public RecordingIngestionSourceProvider SourceProvider { get; } = new();

        public bool ResolveConnector { get; init; } = true;

        public string DataSourceType { get; init; } = AIDataSourceSourceTypes.File;

        public DefaultIngestionRunService Service => Build();

        /// <summary>
        /// Builds a file source pointed at the folder. The same instance is reused across runs, which is how the
        /// cursor from one run reaches the next.
        /// </summary>
        /// <param name="maxItems">The per-run ceiling, when the test sets one.</param>
        /// <returns>The source.</returns>
        public FileSource CreateFileSource(int? maxItems = null)
        {
            var source = new FileSource
            {
                ItemId = "file-source-1",
                Source = FileSystemIngestionConnector.ConnectorName,
                DisplayText = "The folder",
                AIDataSourceId = DataSourceId,
                Enabled = true,
            };

            source.Put(new FileSystemFileSourceMetadata
            {
                RootPath = _root,
                MaxItems = maxItems,
            });

            if (maxItems.HasValue)
            {
                source.Put(new FileSourceMetadata
                {
                    MaxItemsPerRun = maxItems,
                });
            }

            return source;
        }

        private DefaultIngestionRunService Build()
        {
            var ingestionService = FileSourceTestPipeline.Create(Store);

            var dataSourceStore = new Mock<IAIDataSourceStore>();
            dataSourceStore
                .Setup(store => store.FindByIdAsync(DataSourceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AIDataSource
                {
                    ItemId = DataSourceId,
                    Source = DataSourceType,
                    DisplayText = "Knowledge",
                });

            var resolver = new Mock<IIngestionConnectorResolver>();
            resolver
                .Setup(instance => instance.Get(It.IsAny<string>()))
                .Returns(ResolveConnector ? Connector : null);

            return new DefaultIngestionRunService(
                resolver.Object,
                StateStore,
                [SourceProvider],
                ingestionService,
                dataSourceStore.Object,
                Options.Create(new FileSourceOptions()),
                TimeProvider.System,
                NullLogger<DefaultIngestionRunService>.Instance);
        }
    }
}
