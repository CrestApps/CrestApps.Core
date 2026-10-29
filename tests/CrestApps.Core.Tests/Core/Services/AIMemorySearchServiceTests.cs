using CrestApps.Core.AI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class AIMemorySearchServiceTests
{
    [Fact]
    public async Task SearchAsync_WhenIndexProfileIsNotConfigured_ReturnsEmpty()
    {
        var service = CreateService(options: new AIMemoryOptions());

        var results = await service.SearchAsync("user-1", ["hello"], requestedTopN: null, TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_WhenDependenciesResolve_ReturnsAggregatedResults()
    {
        var indexProfileStore = new Mock<ISearchIndexProfileStore>();
        indexProfileStore
            .Setup(store => store.FindByNameAsync("memory-profile", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchIndexProfile
            {
                Name = "memory-profile",
                ProviderName = "AzureAISearch",
                Type = IndexProfileTypes.AIMemory,
                EmbeddingDeploymentName = "deployment-1",
            });

        var deploymentManager = new Mock<IAIDeploymentManager>();
        deploymentManager
            .Setup(manager => manager.FindByNameAsync("deployment-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIDeployment
            {
                ItemId = "deployment-1",
                ClientName = "AzureOpenAI",
                ConnectionName = "Default",
                Name = "text-embedding-3-small",
                ModelName = "text-embedding-3-small",
            });

        var vectorSearchService = new Mock<IMemoryVectorSearchService>();
        vectorSearchService
            .Setup(service => service.SearchAsync(
                It.IsAny<SearchIndexProfile>(),
                It.IsAny<float[]>(),
                "user-1",
                2,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new AIMemorySearchResult
                {
                    MemoryId = "memory-1",
                    Name = "project",
                    Content = "CrestApps.OrchardCore",
                    Score = 0.75f,
                },
                new AIMemorySearchResult
                {
                    MemoryId = "memory-1",
                    Name = "project",
                    Content = "CrestApps.OrchardCore",
                    Score = 0.91f,
                },
            ]);

        var aiClientFactory = new Mock<IAIClientFactory>();
        aiClientFactory
            .Setup(factory => factory.CreateEmbeddingGeneratorAsync(It.Is<AIDeployment>(d => d.ClientName == "AzureOpenAI" && d.ConnectionName == "Default" && d.ModelName == "text-embedding-3-small")))
            .Returns(new ValueTask<IEmbeddingGenerator<string, Embedding<float>>>(new FakeEmbeddingGenerator([1f, 2f, 3f])));

        var service = CreateService(
            options: new AIMemoryOptions
            {
                IndexProfileName = "memory-profile",
                TopN = 2,
            },
            aiClientFactory: aiClientFactory.Object,
            configureServices: services =>
            {
                services.AddSingleton(indexProfileStore.Object);
                services.AddSingleton(deploymentManager.Object);
                services.AddKeyedSingleton<IMemoryVectorSearchService>("AzureAISearch", vectorSearchService.Object);
            });

        var results = (await service.SearchAsync("user-1", ["hello", "hello "], requestedTopN: null, TestContext.Current.CancellationToken)).ToList();

        var result = Assert.Single(results);
        Assert.Equal("memory-1", result.MemoryId);
        Assert.Equal(0.91f, result.Score);
    }

    [Fact]
    public async Task SearchAsync_WhenEmbeddingDeploymentExistsOnlyInMetadata_ReturnsAggregatedResults()
    {
        var indexProfileStore = new Mock<ISearchIndexProfileStore>();
        indexProfileStore
            .Setup(store => store.FindByNameAsync("memory-profile", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchIndexProfile
            {
                Name = "memory-profile",
                ProviderName = "AzureAISearch",
                Type = IndexProfileTypes.AIMemory,
                Properties = new Dictionary<string, object>
                {
                    [nameof(DataSourceIndexProfileMetadata)] = new DataSourceIndexProfileMetadata
                    {
                        EmbeddingDeploymentName = "deployment-1",
                    },
                },
            });

        var deploymentManager = new Mock<IAIDeploymentManager>();
        deploymentManager
            .Setup(manager => manager.FindByNameAsync("deployment-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIDeployment
            {
                ItemId = "deployment-1",
                ClientName = "AzureOpenAI",
                ConnectionName = "Default",
                Name = "text-embedding-3-small",
                ModelName = "text-embedding-3-small",
            });

        var vectorSearchService = new Mock<IMemoryVectorSearchService>();
        vectorSearchService
            .Setup(service => service.SearchAsync(
                It.IsAny<SearchIndexProfile>(),
                It.IsAny<float[]>(),
                "user-1",
                2,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new AIMemorySearchResult
                {
                    MemoryId = "memory-1",
                    Name = "project",
                    Content = "CrestApps.OrchardCore",
                    Score = 0.88f,
                },
            ]);

        var aiClientFactory = new Mock<IAIClientFactory>();
        aiClientFactory
            .Setup(factory => factory.CreateEmbeddingGeneratorAsync(It.Is<AIDeployment>(d => d.ItemId == "deployment-1")))
            .Returns(new ValueTask<IEmbeddingGenerator<string, Embedding<float>>>(new FakeEmbeddingGenerator([1f, 2f, 3f])));

        var service = CreateService(
            options: new AIMemoryOptions
            {
                IndexProfileName = "memory-profile",
                TopN = 2,
            },
            aiClientFactory: aiClientFactory.Object,
            configureServices: services =>
            {
                services.AddSingleton(indexProfileStore.Object);
                services.AddSingleton(deploymentManager.Object);
                services.AddKeyedSingleton<IMemoryVectorSearchService>("AzureAISearch", vectorSearchService.Object);
            });

        var results = (await service.SearchAsync("user-1", ["hello"], requestedTopN: null, TestContext.Current.CancellationToken)).ToList();

        var result = Assert.Single(results);
        Assert.Equal("memory-1", result.MemoryId);
        Assert.Equal(0.88f, result.Score);
    }

    [Fact]
    public async Task SearchAsync_WhenIndexProfileIsNotConfigured_RanksStoredMemoriesByMatchingTerms()
    {
        var service = CreateService(
            options: new AIMemoryOptions(),
            memories:
            [
                CreateMemory("memory-1", "favorite_framework", "The user's stated favorite framework.", "The user loves OrchardCore."),
                CreateMemory("memory-2", "preferred_name", "The user's preferred display name.", "The user prefers to be called Mike."),
            ]);

        var results = (await service.SearchAsync("user-1", ["user preferred name"], requestedTopN: null, TestContext.Current.CancellationToken)).ToList();

        Assert.Equal("memory-2", results[0].MemoryId);
        Assert.True(results[0].Score > results.Skip(1).Select(result => result.Score).DefaultIfEmpty(0).Max());
    }

    [Fact]
    public async Task SearchAsync_WhenNoStoredMemoryShareATerm_ReturnsEmpty()
    {
        var service = CreateService(
            options: new AIMemoryOptions(),
            memories:
            [
                CreateMemory("memory-1", "favorite_framework", "The user's stated favorite framework.", "The user loves OrchardCore."),
            ]);

        var results = await service.SearchAsync("user-1", ["weather tomorrow"], requestedTopN: null, TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_WhenIndexHoldsNothingForUser_FallsBackToMemoryStore()
    {
        var indexProfileStore = new Mock<ISearchIndexProfileStore>();
        indexProfileStore
            .Setup(store => store.FindByNameAsync("memory-profile", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchIndexProfile
            {
                Name = "memory-profile",
                ProviderName = "AzureAISearch",
                Type = IndexProfileTypes.AIMemory,
                EmbeddingDeploymentName = "deployment-1",
            });

        var deploymentManager = new Mock<IAIDeploymentManager>();
        deploymentManager
            .Setup(manager => manager.FindByNameAsync("deployment-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIDeployment { ItemId = "deployment-1" });

        var vectorSearchService = new Mock<IMemoryVectorSearchService>();
        vectorSearchService
            .Setup(service => service.SearchAsync(It.IsAny<SearchIndexProfile>(), It.IsAny<float[]>(), "user-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var aiClientFactory = new Mock<IAIClientFactory>();
        aiClientFactory
            .Setup(factory => factory.CreateEmbeddingGeneratorAsync(It.IsAny<AIDeployment>()))
            .Returns(new ValueTask<IEmbeddingGenerator<string, Embedding<float>>>(new FakeEmbeddingGenerator([1f, 2f, 3f])));

        var service = CreateService(
            options: new AIMemoryOptions { IndexProfileName = "memory-profile" },
            aiClientFactory: aiClientFactory.Object,
            configureServices: services =>
            {
                services.AddSingleton(indexProfileStore.Object);
                services.AddSingleton(deploymentManager.Object);
                services.AddKeyedSingleton<IMemoryVectorSearchService>("AzureAISearch", vectorSearchService.Object);
            },
            memories:
            [
                CreateMemory("memory-1", "favorite_framework", "The user's stated favorite framework.", "The user loves OrchardCore."),
            ]);

        var results = await service.SearchAsync("user-1", ["favorite framework"], requestedTopN: null, TestContext.Current.CancellationToken);

        Assert.Equal("memory-1", Assert.Single(results).MemoryId);
    }

    private static AIMemoryEntry CreateMemory(string id, string name, string description, string content)
    {
        return new AIMemoryEntry
        {
            ItemId = id,
            UserId = "user-1",
            Name = name,
            Description = description,
            Content = content,
            UpdatedUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        };
    }

    private static AIMemorySearchService CreateService(
        AIMemoryOptions options,
        IAIClientFactory aiClientFactory = null,
        Action<IServiceCollection> configureServices = null,
        IReadOnlyCollection<AIMemoryEntry> memories = null)
    {
        var memoryStore = new Mock<IAIMemoryStore>();
        memoryStore
            .Setup(store => store.GetByUserAsync("user-1", It.IsAny<int>()))
            .ReturnsAsync(memories ?? []);

        var services = new ServiceCollection();
        services.AddSingleton(aiClientFactory ?? Mock.Of<IAIClientFactory>());
        services.AddSingleton(Mock.Of<ISearchIndexProfileStore>());
        services.AddSingleton(Mock.Of<IAIDeploymentManager>());
        configureServices?.Invoke(services);

        var serviceProvider = services.BuildServiceProvider();

        return new AIMemorySearchService(
                    serviceProvider,
                    serviceProvider.GetRequiredService<ISearchIndexProfileStore>(),
                    serviceProvider.GetRequiredService<IAIDeploymentManager>(),
                    serviceProvider.GetRequiredService<IAIClientFactory>(),
                    memoryStore.Object,
                    new LuceneTextTokenizer(),
                    new TestOptionsMonitor<AIMemoryOptions> { CurrentValue = options },
                    NullLogger<AIMemorySearchService>.Instance);
    }

    private sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly float[] _vector;

        public FakeEmbeddingGenerator(float[] vector)
        {
            _vector = vector;
        }

        public EmbeddingGeneratorMetadata Metadata { get; } = new("fake");

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions options = null,
            CancellationToken cancellationToken = default)
        {
            var embeddings = new GeneratedEmbeddings<Embedding<float>>();

            foreach (var _ in values)
            {
                embeddings.Add(new Embedding<float>(_vector));
            }

            return Task.FromResult(embeddings);
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
