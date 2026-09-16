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

    private static ServiceProvider BuildServices(IReadOnlyList<DataSourceSearchResult> results)
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
        services.AddKeyedSingleton(ProviderName, contentManager.Object);
        services.AddSingleton<IOptionsMonitor<AIDataSourceOptions>>(new TestOptionsMonitor<AIDataSourceOptions>
        {
            CurrentValue = new AIDataSourceOptions(),
        });
        services.AddLogging();

        return services.BuildServiceProvider();
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
