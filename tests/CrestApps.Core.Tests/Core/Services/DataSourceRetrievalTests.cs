using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Pins the text a data source search hands back to the model. Typed retrieval is about to add blocks to
/// this rendering, and a text-only result set must keep rendering exactly as it does today, character for
/// character. This test is not edited to make a later change pass.
/// </summary>
public sealed class DataSourceRetrievalTests
{
    private const string DataSourceId = "data-source-1";
    private const string ProviderName = "TestProvider";
    private const string KnowledgeBaseName = "kb-index";
    private const string EmbeddingDeploymentName = "text-embedding-3-small";

    /// <summary>
    /// Verifies the rendered result for a result set that holds nothing but text chunks.
    /// </summary>
    [Fact]
    public async Task SearchAsync_TextUnchangedForTextOnlyResults()
    {
        var results = new[]
        {
            new DataSourceSearchResult
            {
                ReferenceId = "doc-1",
                Title = "Vacation policy",
                Content = "Employees accrue 15 days per year.",
                ChunkIndex = 0,
                ReferenceType = "Content",
                Score = 0.91f,
            },
            new DataSourceSearchResult
            {
                ReferenceId = "doc-2",
                Title = "Sick leave",
                Content = "Sick leave is granted separately.",
                ChunkIndex = 0,
                ReferenceType = "Content",
                Score = 0.72f,
            },
        };

        await using var services = BuildServices(results);

        var text = await DataSourceRetrieval.SearchAsync(
            services,
            new DataSourceRetrievalRequest
            {
                DataSourceId = DataSourceId,
                Queries = ["time off"],
                RetrievalMode = DataSourceRetrievalMode.Chunk,
            },
            "knowledge-base",
            NullLogger.Instance,
            TestContext.Current.CancellationToken);

        const string expected = """
            Relevant content from data source:
            ---
            [doc:1] Title: Vacation policy
            [doc:1] Employees accrue 15 days per year.
            ---
            [doc:2] Title: Sick leave
            [doc:2] Sick leave is granted separately.

            References:
            [doc:1] = doc-1
            [doc:2] = doc-2

            """;

        Assert.Equal(expected.ReplaceLineEndings("\n"), text.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// Verifies that a knowledge base which could not be searched is reported as unreachable, and never as a
    /// knowledge base that holds nothing.
    /// </summary>
    /// <remarks>
    /// This is the whole point of the outcome contract. With the database stopped, every provider handed back
    /// an empty list, retrieval said "No relevant content was found", and the model stated with confidence
    /// that the content did not exist — an answer that reads exactly like a real one.
    /// </remarks>
    [Fact]
    public async Task SearchAsync_WhenTheIndexCannotBeReached_ReportsTheFailureInsteadOfReportingNoContent()
    {
        await using var unreachable = BuildServices(new OutcomeContentManager(DataSourceSearchOutcome.Failure()));
        await using var searchable = BuildServices(new OutcomeContentManager(DataSourceSearchOutcome.Success([])));

        var failedText = await RunSearchAsync(unreachable);
        var emptyText = await RunSearchAsync(searchable);

        // A search that failed and a search that matched nothing must not read alike.
        Assert.NotEqual(emptyText, failedText);

        Assert.Contains("could not be reached", failedText, StringComparison.Ordinal);
        Assert.DoesNotContain("No relevant content was found", failedText, StringComparison.Ordinal);

        // The index that was reachable and matched nothing still says so, in the words it always used.
        Assert.Contains("No relevant content was found in the data source for this query.", emptyText, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be reached", emptyText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a provider which has not adopted the outcome contract and answers a dead index by
    /// throwing is still reported as a failure rather than as an absence.
    /// </summary>
    [Fact]
    public async Task SearchAsync_WhenTheProviderThrows_ReportsTheFailureWithoutLeakingItsDetail()
    {
        await using var services = BuildServices(new ThrowingContentManager());

        var text = await RunSearchAsync(services);

        Assert.Contains("could not be reached", text, StringComparison.Ordinal);
        Assert.DoesNotContain("No relevant content was found", text, StringComparison.Ordinal);

        // Nothing the provider said about its own plumbing reaches the model.
        Assert.DoesNotContain("Connection refused", text, StringComparison.Ordinal);
    }

    private static Task<string> RunSearchAsync(IServiceProvider services)
    {
        return DataSourceRetrieval.SearchAsync(
            services,
            new DataSourceRetrievalRequest
            {
                DataSourceId = DataSourceId,
                Queries = ["time off"],
                RetrievalMode = DataSourceRetrievalMode.Chunk,
            },
            "knowledge-base",
            NullLogger.Instance,
            TestContext.Current.CancellationToken);
    }

    private static ServiceProvider BuildServices(IReadOnlyList<DataSourceSearchResult> results)
    {
        var contentManager = new Mock<IDataSourceContentManager>();
        contentManager
            .Setup(manager => manager.SearchAsync(
                It.IsAny<IIndexProfileInfo>(),
                It.IsAny<float[]>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(results);

        return BuildServices(contentManager.Object);
    }

    private static ServiceProvider BuildServices(IDataSourceContentManager contentManager)
    {
        var dataSource = new AIDataSource
        {
            ItemId = DataSourceId,
            Source = AIDataSourceSourceTypes.SearchIndexProfile,
            DisplayText = "Knowledge base",
            AIKnowledgeBaseIndexProfileName = KnowledgeBaseName,
        };

        var indexProfile = new SearchIndexProfile
        {
            Name = KnowledgeBaseName,
            ProviderName = ProviderName,
            Type = IndexProfileTypes.DataSource,
            EmbeddingDeploymentName = EmbeddingDeploymentName,
        };

        var deployment = new AIDeployment
        {
            ItemId = "deployment-1",
            Name = EmbeddingDeploymentName,
        };

        var dataSourceStore = new Mock<IAIDataSourceStore>();
        dataSourceStore
            .Setup(store => store.FindByIdAsync(DataSourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dataSource);

        var indexProfileStore = new Mock<ISearchIndexProfileStore>();
        indexProfileStore
            .Setup(store => store.FindByNameAsync(KnowledgeBaseName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexProfile);

        var deploymentManager = new Mock<IAIDeploymentManager>();
        deploymentManager
            .Setup(manager => manager.FindByNameAsync(EmbeddingDeploymentName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deployment);

        var clientFactory = new Mock<IAIClientFactory>();
        clientFactory
            .Setup(factory => factory.CreateEmbeddingGeneratorAsync(It.IsAny<AIDeployment>()))
            .ReturnsAsync(new FixedEmbeddingGenerator());

        var textNormalizer = new Mock<IAITextNormalizer>();
        textNormalizer
            .Setup(normalizer => normalizer.NormalizeTitle(It.IsAny<string>()))
            .Returns((string title) => title);

        var services = new ServiceCollection();

        services.AddSingleton(dataSourceStore.Object);
        services.AddSingleton(indexProfileStore.Object);
        services.AddSingleton(deploymentManager.Object);
        services.AddSingleton(clientFactory.Object);
        services.AddSingleton(textNormalizer.Object);
        services.AddKeyedSingleton(ProviderName, contentManager);
        services.AddSingleton<IOptionsMonitor<AIDataSourceOptions>>(new TestOptionsMonitor<AIDataSourceOptions>
        {
            CurrentValue = new AIDataSourceOptions(),
        });
        services.AddLogging();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A provider that reports the outcome of its searches, the way the providers in this repository do.
    /// </summary>
    private sealed class OutcomeContentManager : IDataSourceContentManager
    {
        private readonly DataSourceSearchOutcome _outcome;

        public OutcomeContentManager(DataSourceSearchOutcome outcome)
        {
            _outcome = outcome;
        }

        public Task<DataSourceSearchOutcome> TrySearchAsync(
            IIndexProfileInfo indexProfile,
            float[] embedding,
            string dataSourceId,
            int topN,
            string filter = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_outcome);
        }

        public Task<IEnumerable<DataSourceSearchResult>> SearchAsync(
            IIndexProfileInfo indexProfile,
            float[] embedding,
            string dataSourceId,
            int topN,
            string filter = null,
            CancellationToken cancellationToken = default)
        {
            // The older contract cannot say more than "no rows", which is exactly why the outcome exists.
            return Task.FromResult(_outcome.Results);
        }

        public Task<long> DeleteByDataSourceIdAsync(IIndexProfileInfo indexProfile, string dataSourceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }
    }

    /// <summary>
    /// A provider from outside this repository: it has not adopted the outcome contract, and reports a dead
    /// index the only way the older contract allows — by throwing.
    /// </summary>
    private sealed class ThrowingContentManager : IDataSourceContentManager
    {
        public Task<IEnumerable<DataSourceSearchResult>> SearchAsync(
            IIndexProfileInfo indexProfile,
            float[] embedding,
            string dataSourceId,
            int topN,
            string filter = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Connection refused by the test double.");
        }

        public Task<long> DeleteByDataSourceIdAsync(IIndexProfileInfo indexProfile, string dataSourceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
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
}
