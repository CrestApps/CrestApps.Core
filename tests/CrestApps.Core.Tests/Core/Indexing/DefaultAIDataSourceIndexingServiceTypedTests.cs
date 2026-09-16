using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Indexing;

/// <summary>
/// Covers what typed knowledge changed about indexing: rows that arrive already chunked, discriminators that
/// live in real columns, schema top-ups for indexes that predate them, and deleting by reference rather than
/// by a thousand guessed identifiers.
/// </summary>
public sealed class DefaultAIDataSourceIndexingServiceTypedTests
{
    private const string DataSourceId = "data-source-1";
    private const string ProviderName = "TestProvider";
    private const string KnowledgeBaseName = "kb-index";
    private const string EmbeddingDeploymentName = "text-embedding-3-small";

    /// <summary>
    /// Verifies that a row built to sit inside one chunk is stored as it stands, with no second copy of its
    /// title prepended.
    /// </summary>
    [Fact]
    public async Task IndexDocuments_PreChunkedSource_WritesExactlyOneRowAndDoesNotPrependTitle()
    {
        var harness = new Harness();

        harness.Documents["figure:key:1:0"] = new SourceDocument
        {
            Title = "report.pdf",
            Content = "Figure 1. The caption.\n\nThe transcription.",
            IsPreChunked = true,
            Fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [DataSourceConstants.ColumnNames.ContentType] = KnowledgeObjectTypes.Figure,
            },
        };

        await harness.SyncAsync();

        var written = Assert.Single(harness.DocumentManager.Written);

        Assert.Equal("figure:key:1:0_0", written.Id);
        Assert.DoesNotContain("report.pdf", (string)written.Fields[DataSourceConstants.ColumnNames.Content], StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the typed discriminators are written to their own columns and are not duplicated into
    /// the filter bag, which not every provider stores the same way.
    /// </summary>
    [Fact]
    public async Task IndexDocuments_TypedFields_LandInColumnsAndNotInFilters()
    {
        var harness = new Harness();

        harness.Documents["figure:key:1:0"] = new SourceDocument
        {
            Title = "report.pdf",
            Content = "The figure.",
            IsPreChunked = true,
            Fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [DataSourceConstants.ColumnNames.ContentType] = KnowledgeObjectTypes.Figure,
                [DataSourceConstants.ColumnNames.RootId] = "document:key",
                [DataSourceConstants.ColumnNames.ParentId] = "article:key:1",
                [DataSourceConstants.ColumnNames.Page] = 8,
                ["language"] = "hu",
            },
        };

        await harness.SyncAsync();

        var written = Assert.Single(harness.DocumentManager.Written);

        Assert.Equal(KnowledgeObjectTypes.Figure, written.Fields[DataSourceConstants.ColumnNames.ContentType]);
        Assert.Equal("document:key", written.Fields[DataSourceConstants.ColumnNames.RootId]);
        Assert.Equal("article:key:1", written.Fields[DataSourceConstants.ColumnNames.ParentId]);
        Assert.Equal(8, written.Fields[DataSourceConstants.ColumnNames.Page]);

        var filters = Assert.IsType<Dictionary<string, object>>(written.Fields[DataSourceConstants.ColumnNames.Filters]);

        Assert.Equal("hu", filters["language"]);
        Assert.DoesNotContain(DataSourceConstants.ColumnNames.ContentType, filters.Keys);
        Assert.DoesNotContain(DataSourceConstants.ColumnNames.RootId, filters.Keys);
        Assert.DoesNotContain(DataSourceConstants.ColumnNames.Page, filters.Keys);
    }

    /// <summary>
    /// Verifies that a data source which does not produce typed knowledge keeps a field of its own named
    /// <c>contentType</c> in the filter bag, rather than having it promoted into the knowledge base's
    /// discriminator column and dropped from the bag its own filters address.
    /// </summary>
    [Fact]
    public async Task IndexDocuments_WhenSourceDoesNotProduceTypedKnowledge_KeepsItsOwnContentTypeInFilters()
    {
        var harness = new Harness
        {
            ProducesTypedKnowledge = false,
        };

        harness.Documents["their-own-key"] = new SourceDocument
        {
            Title = "A record of their own",
            Content = "Their content.",
            IsPreChunked = true,
            Fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                // Their field, which has meant their own thing since long before the typed columns existed.
                [DataSourceConstants.ColumnNames.ContentType] = "press-release",
                ["language"] = "hu",
            },
        };

        await harness.SyncAsync();

        var written = Assert.Single(harness.DocumentManager.Written);
        var filters = Assert.IsType<Dictionary<string, object>>(written.Fields[DataSourceConstants.ColumnNames.Filters]);

        // Their value stays reachable where their filters look for it.
        Assert.Equal("press-release", filters[DataSourceConstants.ColumnNames.ContentType]);

        // And it never reaches the discriminator, which stays the default every untyped row already reads as.
        Assert.Equal(KnowledgeObjectTypes.Text, written.Fields[DataSourceConstants.ColumnNames.ContentType]);
    }

    /// <summary>
    /// Verifies that an index that already exists is offered the current schema, so columns added after it
    /// was built still reach it.
    /// </summary>
    [Fact]
    public async Task IndexDocuments_ExistingIndex_CallsTryAddFields()
    {
        var harness = new Harness();

        harness.Documents["text:key:1:0"] = new SourceDocument
        {
            Content = "Body.",
        };

        await harness.SyncAsync();

        Assert.True(harness.IndexManager.TryAddFieldsCalled);
    }

    /// <summary>
    /// Verifies that a provider able to delete by reference is asked to, and is never handed a list of
    /// guessed chunk identifiers.
    /// </summary>
    [Fact]
    public async Task Remove_ProviderSupportsReferenceDelete_DoesNotEnumerateChunkIds()
    {
        var harness = new Harness
        {
            SupportsReferenceDelete = true,
        };

        await harness.CreateService().RemoveDataSourceDocumentsAsync(DataSourceId, ["doc-1"], TestContext.Current.CancellationToken);

        Assert.Equal([["doc-1"]], harness.ContentManager.DeletedReferenceIds);
        Assert.Empty(harness.DocumentManager.Deleted);
    }

    /// <summary>
    /// Verifies that a provider that cannot delete by reference still gets its rows deleted, the slow way.
    /// </summary>
    [Fact]
    public async Task Remove_ProviderReturnsFalse_FallsBackToChunkIdEnumeration()
    {
        var harness = new Harness
        {
            SupportsReferenceDelete = false,
        };

        await harness.CreateService().RemoveDataSourceDocumentsAsync(DataSourceId, ["doc-1"], TestContext.Current.CancellationToken);

        var deleted = Assert.Single(harness.DocumentManager.Deleted);

        Assert.Equal(1000, deleted.Count);
    }

    /// <summary>
    /// Verifies that the knowledge-base schema declares the typed discriminators as filterable, because
    /// filtering by them is the only reason they exist.
    /// </summary>
    [Fact]
    public async Task GetFields_KnowledgeBaseSchema_DeclaresTypedColumnsAsFilterable()
    {
        var deployment = new AIDeployment
        {
            ItemId = "deployment-1",
            Name = EmbeddingDeploymentName,
        };

        var deploymentStore = new Mock<IAIDeploymentStore>();
        deploymentStore
            .Setup(store => store.FindByNameAsync(EmbeddingDeploymentName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deployment);

        var clientFactory = new Mock<IAIClientFactory>();
        clientFactory
            .Setup(factory => factory.CreateEmbeddingGeneratorAsync(It.IsAny<AIDeployment>()))
            .ReturnsAsync(new FixedEmbeddingGenerator());

        var handler = new DataSourceSearchIndexProfileHandler(
            deploymentStore.Object,
            clientFactory.Object,
            NullLogger<DataSourceSearchIndexProfileHandler>.Instance);

        var fields = await handler.GetFieldsAsync(
            new SearchIndexProfile
            {
                Name = KnowledgeBaseName,
                Type = IndexProfileTypes.DataSource,
                EmbeddingDeploymentName = EmbeddingDeploymentName,
            },
            TestContext.Current.CancellationToken);

        foreach (var name in new[] { DataSourceConstants.ColumnNames.ContentType, DataSourceConstants.ColumnNames.RootId, DataSourceConstants.ColumnNames.ParentId })
        {
            var field = Assert.Single(fields, item => item.Name == name);

            Assert.Equal(SearchFieldType.Keyword, field.FieldType);
            Assert.True(field.IsFilterable);
        }

        var page = Assert.Single(fields, item => item.Name == DataSourceConstants.ColumnNames.Page);

        Assert.Equal(SearchFieldType.Integer, page.FieldType);
        Assert.True(page.IsFilterable);
    }

    /// <summary>
    /// Assembles the indexing service with fakes a test can inspect.
    /// </summary>
    private sealed class Harness
    {
        public Dictionary<string, SourceDocument> Documents { get; } = new(StringComparer.Ordinal);

        public RecordingSearchDocumentManager DocumentManager { get; } = new();

        public RecordingIndexManager IndexManager { get; } = new();

        public RecordingContentManager ContentManager { get; } = new();

        public bool SupportsReferenceDelete { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the source handler produces typed knowledge. Only such a
        /// handler means the knowledge base's own discriminators by the reserved names.
        /// </summary>
        public bool ProducesTypedKnowledge { get; set; } = true;

        public AIDataSource DataSource { get; } = new()
        {
            ItemId = DataSourceId,
            Source = AIDataSourceSourceTypes.SearchIndexProfile,
            DisplayText = "Knowledge base",
            AIKnowledgeBaseIndexProfileName = KnowledgeBaseName,
        };

        public Task SyncAsync()
        {
            return CreateService().SyncDataSourceAsync(DataSource, TestContext.Current.CancellationToken);
        }

        public DefaultAIDataSourceIndexingService CreateService()
        {
            ContentManager.SupportsReferenceDelete = SupportsReferenceDelete;

            var indexProfile = new SearchIndexProfile
            {
                Name = KnowledgeBaseName,
                ProviderName = ProviderName,
                Type = IndexProfileTypes.DataSource,
                EmbeddingDeploymentName = EmbeddingDeploymentName,
            };

            var dataSourceStore = new Mock<IAIDataSourceStore>();
            dataSourceStore.Setup(store => store.FindByIdAsync(DataSourceId, It.IsAny<CancellationToken>())).ReturnsAsync(DataSource);
            dataSourceStore.Setup(store => store.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([DataSource]);

            var indexProfileManager = new Mock<ISearchIndexProfileManager>();
            indexProfileManager
                .Setup(manager => manager.FindByNameAsync(KnowledgeBaseName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(indexProfile);
            indexProfileManager
                .Setup(manager => manager.GetFieldsAsync(It.IsAny<SearchIndexProfile>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SearchIndexField[]
                {
                    new()
                    {
                        Name = DataSourceConstants.ColumnNames.ContentType,
                        FieldType = SearchFieldType.Keyword,
                        IsFilterable = true,
                    },
                });

            var deployment = new AIDeployment
            {
                ItemId = "deployment-1",
                Name = EmbeddingDeploymentName,
            };

            var deploymentManager = new Mock<IAIDeploymentManager>();
            deploymentManager
                .Setup(manager => manager.FindByNameAsync(EmbeddingDeploymentName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(deployment);

            var clientFactory = new Mock<IAIClientFactory>();
            clientFactory
                .Setup(factory => factory.CreateEmbeddingGeneratorAsync(It.IsAny<AIDeployment>()))
                .ReturnsAsync(new FixedEmbeddingGenerator());

            var services = new ServiceCollection();
            services.AddKeyedSingleton<ISearchIndexManager>(ProviderName, IndexManager);
            services.AddKeyedSingleton<ISearchDocumentManager>(ProviderName, DocumentManager);
            services.AddKeyedSingleton<IDataSourceContentManager>(ProviderName, ContentManager);
            services.AddKeyedSingleton<IAIDataSourceSourceHandler>(
                AIDataSourceSourceTypes.SearchIndexProfile,
                new FakeSourceHandler(Documents, ProducesTypedKnowledge));

            return new DefaultAIDataSourceIndexingService(
                dataSourceStore.Object,
                indexProfileManager.Object,
                deploymentManager.Object,
                clientFactory.Object,
                new PassThroughTextNormalizer(),
                services.BuildServiceProvider(),
                TimeProvider.System,
                NullLogger<DefaultAIDataSourceIndexingService>.Instance);
        }
    }

    private sealed class RecordingIndexManager : ISearchIndexManager
    {
        public bool TryAddFieldsCalled { get; private set; }

        public Task<bool> ExistsAsync(IIndexProfileInfo profile, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public string ComposeIndexFullName(IIndexProfileInfo profile)
        {
            return $"prefix_{KnowledgeBaseName}";
        }

        public Task CreateAsync(IIndexProfileInfo profile, IReadOnlyCollection<SearchIndexField> fields, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(IIndexProfileInfo profile, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> TryAddFieldsAsync(IIndexProfileInfo profile, IReadOnlyCollection<SearchIndexField> fields, CancellationToken cancellationToken = default)
        {
            TryAddFieldsCalled = true;

            return Task.FromResult(true);
        }
    }

    private sealed class RecordingContentManager : IDataSourceContentManager
    {
        public bool SupportsReferenceDelete { get; set; }

        public List<IReadOnlyList<string>> DeletedReferenceIds { get; } = [];

        public Task<IEnumerable<DataSourceSearchResult>> SearchAsync(
            IIndexProfileInfo profile,
            float[] embedding,
            string dataSourceId,
            int topN,
            string filter = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<DataSourceSearchResult>>([]);
        }

        public Task<long> DeleteByDataSourceIdAsync(IIndexProfileInfo profile, string dataSourceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }

        public Task<bool> DeleteByReferenceIdsAsync(
            IIndexProfileInfo profile,
            string dataSourceId,
            IReadOnlyCollection<string> referenceIds,
            CancellationToken cancellationToken = default)
        {
            if (!SupportsReferenceDelete)
            {
                return Task.FromResult(false);
            }

            DeletedReferenceIds.Add(referenceIds.ToArray());

            return Task.FromResult(true);
        }
    }

    private sealed class RecordingSearchDocumentManager : ISearchDocumentManager
    {
        public List<IndexDocument> Written { get; } = [];

        public List<IReadOnlyList<string>> Deleted { get; } = [];

        public Task<bool> AddOrUpdateAsync(IIndexProfileInfo profile, IReadOnlyCollection<IndexDocument> documents, CancellationToken cancellationToken = default)
        {
            Written.AddRange(documents);

            return Task.FromResult(true);
        }

        public Task DeleteAsync(IIndexProfileInfo profile, IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
        {
            Deleted.Add(documentIds.ToArray());

            return Task.CompletedTask;
        }

        public Task DeleteAllAsync(IIndexProfileInfo profile, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSourceHandler : IAIDataSourceSourceHandler
    {
        private readonly Dictionary<string, SourceDocument> _documents;

        public FakeSourceHandler(Dictionary<string, SourceDocument> documents)
            : this(documents, producesTypedKnowledge: true)
        {
        }

        public FakeSourceHandler(Dictionary<string, SourceDocument> documents, bool producesTypedKnowledge)
        {
            _documents = documents;
            ProducesTypedKnowledge = producesTypedKnowledge;
        }

        public string SourceType => AIDataSourceSourceTypes.SearchIndexProfile;

        public bool ProducesTypedKnowledge { get; }

        public ValueTask ValidateAsync(AIDataSource dataSource, ValidationResultDetails result, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<string> GetReferenceTypeAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult("TestReference");
        }

        public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadAsync(
            AIDataSource dataSource,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var pair in _documents)
            {
                yield return pair;
            }

            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadByIdsAsync(
            AIDataSource dataSource,
            IEnumerable<string> documentIds,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var documentId in documentIds)
            {
                if (_documents.TryGetValue(documentId, out var document))
                {
                    yield return new KeyValuePair<string, SourceDocument>(documentId, document);
                }
            }

            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Leaves the text alone, so a test sees exactly what the service decided to store.
    /// </summary>
    private sealed class PassThroughTextNormalizer : IAITextNormalizer
    {
        public Task<string> NormalizeContentAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(text);
        }

        public Task<List<string>> NormalizeAndChunkAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.IsNullOrWhiteSpace(text) ? new List<string>() : [text]);
        }

        public string NormalizeTitle(string title)
        {
            return title;
        }
    }

    private sealed class FixedEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions options = null,
            CancellationToken cancellationToken = default)
        {
            var embeddings = values.Select(_ => new Embedding<float>(new[] { 0.25f, 0.5f })).ToList();

            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object GetService(Type serviceType, object serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }
    }
}
