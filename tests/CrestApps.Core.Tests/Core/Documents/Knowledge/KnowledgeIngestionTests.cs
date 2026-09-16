using System.Text;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Ingestion;
using CrestApps.Core.AI.Documents.Knowledge;
using CrestApps.Core.AI.Documents.Knowledge.Structure;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Documents.Knowledge;

public sealed class KnowledgeIngestionTests
{
    private const string DataSourceId = "data-source-1";

    /// <summary>
    /// Verifies that every knowledge object becomes one row that is already a chunk, carrying the type,
    /// parentage and page that make it retrievable on its own.
    /// </summary>
    [Fact]
    public async Task IngestedHandler_ReadAsync_YieldsOnePreChunkedRowPerObjectWithTypedFields()
    {
        var store = new InMemoryKnowledgeObjectStore();

        store.Seed(CreateObject("text:key:1:0", KnowledgeContentTypes.Text, "doc:key", "article:key:1", page: 4));
        store.Seed(CreateObject("figure:key:1:0", KnowledgeContentTypes.Figure, "doc:key", "article:key:1", page: 8));

        var handler = new FileAIDataSourceSourceHandler(store);
        var rows = new List<KeyValuePair<string, SourceDocument>>();

        await foreach (var row in handler.ReadAsync(CreateDataSource(), TestContext.Current.CancellationToken))
        {
            rows.Add(row);
        }

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.True(row.Value.IsPreChunked));

        var figure = rows.Single(row => row.Key == "figure:key:1:0").Value;

        Assert.Equal(KnowledgeContentTypes.Figure, figure.Fields[DataSourceConstants.ColumnNames.ContentType]);
        Assert.Equal("doc:key", figure.Fields[DataSourceConstants.ColumnNames.RootId]);
        Assert.Equal("article:key:1", figure.Fields[DataSourceConstants.ColumnNames.ParentId]);
        Assert.Equal(8, figure.Fields[DataSourceConstants.ColumnNames.Page]);
    }

    /// <summary>
    /// Verifies that an incremental read returns only what was asked for.
    /// </summary>
    [Fact]
    public async Task IngestedHandler_ReadByIdsAsync_MapsOnlyRequestedIds()
    {
        var store = new InMemoryKnowledgeObjectStore();

        store.Seed(CreateObject("text:key:1:0", KnowledgeContentTypes.Text, "doc:key", "article:key:1", page: 1));
        store.Seed(CreateObject("text:key:1:1", KnowledgeContentTypes.Text, "doc:key", "article:key:1", page: 2));

        var handler = new FileAIDataSourceSourceHandler(store);
        var rows = new List<string>();

        await foreach (var row in handler.ReadByIdsAsync(CreateDataSource(), ["text:key:1:1"], TestContext.Current.CancellationToken))
        {
            rows.Add(row.Key);
        }

        Assert.Equal(["text:key:1:1"], rows);
    }

    /// <summary>
    /// Verifies that a chart carrying series points, but no caption, no description and no surrounding
    /// text, is still indexed. The points were successfully read off the chart, and dropping the row would
    /// throw them away.
    /// </summary>
    [Fact]
    public async Task IngestedHandler_ReadAsync_IndexesAChartThatCarriesOnlySeries()
    {
        var store = new InMemoryKnowledgeObjectStore();
        var chart = CreateObject("chart:key:1:0", KnowledgeContentTypes.Chart, "doc:key", "article:key:1", page: 5);

        chart.Content = null;
        chart.Put(new FigureDetails());
        chart.Put(new ChartDetails
        {
            ChartType = "line",
            ValueConfidence = ChartValueConfidence.Exact,
            AxisX = "Period",
            AxisY = "Units",
            Series =
            [
                new ChartSeries
                {
                    Name = "First series",
                    Points = [[1d, 4d], [2d, 6.5d]],
                },
            ],
        });

        store.Seed(chart);

        var handler = new FileAIDataSourceSourceHandler(store);
        var rows = new List<KeyValuePair<string, SourceDocument>>();

        await foreach (var row in handler.ReadAsync(CreateDataSource(), TestContext.Current.CancellationToken))
        {
            rows.Add(row);
        }

        var indexed = Assert.Single(rows);

        Assert.Equal("chart:key:1:0", indexed.Key);
        Assert.Contains("First series", indexed.Value.Content);
        Assert.Contains("(1, 4)", indexed.Value.Content);
        Assert.Contains("(2, 6.5)", indexed.Value.Content);
        Assert.Contains("Period | Units", indexed.Value.Content);
    }

    /// <summary>
    /// Verifies that a table with rows, but nothing written around it, is indexed by the rows themselves,
    /// each carrying its column names.
    /// </summary>
    [Fact]
    public async Task IngestedHandler_ReadAsync_IndexesATableThatCarriesOnlyRows()
    {
        var store = new InMemoryKnowledgeObjectStore();
        var table = CreateObject("table:key:1:0", KnowledgeContentTypes.Table, "doc:key", "article:key:1", page: 6);

        table.Content = null;
        table.Put(new TableDetails
        {
            Columns = ["Period", "Units"],
            Rows =
            [
                ["First", "4"],
                ["Second", "7"],
            ],
        });

        store.Seed(table);

        var handler = new FileAIDataSourceSourceHandler(store);
        var rows = new List<KeyValuePair<string, SourceDocument>>();

        await foreach (var row in handler.ReadAsync(CreateDataSource(), TestContext.Current.CancellationToken))
        {
            rows.Add(row);
        }

        var indexed = Assert.Single(rows);

        Assert.Contains("Period | Units", indexed.Value.Content);
        Assert.Contains("Period=First; Units=4", indexed.Value.Content);
        Assert.Contains("Period=Second; Units=7", indexed.Value.Content);
    }

    /// <summary>
    /// Verifies that an object carrying neither text nor structured detail is still dropped. Nothing empty
    /// is ever embedded.
    /// </summary>
    [Fact]
    public async Task IngestedHandler_ReadAsync_DropsAnObjectThatCarriesNothing()
    {
        var store = new InMemoryKnowledgeObjectStore();
        var figure = CreateObject("figure:key:1:0", KnowledgeContentTypes.Figure, "doc:key", "article:key:1", page: 3);
        var chart = CreateObject("chart:key:1:1", KnowledgeContentTypes.Chart, "doc:key", "article:key:1", page: 3);

        figure.Content = null;
        figure.Put(new FigureDetails());

        chart.Content = "   ";
        chart.Put(new ChartDetails
        {
            ValueConfidence = ChartValueConfidence.Descriptive,
            Series = [],
        });

        store.Seed(figure);
        store.Seed(chart);

        var handler = new FileAIDataSourceSourceHandler(store);
        var rows = new List<string>();

        await foreach (var row in handler.ReadAsync(CreateDataSource(), TestContext.Current.CancellationToken))
        {
            rows.Add(row.Key);
        }

        Assert.Empty(rows);
    }

    /// <summary>
    /// Verifies that one ingest stores the objects, writes the figure bytes, and queues exactly what it
    /// stored for indexing.
    /// </summary>
    [Fact]
    public async Task KnowledgeIngestionService_IngestAsync_StoresObjectsFilesAndQueuesSync()
    {
        var harness = new Harness();

        var result = await harness.IngestAsync("First body line. Second body line.");

        Assert.True(result.Success, result.Error);
        Assert.StartsWith("document:", result.RootId, StringComparison.Ordinal);
        Assert.True(result.ObjectCount >= 3);

        Assert.Equal(
            harness.Store.All.Select(entry => entry.CanonicalId).OrderBy(id => id, StringComparer.Ordinal),
            harness.Queue.Synced.OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifies that re-ingesting identical bytes replaces what the file produced rather than storing a
    /// second copy of it.
    /// </summary>
    [Fact]
    public async Task KnowledgeIngestionService_ReingestSameBytes_ReplacesObjectsUnderSameRoot()
    {
        var harness = new Harness();

        var first = await harness.IngestAsync("The body text.");
        var countAfterFirst = harness.Store.All.Count;

        var second = await harness.IngestAsync("The body text.");

        Assert.Equal(first.RootId, second.RootId);
        Assert.Equal(countAfterFirst, harness.Store.All.Count);
    }

    /// <summary>
    /// Verifies that re-ingesting the same bytes twice through one uncommitted unit of work stores one set
    /// of objects. The store cannot see what was written to the unit of work earlier, so the service has to
    /// know what it has already produced.
    /// </summary>
    [Fact]
    public async Task KnowledgeIngestionService_SameBytesTwiceInOneUnitOfWork_StoresOneSetOfObjects()
    {
        var store = new UncommittedKnowledgeObjectStore();
        var service = CreateService(store, new RecordingDocumentFileStore(), new RecordingIndexingQueue());

        var first = await IngestAsync(service, "The body text.");
        var second = await IngestAsync(service, "The body text.");

        var canonicalIds = store.All.Select(entry => entry.CanonicalId).ToArray();

        Assert.Equal(first.RootId, second.RootId);
        Assert.Equal(second.ObjectCount, canonicalIds.Length);
        Assert.Equal(canonicalIds.Length, canonicalIds.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Verifies that an object reclassified as excluded on a later ingest is taken out of the index. It is
    /// deliberately kept out of the sync queue, so nothing else would ever mention it again and its earlier
    /// rows would stay answerable.
    /// </summary>
    [Fact]
    public async Task KnowledgeIngestionService_ReingestAsExcluded_RemovesTheObjectsFromTheIndex()
    {
        var analyzer = new FixedStructureAnalyzer();
        var harness = new Harness(analyzer);

        await harness.IngestAsync("The body text.");

        var indexedFirst = harness.Queue.Synced.ToArray();

        Assert.Contains(indexedFirst, id => id.StartsWith(KnowledgeContentTypes.Article, StringComparison.Ordinal));

        analyzer.ArticleType = KnowledgeArticleTypes.Advertisement;

        await harness.IngestAsync("The body text.");

        var excluded = harness.Store.All
            .Where(entry => entry.Status == KnowledgeObjectStatus.Excluded)
            .Select(entry => entry.CanonicalId)
            .ToArray();

        // What the first ingest put in front of the index and the second one excluded.
        var reclassified = excluded.Where(id => indexedFirst.Contains(id, StringComparer.Ordinal)).ToArray();

        Assert.NotEmpty(reclassified);
        Assert.All(reclassified, id => Assert.Contains(id, harness.Queue.Removed));
        Assert.DoesNotContain(harness.Queue.Synced.Skip(indexedFirst.Length), id => excluded.Contains(id, StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifies that removing a document takes its objects, its files and its index rows with it.
    /// </summary>
    [Fact]
    public async Task KnowledgeIngestionService_RemoveAsync_DeletesObjectsFilesAndQueuesRemove()
    {
        var harness = new Harness();

        var result = await harness.IngestAsync("The body text.");
        var stored = harness.Store.All.Select(entry => entry.CanonicalId).ToArray();

        await harness.Service.RemoveAsync(CreateDataSource(), result.RootId, TestContext.Current.CancellationToken);

        Assert.Empty(harness.Store.All);
        Assert.Equal(
            stored.OrderBy(id => id, StringComparer.Ordinal),
            harness.Queue.Removed.OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifies that re-ingesting a document takes out of the index, and off the disk, whatever the previous
    /// ingest produced that this one did not. Deleting the store by root replaces the objects; the index only
    /// ever hears about identifiers it is told about.
    /// </summary>
    [Fact]
    public async Task KnowledgeIngestionService_Reingest_RemovesObjectsThatVanished()
    {
        var harness = new Harness();

        var first = await harness.IngestAsync("The body text.");

        // A figure the earlier reader produced and this one will not, with a stored picture of its own.
        var stale = CreateObject($"figure:{first.RootId[9..]}:1:0", KnowledgeContentTypes.Figure, first.RootId, $"article:{first.RootId[9..]}:1", page: 2);

        stale.StoragePath = "figures/stale.png";
        harness.Store.Seed(stale);
        harness.FileStore.Saved["figures/stale.png"] = [1, 2, 3];

        await harness.IngestAsync("The body text.");

        Assert.DoesNotContain(harness.Store.All, entry => entry.CanonicalId == stale.CanonicalId);
        Assert.Contains(stale.CanonicalId, harness.Queue.Removed);
        Assert.DoesNotContain("figures/stale.png", harness.FileStore.Saved.Keys);
    }

    /// <summary>
    /// Wires the ingestion service up with a plain-text reader.
    /// </summary>
    /// <param name="store">The knowledge object store.</param>
    /// <param name="fileStore">The store the figure bytes are written to.</param>
    /// <param name="indexingQueue">The indexing queue.</param>
    /// <param name="structureAnalyzer">The structure analyzer, or <see langword="null"/> for the real one.</param>
    /// <returns>The service.</returns>
    private static DefaultKnowledgeIngestionService CreateService(
        IKnowledgeObjectStore store,
        IDocumentFileStore fileStore,
        IAIDataSourceIndexingQueue indexingQueue,
        IDocumentStructureAnalyzer structureAnalyzer = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<PlainTextIngestionDocumentReader>();
        services.AddKeyedSingleton<IngestionDocumentReader>(
            ".txt",
            (sp, _) => sp.GetRequiredService<PlainTextIngestionDocumentReader>());

        var serviceProvider = services.BuildServiceProvider();

        return new DefaultKnowledgeIngestionService(
            new DefaultAIDocumentIngestionPipeline(new DefaultIngestionDocumentReaderResolver(serviceProvider), []),
            store,
            fileStore,
            new DefaultAITextNormalizer(),
            structureAnalyzer ?? new TocSeededStructureAnalyzer(NullLogger<TocSeededStructureAnalyzer>.Instance),
            new NullPublicationMetadataExtractor(),
            indexingQueue,
            NullLogger<DefaultKnowledgeIngestionService>.Instance);
    }

    private static Task<KnowledgeIngestionResult> IngestAsync(DefaultKnowledgeIngestionService service, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);

        return service.IngestAsync(
            CreateDataSource(),
            new MemoryStream(bytes),
            "report.txt",
            "text/plain",
            new KnowledgeIngestionOptions(),
            TestContext.Current.CancellationToken);
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

    private static KnowledgeObject CreateObject(string canonicalId, string objectType, string rootId, string parentId, int page)
    {
        return new KnowledgeObject
        {
            ItemId = canonicalId,
            Source = DataSourceId,
            CanonicalId = canonicalId,
            ObjectType = objectType,
            RootId = rootId,
            ParentId = parentId,
            Title = "report.pdf",
            Content = "Some content.",
            PageStart = page,
            PageEnd = page,
        };
    }

    /// <summary>
    /// Reads nothing, so a test never depends on a model being configured.
    /// </summary>
    private sealed class NullPublicationMetadataExtractor : IPublicationMetadataExtractor
    {
        public Task<PublicationMetadata> ExtractAsync(IngestionDocument document, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PublicationMetadata>(null);
        }
    }

    /// <summary>
    /// Returns one article of whatever kind it is set to, so a re-ingest of identical bytes can reclassify
    /// what they produce.
    /// </summary>
    private sealed class FixedStructureAnalyzer : IDocumentStructureAnalyzer
    {
        /// <summary>
        /// Gets or sets the kind of article to report. See <see cref="KnowledgeArticleTypes"/>.
        /// </summary>
        public string ArticleType { get; set; } = KnowledgeArticleTypes.Article;

        public DocumentStructure Analyze(IngestionDocument document)
        {
            return new DocumentStructure
            {
                Articles =
                [
                    new DocumentArticle
                    {
                        Ordinal = 1,
                        Title = "The article",
                        PageStart = 1,
                        PageEnd = document.Sections.Count,
                        Type = ArticleType,
                    },
                ],
                IsInferred = true,
            };
        }
    }

    /// <summary>
    /// Stands in for a store sharing one uncommitted unit of work: what is written is invisible to a read
    /// until the unit of work is committed, which is how the session the real stores sit on behaves.
    /// </summary>
    private sealed class UncommittedKnowledgeObjectStore : IKnowledgeObjectStore
    {
        private readonly InMemoryKnowledgeObjectStore _committed = new();
        private readonly List<KnowledgeObject> _pending = [];

        /// <summary>
        /// Gets everything the unit of work holds, written or committed.
        /// </summary>
        public IReadOnlyList<KnowledgeObject> All => [.. _committed.All, .. _pending];

        public ValueTask CreateAsync(KnowledgeObject entry, CancellationToken cancellationToken = default)
        {
            _pending.Add(entry);

            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(KnowledgeObject entry, CancellationToken cancellationToken = default)
        {
            _pending.RemoveAll(item => item.ItemId == entry.ItemId);
            _pending.Add(entry);

            return ValueTask.CompletedTask;
        }

        public async ValueTask<bool> DeleteAsync(KnowledgeObject entry, CancellationToken cancellationToken = default)
        {
            var removed = _pending.RemoveAll(item => item.ItemId == entry.ItemId) > 0;

            return await _committed.DeleteAsync(entry, cancellationToken) || removed;
        }

        // A query runs against what was committed, so an object written to this unit of work is not found
        // by one and is not deleted by one either.
        public Task DeleteByRootIdAsync(string dataSourceId, string rootId, CancellationToken cancellationToken = default)
        {
            return _committed.DeleteByRootIdAsync(dataSourceId, rootId, cancellationToken);
        }

        public ValueTask<KnowledgeObject> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return _committed.FindByIdAsync(id, cancellationToken);
        }

        public ValueTask<IReadOnlyCollection<KnowledgeObject>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return _committed.GetAllAsync(cancellationToken);
        }

        public ValueTask<IReadOnlyCollection<KnowledgeObject>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
        {
            return _committed.GetAsync(ids, cancellationToken);
        }

        public ValueTask<IReadOnlyCollection<KnowledgeObject>> GetAsync(string source, CancellationToken cancellationToken = default)
        {
            return _committed.GetAsync(source, cancellationToken);
        }

        public ValueTask<PageResult<KnowledgeObject>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
            where TQuery : QueryContext
        {
            return _committed.PageAsync(page, pageSize, context, cancellationToken);
        }

        public Task<KnowledgeObject> FindByCanonicalIdAsync(string dataSourceId, string canonicalId, CancellationToken cancellationToken = default)
        {
            return _committed.FindByCanonicalIdAsync(dataSourceId, canonicalId, cancellationToken);
        }

        public Task<IReadOnlyCollection<KnowledgeObject>> GetByCanonicalIdsAsync(string dataSourceId, IEnumerable<string> canonicalIds, CancellationToken cancellationToken = default)
        {
            return _committed.GetByCanonicalIdsAsync(dataSourceId, canonicalIds, cancellationToken);
        }

        public Task<IReadOnlyCollection<KnowledgeObject>> GetByRootIdAsync(string dataSourceId, string rootId, CancellationToken cancellationToken = default)
        {
            return _committed.GetByRootIdAsync(dataSourceId, rootId, cancellationToken);
        }

        public Task<IReadOnlyCollection<KnowledgeObject>> GetByDataSourceIdAsync(string dataSourceId, CancellationToken cancellationToken = default)
        {
            return _committed.GetByDataSourceIdAsync(dataSourceId, cancellationToken);
        }

        public Task<IReadOnlyCollection<KnowledgeObject>> GetByStatusAsync(string dataSourceId, string status, int take, CancellationToken cancellationToken = default)
        {
            return _committed.GetByStatusAsync(dataSourceId, status, take, cancellationToken);
        }

        public Task<KnowledgeObject> FindFigureByContentHashAsync(string contentHash, string promptVersion, CancellationToken cancellationToken = default)
        {
            return _committed.FindFigureByContentHashAsync(contentHash, promptVersion, cancellationToken);
        }
    }

    /// <summary>
    /// Wires the ingestion service up with a plain-text reader, an in-memory store and a recording queue.
    /// </summary>
    private sealed class Harness
    {
        public Harness(IDocumentStructureAnalyzer structureAnalyzer = null)
        {
            Service = CreateService(Store, FileStore, Queue, structureAnalyzer);
        }

        public DefaultKnowledgeIngestionService Service { get; }

        public InMemoryKnowledgeObjectStore Store { get; } = new();

        public RecordingDocumentFileStore FileStore { get; } = new();

        public RecordingIndexingQueue Queue { get; } = new();

        public Task<KnowledgeIngestionResult> IngestAsync(string content)
        {
            return KnowledgeIngestionTests.IngestAsync(Service, content);
        }
    }
}
