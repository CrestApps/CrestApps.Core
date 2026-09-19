using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.FileSources;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.FileSources;

/// <summary>
/// Covers the scheduler: which records it considers its own, and which of those are due.
/// </summary>
/// <remarks>
/// This is deliberately separate from the hosted service that drives it, so a host with its own scheduling —
/// Orchard Core's background tasks, a cron job, an operator pressing a button — gets the same behaviour
/// without taking a timer with it. These tests exercise it the way such a host would: resolve it, call it.
/// </remarks>
public sealed class FileSourceSchedulerTests
{
    private const string IngestedDataSourceId = "ingested-ds";
    private const string WebDataSourceId = "web-ds";

    /// <summary>
    /// Verifies that a file source that has never run is due.
    /// </summary>
    [Fact]
    public async Task FileSourceThatHasNeverRun_IsDue()
    {
        var harness = new Harness();

        harness.FileSources.Add(CreateFileSource("fs-1"));

        var due = await harness.Scheduler.GetDueAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.Equal(["fs-1"], due.Select(source => source.ItemId));
    }

    /// <summary>
    /// Verifies that a file source that ran inside its interval is left alone.
    /// </summary>
    [Fact]
    public async Task FileSourceRunWithinItsInterval_IsNotDue()
    {
        var harness = new Harness();
        var now = DateTimeOffset.UtcNow;
        var fileSource = CreateFileSource("fs-1", intervalMinutes: 60);

        fileSource.Put(new FileSourceRunSummary { StartedUtc = now.UtcDateTime.AddMinutes(-10) });
        harness.FileSources.Add(fileSource);

        Assert.Empty(await harness.Scheduler.GetDueAsync(now, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that a file source whose interval has elapsed is due again.
    /// </summary>
    [Fact]
    public async Task FileSourceWhoseIntervalElapsed_IsDue()
    {
        var harness = new Harness();
        var now = DateTimeOffset.UtcNow;
        var fileSource = CreateFileSource("fs-1", intervalMinutes: 60);

        fileSource.Put(new FileSourceRunSummary { StartedUtc = now.UtcDateTime.AddMinutes(-90) });
        harness.FileSources.Add(fileSource);

        Assert.Single(await harness.Scheduler.GetDueAsync(now, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that a disabled file source is never run.
    /// </summary>
    [Fact]
    public async Task DisabledFileSource_IsSkipped()
    {
        var harness = new Harness();
        var fileSource = CreateFileSource("fs-1");

        fileSource.Enabled = false;
        harness.FileSources.Add(fileSource);

        Assert.Empty(await harness.Scheduler.GetDueAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that a record whose connector is no longer registered is skipped rather than run and failed.
    /// </summary>
    [Fact]
    public async Task FileSourceWithNoRegisteredConnector_IsSkipped()
    {
        var harness = new Harness();

        harness.FileSources.Add(CreateFileSource("fs-1", source: "Gone"));

        Assert.Empty(await harness.Scheduler.GetDueAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that a web crawler pointed at an ingested data source is picked up, because it is run by the
    /// same pipeline as a file source even though it lives in a different store.
    /// </summary>
    [Fact]
    public async Task CrawlerFeedingAnIngestedDataSource_IsDue()
    {
        var harness = new Harness();

        harness.WebCrawlers.Add(CreateCrawler("wc-1", IngestedDataSourceId));

        var due = await harness.Scheduler.GetDueAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.Equal(["wc-1"], due.Select(source => source.ItemId));
    }

    /// <summary>
    /// Verifies that a web crawler pointed at a Web data source is left to the re-index service.
    /// </summary>
    /// <remarks>
    /// Running it here as well would run it twice and overwrite the crawl state the re-index service keeps.
    /// </remarks>
    [Fact]
    public async Task CrawlerFeedingAWebDataSource_IsLeftAlone()
    {
        var harness = new Harness();

        harness.WebCrawlers.Add(CreateCrawler("wc-1", WebDataSourceId));

        Assert.Empty(await harness.Scheduler.GetDueAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that everything due is run, and that the pass reports what it did.
    /// </summary>
    [Fact]
    public async Task RunDue_RunsEverythingDue()
    {
        var harness = new Harness();

        harness.FileSources.Add(CreateFileSource("fs-1"));
        harness.FileSources.Add(CreateFileSource("fs-2"));
        harness.WebCrawlers.Add(CreateCrawler("wc-1", IngestedDataSourceId));

        var result = await harness.Scheduler.RunDueAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Considered);
        Assert.Equal(3, result.Ran);
        Assert.Equal(0, result.Failed);
        Assert.Equal(["fs-1", "fs-2", "wc-1"], harness.Ran.Select(source => source.ItemId).Order());
    }

    /// <summary>
    /// Verifies that one source that throws does not stop the others.
    /// </summary>
    /// <remarks>
    /// A pass that abandoned everything after the first unreachable file server would leave every later
    /// source unread until someone noticed.
    /// </remarks>
    [Fact]
    public async Task RunDue_OneSourceThatThrows_DoesNotStopTheOthers()
    {
        var harness = new Harness { FailingSourceId = "fs-1" };

        harness.FileSources.Add(CreateFileSource("fs-1"));
        harness.FileSources.Add(CreateFileSource("fs-2"));

        var result = await harness.Scheduler.RunDueAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Ran);
        Assert.Equal(1, result.Failed);
        Assert.Equal(["fs-2"], harness.Ran.Select(source => source.ItemId));
    }

    private static FileSource CreateFileSource(string id, string source = "FileSystem", int? intervalMinutes = null)
    {
        return new FileSource
        {
            ItemId = id,
            Source = source,
            DisplayText = id,
            AIDataSourceId = IngestedDataSourceId,
            Enabled = true,
            ReindexIntervalMinutes = intervalMinutes,
        };
    }

    private static WebCrawler CreateCrawler(string id, string dataSourceId)
    {
        return new WebCrawler
        {
            ItemId = id,
            Source = "Sitemap",
            DisplayText = id,
            AIDataSourceId = dataSourceId,
            Enabled = true,
        };
    }

    /// <summary>
    /// Wires the real scheduler to in-memory stores and a run service that only records what it was handed.
    /// </summary>
    private sealed class Harness
    {
        public Harness()
        {
            var fileSourceStore = new Mock<IFileSourceStore>();
            fileSourceStore
                .Setup(store => store.GetAllAsync(It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<IReadOnlyCollection<FileSource>>(FileSources));

            var webCrawlerStore = new Mock<IWebCrawlerStore>();
            webCrawlerStore
                .Setup(store => store.GetAllAsync(It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<IReadOnlyCollection<WebCrawler>>(WebCrawlers));

            var dataSources = new Dictionary<string, AIDataSource>(StringComparer.Ordinal)
            {
                [IngestedDataSourceId] = new AIDataSource { ItemId = IngestedDataSourceId, Source = AIDataSourceSourceTypes.File },
                [WebDataSourceId] = new AIDataSource { ItemId = WebDataSourceId, Source = AIDataSourceSourceTypes.Web },
            };

            var dataSourceStore = new Mock<IAIDataSourceStore>();
            dataSourceStore
                .Setup(store => store.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string id, CancellationToken _) => ValueTask.FromResult(dataSources.GetValueOrDefault(id)));

            var connectorResolver = new Mock<IIngestionConnectorResolver>();
            connectorResolver
                .Setup(resolver => resolver.Get(It.IsAny<string>()))
                .Returns((string name) => name is "FileSystem" or "Sitemap" ? Mock.Of<IIngestionConnector>() : null);

            var runService = new Mock<IFileSourceRunService>();
            runService
                .Setup(service => service.RunAsync(It.IsAny<IngestionSource>(), It.IsAny<CancellationToken>()))
                .Returns((IngestionSource source, CancellationToken _) =>
                {
                    if (source.ItemId == FailingSourceId)
                    {
                        throw new InvalidOperationException("The source could not be reached.");
                    }

                    Ran.Add(source);

                    return Task.FromResult(new FileSourceRunSummary());
                });

            Scheduler = new DefaultFileSourceScheduler(
                fileSourceStore.Object,
                webCrawlerStore.Object,
                dataSourceStore.Object,
                connectorResolver.Object,
                runService.Object,
                Options.Create(new FileSourceOptions()),
                TimeProvider.System,
                NullLogger<DefaultFileSourceScheduler>.Instance);
        }

        public List<FileSource> FileSources { get; } = [];

        public List<WebCrawler> WebCrawlers { get; } = [];

        public List<IngestionSource> Ran { get; } = [];

        public string FailingSourceId { get; init; }

        public DefaultFileSourceScheduler Scheduler { get; }
    }
}
