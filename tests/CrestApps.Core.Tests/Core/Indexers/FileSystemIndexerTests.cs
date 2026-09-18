using System.Text;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Documents.Ingestion;
using CrestApps.Core.AI.Documents.Knowledge;
using CrestApps.Core.AI.Documents.Knowledge.Structure;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.FileSources;
using CrestApps.Core.AI.FileSources.Connectors;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Models;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Indexers;

/// <summary>
/// Covers continuous intake from a folder on the host: what it lists, what it refuses to read, and the one
/// rule the whole subsystem turns on — a partial listing never deletes anything.
/// </summary>
public sealed class FileSystemIndexerTests : IDisposable
{
    private const string DataSourceId = "data-source-1";

    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemIndexerTests"/> class.
    /// </summary>
    public FileSystemIndexerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "crestapps-indexer-" + Guid.NewGuid().ToString("N"));

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
    /// Verifies that a folder nobody allowed is refused, so the indexer screen is not a way to read any file
    /// the host process can open.
    /// </summary>
    [Fact]
    public async Task Discover_RootOutsideAllowedRoots_IsRefusedAndIncomplete()
    {
        var connector = CreateConnector(allowed: false);

        var result = await connector.DiscoverAsync(CreateIndexer(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Items);

        var validation = new ValidationResultDetails();

        await connector.ValidateAsync(CreateIndexer(), validation, TestContext.Current.CancellationToken);

        Assert.False(validation.Succeeded);
    }

    /// <summary>
    /// Verifies that every matching file is listed with a token that changes when the file does.
    /// </summary>
    [Fact]
    public async Task Discover_ListsFilesWithAChangeToken()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "first.txt"), "The first file.", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_root, "second.txt"), "The second file.", TestContext.Current.CancellationToken);

        var connector = CreateConnector();
        var result = await connector.DiscoverAsync(CreateIndexer(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.ChangeToken)));

        var before = result.Items.Single(item => item.ItemId == "first.txt").ChangeToken;

        await File.WriteAllTextAsync(Path.Combine(_root, "first.txt"), "The first file, revised and longer.", TestContext.Current.CancellationToken);

        var after = await connector.DiscoverAsync(CreateIndexer(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(before, after.Items.Single(item => item.ItemId == "first.txt").ChangeToken);
    }

    /// <summary>
    /// Verifies that an item identifier that climbs out of the indexed folder reads nothing, whatever it was
    /// stored as.
    /// </summary>
    [Fact]
    public async Task Fetch_ItemIdOutsideRoot_ReturnsNothing()
    {
        var connector = CreateConnector();

        var content = await connector.FetchAsync(CreateIndexer(), "../../secrets.txt", TestContext.Current.CancellationToken);

        Assert.Null(content);
    }

    /// <summary>
    /// Verifies that a folder of files becomes typed knowledge in the data source, end to end, through the
    /// real pipeline.
    /// </summary>
    [Fact]
    public async Task Run_FileSystem_PopulatesTheDataSource()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "report.txt"), "The rig was measured at three loads.", TestContext.Current.CancellationToken);

        var harness = new Harness(_root);

        var summary = await harness.RunAsync();

        Assert.Equal(1, summary.ItemsDiscovered);
        Assert.Equal(1, summary.ItemsIndexed);
        Assert.Equal(0, summary.ItemsFailed);
        Assert.True(summary.DiscoveryCompleted);

        Assert.Contains(harness.Store.All, entry => entry.ObjectType == KnowledgeObjectTypes.Document);
        Assert.Contains(harness.Store.All, entry => entry.ObjectType == KnowledgeObjectTypes.Text);
        Assert.NotEmpty(harness.Queue.Synced);
    }

    /// <summary>
    /// Verifies that a second run over unchanged files ingests nothing again, so an indexer is cheap to run
    /// often.
    /// </summary>
    [Fact]
    public async Task Run_UnchangedFile_IsNotIngestedTwice()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "report.txt"), "The rig was measured.", TestContext.Current.CancellationToken);

        var harness = new Harness(_root);

        await harness.RunAsync();

        var second = await harness.RunAsync();

        Assert.Equal(0, second.ItemsIndexed);
    }

    /// <summary>
    /// Verifies that a file removed from the folder takes its knowledge with it.
    /// </summary>
    [Fact]
    public async Task Run_RemovedFile_IsDeletedFromTheDataSource()
    {
        var path = Path.Combine(_root, "report.txt");

        await File.WriteAllTextAsync(path, "The rig was measured.", TestContext.Current.CancellationToken);

        var harness = new Harness(_root);

        await harness.RunAsync();

        Assert.NotEmpty(harness.Store.All);

        File.Delete(path);

        var second = await harness.RunAsync();

        Assert.Equal(1, second.ItemsDeleted);
        Assert.Empty(harness.Store.All);
    }

    /// <summary>
    /// Verifies that a listing the connector could not complete never deletes anything. This is the
    /// highest-consequence rule in the intake path: a source that could not be listed and was treated as
    /// though it had been deletes everything it failed to see.
    /// </summary>
    [Fact]
    public async Task Run_PartialDiscovery_RemovesNothing()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "report.txt"), "The rig was measured.", TestContext.Current.CancellationToken);

        var harness = new Harness(_root);

        await harness.RunAsync();

        var stored = harness.Store.All.Count;

        harness.Connector.Override = new IngestionDiscoveryResult([], IsComplete: false, "The folder could not be listed.");

        var second = await harness.RunAsync();

        Assert.Equal(0, second.ItemsDeleted);
        Assert.False(second.DiscoveryCompleted);
        Assert.Equal(stored, harness.Store.All.Count);
    }

    private FileSystemIngestionConnector CreateConnector(bool allowed = true)
    {
        var options = new FileSourceOptions();

        if (allowed)
        {
            options.AllowedLocalRoots.Add(_root);
        }

        return new FileSystemIngestionConnector(
            Options.Create(options),
            NullLogger<FileSystemIngestionConnector>.Instance);
    }

    private WebCrawler CreateIndexer()
    {
        var indexer = new WebCrawler
        {
            ItemId = "indexer-1",
            Source = FileSystemIngestionConnector.ConnectorName,
            DisplayText = "The folder",
            AIDataSourceId = DataSourceId,
            Enabled = true,
        };

        indexer.Put(new LocalFolderIndexerMetadata
        {
            RootPath = _root,
        });

        return indexer;
    }

    /// <summary>
    /// Runs the real ingestion pipeline against an in-memory store.
    /// </summary>
    private sealed class Harness
    {
        private readonly string _root;

        public Harness(string root)
        {
            _root = root;

            var options = new FileSourceOptions();

            options.AllowedLocalRoots.Add(root);

            Connector = new OverridableConnector(new FileSystemIngestionConnector(
                Options.Create(options),
                NullLogger<FileSystemIngestionConnector>.Instance));

            var services = new ServiceCollection();
            services.AddSingleton<PlainTextIngestionDocumentReader>();
            services.AddKeyedSingleton<IngestionDocumentReader>(
                "text/plain",
                (sp, _) => sp.GetRequiredService<PlainTextIngestionDocumentReader>());

            var serviceProvider = services.BuildServiceProvider();

            var ingestionService = new DefaultKnowledgeIngestionService(
                new DefaultAIDocumentIngestionPipeline(new DefaultIngestionDocumentReaderResolver(serviceProvider), []),
                Store,
                FileStore,
                new DefaultAITextNormalizer(),
                new TocSeededStructureAnalyzer(NullLogger<TocSeededStructureAnalyzer>.Instance),
                new NullPublicationMetadataExtractor(),
                Queue,
                NullLogger<DefaultKnowledgeIngestionService>.Instance);

            var dataSourceStore = new Mock<IAIDataSourceStore>();
            dataSourceStore
                .Setup(store => store.FindByIdAsync(DataSourceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AIDataSource
                {
                    ItemId = DataSourceId,
                    Source = AIDataSourceSourceTypes.File,
                    DisplayText = "Knowledge",
                });

            var resolver = new Mock<IIngestionConnectorResolver>();
            resolver.Setup(instance => instance.Get(It.IsAny<string>())).Returns(Connector);

            var indexerStore = new Mock<IWebCrawlerStore>();

            Service = new DefaultFileSourceRunService(
                resolver.Object,
                StateStore,
                indexerStore.Object,
                ingestionService,
                dataSourceStore.Object,
                Options.Create(new FileSourceOptions()),
                TimeProvider.System,
                NullLogger<DefaultFileSourceRunService>.Instance);
        }

        public OverridableConnector Connector { get; }

        public InMemoryKnowledgeObjectStore Store { get; } = new();

        public RecordingDocumentFileStore FileStore { get; } = new();

        public RecordingIndexingQueue Queue { get; } = new();

        public InMemoryWebCrawlStateStore StateStore { get; } = new();

        public DefaultFileSourceRunService Service { get; }

        public Task<IndexerRunSummary> RunAsync()
        {
            var indexer = new WebCrawler
            {
                ItemId = "indexer-1",
                Source = FileSystemIngestionConnector.ConnectorName,
                DisplayText = "The folder",
                AIDataSourceId = DataSourceId,
                Enabled = true,
            };

            indexer.Put(new LocalFolderIndexerMetadata
            {
                RootPath = _root,
            });

            return Service.RunAsync(indexer, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Wraps the real connector so a test can make one discovery pass report itself incomplete.
    /// </summary>
    private sealed class OverridableConnector : IIngestionConnector
    {
        private readonly IIngestionConnector _inner;

        public OverridableConnector(IIngestionConnector inner)
        {
            _inner = inner;
        }

        public IngestionDiscoveryResult Override { get; set; }

        public string Name => _inner.Name;

        public ValueTask ValidateAsync(WebCrawler settings, ValidationResultDetails result, CancellationToken cancellationToken = default)
        {
            return _inner.ValidateAsync(settings, result, cancellationToken);
        }

        public Task<IngestionDiscoveryResult> DiscoverAsync(WebCrawler settings, string continuationToken = null, CancellationToken cancellationToken = default)
        {
            return Override is not null
                ? Task.FromResult(Override)
                : _inner.DiscoverAsync(settings, continuationToken, cancellationToken);
        }

        public Task<IngestionItemContent> FetchAsync(WebCrawler settings, string itemId, CancellationToken cancellationToken = default)
        {
            return _inner.FetchAsync(settings, itemId, cancellationToken);
        }
    }

    private sealed class NullPublicationMetadataExtractor : IPublicationMetadataExtractor
    {
        public Task<PublicationMetadata> ExtractAsync(IngestionDocument document, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PublicationMetadata>(null);
        }
    }
}
