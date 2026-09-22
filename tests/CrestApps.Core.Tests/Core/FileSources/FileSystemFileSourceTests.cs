using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.FileSources;
using CrestApps.Core.AI.FileSources.Connectors;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Knowledge;
using CrestApps.Core.AI.Ingestion.Knowledge.Structure;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.FileSources;

/// <summary>
/// Covers continuous intake from a folder on the host: what it lists, what it refuses to read, and the one
/// rule the whole subsystem turns on — a partial listing never deletes anything.
/// </summary>
public sealed class FileSystemFileSourceTests : IDisposable
{
    private const string DataSourceId = "data-source-1";

    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemFileSourceTests"/> class.
    /// </summary>
    public FileSystemFileSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "crestapps-file-source-" + Guid.NewGuid().ToString("N"));

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
    /// Verifies that a folder nobody allowed is refused, so the File Sources screen is not a way to read any file
    /// the host process can open.
    /// </summary>
    [Fact]
    public async Task Discover_RootOutsideAllowedRoots_IsRefusedAndIncomplete()
    {
        var connector = CreateConnector(allowed: false);

        var result = await connector.DiscoverAsync(CreateFileSource(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Items);

        var validation = new ValidationResultDetails();

        await connector.ValidateAsync(CreateFileSource(), validation, TestContext.Current.CancellationToken);

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
        var result = await connector.DiscoverAsync(CreateFileSource(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.ChangeToken)));

        var before = result.Items.Single(item => item.ItemId == "first.txt").ChangeToken;

        await File.WriteAllTextAsync(Path.Combine(_root, "first.txt"), "The first file, revised and longer.", TestContext.Current.CancellationToken);

        var after = await connector.DiscoverAsync(CreateFileSource(), cancellationToken: TestContext.Current.CancellationToken);

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

        var content = await connector.FetchAsync(CreateFileSource(), "../../secrets.txt", TestContext.Current.CancellationToken);

        Assert.Null(content);
    }

    /// <summary>
    /// Verifies that a stored folder containing a <c>..</c> reads nothing, even though it was never
    /// validated.
    /// </summary>
    /// <remarks>
    /// Validation runs when a record is saved, but a record can reach the store without it — a caller that
    /// skips it, a row written by hand, a settings blob edited outside the screens. The connector is the
    /// boundary that does not depend on any of that, so it re-checks what it was handed on every run.
    /// </remarks>
    [Fact]
    public async Task Discover_StoredRootWithParentTraversal_ReadsNothing()
    {
        var fileSource = CreateFileSource();

        // Never validated: written straight onto the record, the way an unvalidated save would leave it.
        // It normalizes to a folder inside the allowed root, so containment alone would admit it.
        fileSource.Put(new FileSystemFileSourceMetadata
        {
            RootPath = Path.Combine(_root, "..", Path.GetFileName(_root)),
        });

        var discovery = await CreateConnector().DiscoverAsync(fileSource, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(discovery.Items);

        // Never complete, so nothing the record had already indexed is treated as deleted.
        Assert.False(discovery.IsComplete);
        Assert.Contains("'..'", discovery.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the same stored folder cannot be read one file at a time either.
    /// </summary>
    [Fact]
    public async Task Fetch_StoredRootWithParentTraversal_ReadsNothing()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "report.txt"), "The rig was measured.", TestContext.Current.CancellationToken);

        var fileSource = CreateFileSource();

        fileSource.Put(new FileSystemFileSourceMetadata
        {
            RootPath = Path.Combine(_root, "..", Path.GetFileName(_root)),
        });

        var content = await CreateConnector().FetchAsync(fileSource, "report.txt", TestContext.Current.CancellationToken);

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
    /// Verifies that a second run over unchanged files ingests nothing again, so a source is cheap to run
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

    /// <summary>
    /// Verifies that a file source reads its own folder and everything beneath it, and nothing beside it,
    /// even though the folder it may read is only a sub-folder of the host's allowed root.
    /// </summary>
    [Fact]
    public async Task Discover_Recursive_ReadsTheFileSourcesOwnFolderAndBelow()
    {
        var wanted = Path.Combine(_root, "test");

        Directory.CreateDirectory(Path.Combine(wanted, "nested"));
        await File.WriteAllTextAsync(Path.Combine(wanted, "a.txt"), "a", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(wanted, "nested", "b.txt"), "b", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_root, "beside.txt"), "beside", TestContext.Current.CancellationToken);

        var fileSource = CreateFileSource();

        fileSource.Put(new FileSystemFileSourceMetadata { RootPath = wanted, Recursive = true });

        var discovery = await CreateConnector().DiscoverAsync(fileSource, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["a.txt", "nested/b.txt"], discovery.Items.Select(item => item.ItemId).Order());
    }

    /// <summary>
    /// Verifies that a file source that is not recursive reads only the files sitting directly in its own
    /// folder. "Top directory" is that folder, never the allowed root above it.
    /// </summary>
    [Fact]
    public async Task Discover_NotRecursive_ReadsOnlyTheFileSourcesOwnFolder()
    {
        var wanted = Path.Combine(_root, "test");

        Directory.CreateDirectory(Path.Combine(wanted, "nested"));
        await File.WriteAllTextAsync(Path.Combine(wanted, "a.txt"), "a", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(wanted, "nested", "b.txt"), "b", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_root, "beside.txt"), "beside", TestContext.Current.CancellationToken);

        var fileSource = CreateFileSource();

        fileSource.Put(new FileSystemFileSourceMetadata { RootPath = wanted, Recursive = false });

        var discovery = await CreateConnector().DiscoverAsync(fileSource, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["a.txt"], discovery.Items.Select(item => item.ItemId));
    }

    /// <summary>
    /// Verifies that every file in the folder is listed, whatever its extension. Which of them can be read
    /// is the reader resolver's business, not a glob the connector guesses at.
    /// </summary>
    [Fact]
    public async Task Discover_ListsEveryFileWhateverItsExtension()
    {
        var wanted = Path.Combine(_root, "test");

        Directory.CreateDirectory(wanted);
        await File.WriteAllTextAsync(Path.Combine(wanted, "a.txt"), "a", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(wanted, "b.md"), "b", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(wanted, "c"), "c", TestContext.Current.CancellationToken);

        var fileSource = CreateFileSource();

        fileSource.Put(new FileSystemFileSourceMetadata { RootPath = wanted });

        var discovery = await CreateConnector().DiscoverAsync(fileSource, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["a.txt", "b.md", "c"], discovery.Items.Select(item => item.ItemId).Order());
    }

    /// <summary>
    /// Verifies that a source naming no folder reads the folder the host allows, rather than being treated
    /// as unconfigured and quietly ingesting nothing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Discover_NoFolderNamed_ReadsTheAllowedRoot(string rootPath)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "first.txt"), "The first file.", TestContext.Current.CancellationToken);

        var connector = CreateConnector();
        var source = CreateFileSource(rootPath);
        var result = await connector.DiscoverAsync(source, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Null(result.Message);
        Assert.Equal("first.txt", Assert.Single(result.Items).ItemId);

        var validation = new ValidationResultDetails();

        await connector.ValidateAsync(source, validation, TestContext.Current.CancellationToken);

        Assert.True(validation.Succeeded);
    }

    private FileSystemIngestionConnector CreateConnector(bool allowed = true)
    {
        var options = new FileSystemConnectorOptions();

        if (allowed)
        {
            options.AllowedRoots.Add(_root);
        }

        return new FileSystemIngestionConnector(
            Options.Create(new FileSourceOptions()),
            Options.Create(options),
            NullLogger<FileSystemIngestionConnector>.Instance);
    }

    private FileSource CreateFileSource()
        => CreateFileSource(_root);

    private static FileSource CreateFileSource(string rootPath)
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
            RootPath = rootPath,
        });

        return source;
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

            var options = new FileSystemConnectorOptions();

            options.AllowedRoots.Add(root);

            Connector = new OverridableConnector(new FileSystemIngestionConnector(
                Options.Create(new FileSourceOptions()),
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
                StructureAnalyzers.Default(),
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

            Service = new DefaultIngestionRunService(
                resolver.Object,
                StateStore,
                [new RecordingIngestionSourceProvider()],
                ingestionService,
                dataSourceStore.Object,
                Options.Create(new FileSourceOptions()),
                TimeProvider.System,
                NullLogger<DefaultIngestionRunService>.Instance);
        }

        public OverridableConnector Connector { get; }

        public InMemoryKnowledgeObjectStore Store { get; } = new();

        public RecordingDocumentFileStore FileStore { get; } = new();

        public RecordingIndexingQueue Queue { get; } = new();

        public InMemoryIngestionItemStateStore StateStore { get; } = new();

        public DefaultIngestionRunService Service { get; }

        public Task<FileSourceRunSummary> RunAsync()
        {
            var source = new WebCrawler
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
            });

            return Service.RunAsync(source, TestContext.Current.CancellationToken);
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

        public ValueTask ValidateAsync(IngestionSource settings, ValidationResultDetails result, CancellationToken cancellationToken = default)
        {
            return _inner.ValidateAsync(settings, result, cancellationToken);
        }

        public Task<IngestionDiscoveryResult> DiscoverAsync(IngestionSource settings, string continuationToken = null, CancellationToken cancellationToken = default)
        {
            return Override is not null
                ? Task.FromResult(Override)
                : _inner.DiscoverAsync(settings, continuationToken, cancellationToken);
        }

        public Task<IngestionItemContent> FetchAsync(IngestionSource settings, string itemId, CancellationToken cancellationToken = default)
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
