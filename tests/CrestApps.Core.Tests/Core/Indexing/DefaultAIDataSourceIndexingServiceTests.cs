using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Indexing;

/// <summary>
/// Pins what the knowledge-base indexing service writes and deletes today. The row shape and the delete
/// strategy are both about to change, so these tests are the "before" the later phases are measured against
/// and are not edited to make a change pass.
/// </summary>
public sealed class DefaultAIDataSourceIndexingServiceTests
{
    private const string DataSourceId = "data-source-1";
    private const string ProviderName = "TestProvider";
    private const string KnowledgeBaseName = "kb-index";
    private const string EmbeddingDeploymentName = "text-embedding-3-small";
    private const string ReferenceTypeName = "TestReference";

    private static readonly DateTimeOffset _now = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    /// Verifies the exact field set, the chunk identifier shape and the title prepended to the first chunk
    /// for a source document that is chunked by the normalizer rather than arriving pre-chunked.
    /// </summary>
    /// <remarks>
    /// This pin has been edited exactly once, and this is it: typed knowledge added a <c>contentType</c>
    /// column, and a source that says nothing about its type is text. Nothing else about the row may change
    /// without the change being wrong.
    /// </remarks>
    [Fact]
    public async Task PlainSource_RowFieldsMatchToday()
    {
        var documentManager = new RecordingSearchDocumentManager();
        var harness = new Harness(documentManager)
        {
            Documents =
            {
                ["doc-1"] = new SourceDocument
                {
                    Title = "Vacation policy",
                    Content = "first chunk\n\nsecond chunk",
                },
            },
        };

        await harness.CreateService().SyncDataSourceAsync(harness.DataSource, TestContext.Current.CancellationToken);

        Assert.Equal(2, documentManager.Written.Count);
        Assert.Equal(["doc-1_0", "doc-1_1"], documentManager.Written.Select(document => document.Id));

        var first = documentManager.Written[0];

        Assert.Equal(
            [
                DataSourceConstants.ColumnNames.ChunkId,
                DataSourceConstants.ColumnNames.ReferenceId,
                DataSourceConstants.ColumnNames.DataSourceId,
                DataSourceConstants.ColumnNames.ReferenceType,
                DataSourceConstants.ColumnNames.ChunkIndex,
                DataSourceConstants.ColumnNames.Title,
                DataSourceConstants.ColumnNames.Content,
                DataSourceConstants.ColumnNames.Embedding,
                DataSourceConstants.ColumnNames.Timestamp,
                DataSourceConstants.ColumnNames.ContentType,
            ],
            first.Fields.Keys);

        Assert.Equal(KnowledgeObjectTypes.Text, first.Fields[DataSourceConstants.ColumnNames.ContentType]);

        Assert.Equal("doc-1_0", first.Fields[DataSourceConstants.ColumnNames.ChunkId]);
        Assert.Equal("doc-1", first.Fields[DataSourceConstants.ColumnNames.ReferenceId]);
        Assert.Equal(DataSourceId, first.Fields[DataSourceConstants.ColumnNames.DataSourceId]);
        Assert.Equal(ReferenceTypeName, first.Fields[DataSourceConstants.ColumnNames.ReferenceType]);
        Assert.Equal(0, first.Fields[DataSourceConstants.ColumnNames.ChunkIndex]);
        Assert.Equal("Vacation policy", first.Fields[DataSourceConstants.ColumnNames.Title]);
        Assert.Equal("Vacation policy\nfirst chunk", first.Fields[DataSourceConstants.ColumnNames.Content]);
        Assert.Equal(_now.UtcDateTime, first.Fields[DataSourceConstants.ColumnNames.Timestamp]);
        Assert.Equal([0.25f, 0.5f], Assert.IsType<float[]>(first.Fields[DataSourceConstants.ColumnNames.Embedding]));

        var second = documentManager.Written[1];

        Assert.Equal(1, second.Fields[DataSourceConstants.ColumnNames.ChunkIndex]);
        Assert.Equal("second chunk", second.Fields[DataSourceConstants.ColumnNames.Content]);
    }

    /// <summary>
    /// Verifies that removing one document still enumerates a fixed span of chunk identifiers per reference,
    /// because no provider can delete by reference identifier yet.
    /// </summary>
    [Fact]
    public async Task Remove_ProviderWithoutReferenceDelete_FallsBackToChunkIdEnumeration()
    {
        var documentManager = new RecordingSearchDocumentManager();
        var harness = new Harness(documentManager);

        await harness.CreateService().RemoveDataSourceDocumentsAsync(
            DataSourceId,
            ["doc-1"],
            TestContext.Current.CancellationToken);

        var deleted = Assert.Single(documentManager.Deleted);

        Assert.Equal(1000, deleted.Count);
        Assert.Equal("doc-1_0", deleted[0]);
        Assert.Equal("doc-1_999", deleted[^1]);
    }

    /// <summary>
    /// Verifies that a sync which reached the index records that it did, and how much it wrote.
    /// </summary>
    [Fact]
    public async Task Sync_WhenTheIndexAcceptsTheWrite_RecordsSuccessAndTheDocumentCount()
    {
        var documentManager = new RecordingSearchDocumentManager();
        var harness = new Harness(documentManager)
        {
            Documents =
            {
                ["doc-1"] = new SourceDocument
                {
                    Title = "Vacation policy",
                    Content = "first chunk\n\nsecond chunk",
                },
            },
        };

        await harness.CreateService().SyncDataSourceAsync(harness.DataSource, TestContext.Current.CancellationToken);

        var summary = harness.ReadRecordedSummary();

        Assert.NotNull(summary);
        Assert.Equal(AIDataSourceSyncStatus.Succeeded, summary.Status);
        Assert.Equal(2, summary.DocumentsIndexed);
        Assert.Null(summary.Error);
        Assert.Equal(_now.UtcDateTime, summary.StartedUtc);
        Assert.Equal(_now.UtcDateTime, summary.CompletedUtc);
    }

    /// <summary>
    /// Verifies that an index which refuses the write leaves the reason on the data source, rather than a
    /// data source that reads as perfectly normal and holds nothing.
    /// </summary>
    /// <remarks>
    /// This is the one that was paid for in production: a file was ingested while the vector database was
    /// stopped, the knowledge objects were written, the index write failed, and the data source reported an
    /// ordinary state with zero rows in it for days.
    /// </remarks>
    [Fact]
    public async Task Sync_WhenTheIndexRefusesTheWrite_RecordsTheFailureAndItsReason()
    {
        var documentManager = new RecordingSearchDocumentManager
        {
            WriteSucceeds = false,
        };

        var harness = new Harness(documentManager)
        {
            Documents =
            {
                ["doc-1"] = new SourceDocument
                {
                    Title = "Vacation policy",
                    Content = "first chunk",
                },
            },
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.CreateService().SyncDataSourceAsync(harness.DataSource, TestContext.Current.CancellationToken));

        var summary = harness.ReadRecordedSummary();

        Assert.NotNull(summary);
        Assert.Equal(AIDataSourceSyncStatus.Failed, summary.Status);
        Assert.Equal(0, summary.DocumentsIndexed);
        Assert.Contains("Knowledge-base indexing failed", summary.Error, StringComparison.Ordinal);

        // The caller of a failed sync throws its session away, so the record only reaches storage because it
        // was committed on a session of its own.
        Assert.Equal(1, harness.Commits);
    }

    /// <summary>
    /// Verifies that a data source the service cannot even build a context for records why, instead of
    /// returning as though there had been nothing to do.
    /// </summary>
    [Fact]
    public async Task Sync_WhenTheKnowledgeBaseProfileIsMissing_RecordsTheReasonInsteadOfSkippingQuietly()
    {
        var documentManager = new RecordingSearchDocumentManager();
        var harness = new Harness(documentManager);

        harness.DataSource.AIKnowledgeBaseIndexProfileName = "kb-index-that-is-not-registered";

        await harness.CreateService().SyncDataSourceAsync(harness.DataSource, TestContext.Current.CancellationToken);

        var summary = harness.ReadRecordedSummary();

        Assert.NotNull(summary);
        Assert.Equal(AIDataSourceSyncStatus.Failed, summary.Status);
        Assert.Equal(0, summary.DocumentsIndexed);
        Assert.Contains("kb-index-that-is-not-registered", summary.Error, StringComparison.Ordinal);
        Assert.Empty(documentManager.Written);
    }

    /// <summary>
    /// Verifies that the incremental path records its outcome too. Ingesting a file syncs the documents it
    /// produced rather than the whole data source, so it is the path a failed ingest actually takes.
    /// </summary>
    [Fact]
    public async Task SyncDocuments_WhenTheIndexRefusesTheWrite_RecordsTheFailureOnTheDataSource()
    {
        var documentManager = new RecordingSearchDocumentManager
        {
            WriteSucceeds = false,
        };

        var harness = new Harness(documentManager)
        {
            Documents =
            {
                ["doc-1"] = new SourceDocument
                {
                    Title = "Vacation policy",
                    Content = "first chunk",
                },
            },
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.CreateService().SyncDataSourceDocumentsAsync(DataSourceId, ["doc-1"], TestContext.Current.CancellationToken));

        var summary = harness.ReadRecordedSummary();

        Assert.NotNull(summary);
        Assert.Equal(AIDataSourceSyncStatus.Failed, summary.Status);
        Assert.Contains("Knowledge-base indexing failed", summary.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Assembles the collaborators the indexing service resolves, so a test only supplies the source
    /// documents it wants indexed.
    /// </summary>
    private sealed class Harness
    {
        private readonly RecordingSearchDocumentManager _documentManager;
        private readonly RecordingStoreCommitter _committer = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="Harness"/> class.
        /// </summary>
        /// <param name="documentManager">The recording document manager the service writes through.</param>
        public Harness(RecordingSearchDocumentManager documentManager)
        {
            _documentManager = documentManager;

            DataSource = new AIDataSource
            {
                ItemId = DataSourceId,
                Source = AIDataSourceSourceTypes.SearchIndexProfile,
                DisplayText = "Knowledge base",
                AIKnowledgeBaseIndexProfileName = KnowledgeBaseName,
            };
        }

        /// <summary>
        /// Gets the data source under test.
        /// </summary>
        public AIDataSource DataSource { get; }

        /// <summary>
        /// Gets the data source the service wrote back to the store, or <see langword="null"/> when it never
        /// wrote one.
        /// </summary>
        public AIDataSource Recorded { get; private set; }

        /// <summary>
        /// Gets how many times the service committed a store session of its own.
        /// </summary>
        /// <remarks>
        /// A sync that failed is rolled back by whoever called it, so the outcome only survives if it was
        /// committed on its own session. Counting the commits is how that is pinned.
        /// </remarks>
        public int Commits => _committer.Commits;

        /// <summary>
        /// Gets the source documents the fake source handler returns, keyed by reference identifier.
        /// </summary>
        public Dictionary<string, SourceDocument> Documents { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Reads back the sync outcome the service recorded on the data source.
        /// </summary>
        /// <returns>The recorded outcome, or <see langword="null"/> when nothing was recorded.</returns>
        public AIDataSourceSyncSummary ReadRecordedSummary()
        {
            return Recorded != null && Recorded.TryGet<AIDataSourceSyncSummary>(out var summary) ? summary : null;
        }

        /// <summary>
        /// Builds the service with every collaborator wired up.
        /// </summary>
        /// <returns>The service under test.</returns>
        public DefaultAIDataSourceIndexingService CreateService()
        {
            var indexProfile = new SearchIndexProfile
            {
                Name = KnowledgeBaseName,
                ProviderName = ProviderName,
                Type = IndexProfileTypes.DataSource,
                EmbeddingDeploymentName = EmbeddingDeploymentName,
            };

            var dataSourceStore = new Mock<IAIDataSourceStore>();
            dataSourceStore
                .Setup(store => store.FindByIdAsync(DataSourceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(DataSource);
            dataSourceStore
                .Setup(store => store.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([DataSource]);
            dataSourceStore
                .Setup(store => store.UpdateAsync(It.IsAny<AIDataSource>(), It.IsAny<CancellationToken>()))
                .Callback<AIDataSource, CancellationToken>((entry, _) => Recorded = entry)
                .Returns(ValueTask.CompletedTask);

            var indexProfileManager = new Mock<ISearchIndexProfileManager>();
            indexProfileManager
                .Setup(manager => manager.FindByNameAsync(KnowledgeBaseName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(indexProfile);

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

            var indexManager = new Mock<ISearchIndexManager>();
            indexManager
                .Setup(manager => manager.ExistsAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            indexManager
                .Setup(manager => manager.ComposeIndexFullName(It.IsAny<IIndexProfileInfo>()))
                .Returns($"prefix_{KnowledgeBaseName}");

            var contentManager = new Mock<IDataSourceContentManager>();
            contentManager
                .Setup(manager => manager.DeleteByDataSourceIdAsync(It.IsAny<IIndexProfileInfo>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0L);

            var services = new ServiceCollection();
            services.AddKeyedSingleton(ProviderName, indexManager.Object);
            services.AddKeyedSingleton(ProviderName, _documentManager as ISearchDocumentManager);
            services.AddKeyedSingleton(ProviderName, contentManager.Object);
            services.AddKeyedSingleton<IAIDataSourceSourceHandler>(
                AIDataSourceSourceTypes.SearchIndexProfile,
                new FakeSourceHandler(Documents));

            // The outcome is recorded on a store session of its own, which the service resolves from a fresh
            // scope rather than from the session the sync itself ran on.
            services.AddSingleton(dataSourceStore.Object);
            services.AddSingleton<IStoreCommitter>(_committer);

            return new DefaultAIDataSourceIndexingService(
                dataSourceStore.Object,
                indexProfileManager.Object,
                deploymentManager.Object,
                clientFactory.Object,
                new ParagraphTextNormalizer(),
                services.BuildServiceProvider(),
                new FixedTimeProvider(_now),
                NullLogger<DefaultAIDataSourceIndexingService>.Instance);
        }
    }

    private sealed class RecordingSearchDocumentManager : ISearchDocumentManager
    {
        public List<IndexDocument> Written { get; } = [];

        public List<IReadOnlyList<string>> Deleted { get; } = [];

        /// <summary>
        /// Gets or sets a value indicating whether the index accepts what it is handed. Set it to
        /// <see langword="false"/> to stand in for an index that is not reachable.
        /// </summary>
        public bool WriteSucceeds { get; set; } = true;

        public Task<bool> AddOrUpdateAsync(IIndexProfileInfo profile, IReadOnlyCollection<IndexDocument> documents, CancellationToken cancellationToken = default)
        {
            if (!WriteSucceeds)
            {
                return Task.FromResult(false);
            }

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
        {
            _documents = documents;
        }

        public string SourceType => AIDataSourceSourceTypes.SearchIndexProfile;

        public ValueTask ValidateAsync(AIDataSource dataSource, ValidationResultDetails result, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<string> GetReferenceTypeAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(ReferenceTypeName);
        }

        public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadAsync(
            AIDataSource dataSource,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var pair in _documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

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
                cancellationToken.ThrowIfCancellationRequested();

                if (_documents.TryGetValue(documentId, out var document))
                {
                    yield return new KeyValuePair<string, SourceDocument>(documentId, document);
                }
            }

            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Splits on a blank line so a test controls the chunk count directly, and leaves titles untouched.
    /// </summary>
    private sealed class ParagraphTextNormalizer : IAITextNormalizer
    {
        public Task<string> NormalizeContentAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(text);
        }

        public Task<List<string>> NormalizeAndChunkAsync(string text, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Task.FromResult(new List<string>());
            }

            return Task.FromResult(text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).ToList());
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
            var embeddings = values
                .Select(_ => new Embedding<float>(new[] { 0.25f, 0.5f }))
                .ToList();

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

    private sealed class RecordingStoreCommitter : IStoreCommitter
    {
        public int Commits { get; private set; }

        public ValueTask CommitAsync(CancellationToken cancellationToken = default)
        {
            Commits++;

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
