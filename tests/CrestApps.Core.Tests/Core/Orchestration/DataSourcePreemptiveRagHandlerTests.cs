using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Services;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

#pragma warning disable MEAI001
namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class DataSourcePreemptiveRagHandlerTests
{
    [Fact]
    public async Task HandleAsync_PrioritizesRawUserQueryAndInjectsMatchingContext()
    {
        var dataSourceStore = new Mock<IAIDataSourceStore>();
        dataSourceStore.Setup(store => store.FindByIdAsync("data-source-1"))
            .ReturnsAsync(new AIDataSource
            {
                ItemId = "data-source-1",
                AIKnowledgeBaseIndexProfileName = "kb-index",
            });

        var indexProfileStore = new Mock<ISearchIndexProfileStore>();
        indexProfileStore.Setup(store => store.FindByNameAsync("kb-index"))
            .ReturnsAsync(new SearchIndexProfile
            {
                Name = "kb-index",
                ProviderName = "test-provider",
                EmbeddingDeploymentName = "embedding",
            });

        var deploymentManager = new Mock<IAIDeploymentManager>();
        deploymentManager.Setup(manager => manager.FindByNameAsync("embedding"))
            .ReturnsAsync(new AIDeployment
            {
                ItemId = "embedding-id",
                Name = "embedding",
                ModelName = "embedding",
                ClientName = "OpenAI",
                ConnectionName = "Default",
                Purpose = AIDeploymentPurpose.Embedding,
            });

        var textNormalizer = new Mock<IAITextNormalizer>();
        textNormalizer.Setup(normalizer => normalizer.NormalizeTitle(It.IsAny<string>()))
            .Returns<string>(value => value);

        var services = new ServiceCollection()
            .AddSingleton<IAIDataSourceStore>(dataSourceStore.Object)
            .AddSingleton<ISearchIndexProfileStore>(indexProfileStore.Object)
            .AddSingleton<IAIDeploymentManager>(deploymentManager.Object)
            .AddSingleton<IAIClientFactory>(new FakeAIClientFactory(new FakeEmbeddingGenerator(new Dictionary<string, float[]>
            {
                ["What year did the theater start?"] = [1f],
                ["theater founding year"] = [2f],
            })))
            .AddSingleton<ITemplateService, FakeTemplateService>()
            .AddSingleton<IAITextNormalizer>(textNormalizer.Object)
            .AddSingleton<IOptionsMonitor<AIDataSourceOptions>>(new TestOptionsMonitor<AIDataSourceOptions>
            {
                CurrentValue = new AIDataSourceOptions
                {
                    DefaultStrictness = 3,
                    DefaultTopNDocuments = 3,
                },
            })
            .AddLogging()
            .AddKeyedSingleton<IDataSourceContentManager>("test-provider", new FakeDataSourceContentManager())
            .BuildServiceProvider();

        var handler = new DataSourcePreemptiveRagHandler(
            services,
            services.GetRequiredService<IAIClientFactory>(),
            services.GetRequiredService<ITemplateService>(),
            services.GetRequiredService<IAIDeploymentManager>(),
            services.GetRequiredService<IAITextNormalizer>(),
            services.GetRequiredService<IOptionsMonitor<AIDataSourceOptions>>(),
            NullLogger<DataSourcePreemptiveRagHandler>.Instance);

        var profile = new AIProfile
        {
            ItemId = "profile-1",
        };
        profile.Put(new AIDataSourceRagMetadata
        {
            IsInScope = true,
        });

        var context = new OrchestrationContext
        {
            UserMessage = "What year did the theater start?",
            CompletionContext = new AICompletionContext
            {
                DataSourceId = "data-source-1",
            },
        };

        await handler.HandleAsync(new PreemptiveRagContext(context, profile, ["theater founding year"]));

        var systemMessage = context.SystemMessageBuilder.ToString();

        Assert.Contains("[Retrieved Data Source Context]", systemMessage);
        Assert.Contains("Since 1941, the 20th Century Theater has been", systemMessage);
        Assert.DoesNotContain("Directions and parking information.", systemMessage);
        Assert.True(context.Properties.ContainsKey("DataSourceReferences"));
    }

    private sealed class FakeAIClientFactory : IAIClientFactory
    {
        private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

        public FakeAIClientFactory(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
        {
            _embeddingGenerator = embeddingGenerator;
        }

        public ValueTask<IChatClient> CreateChatClientAsync(AIDeployment deployment)
        {
            return new((IChatClient)null);
        }

        public ValueTask<IChatClient> CreateChatClientAsync(AIDeployment deployment, Action<ChatClientBuilder> configurePipeline)
        {
            return CreateChatClientAsync(deployment);
        }

        public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(AIDeployment deployment)
        {
            return new(_embeddingGenerator);
        }

        public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(AIDeployment deployment, Action<EmbeddingGeneratorBuilder<string, Embedding<float>>> configurePipeline)
        {
            return CreateEmbeddingGeneratorAsync(deployment);
        }

        public ValueTask<IImageGenerator> CreateImageGeneratorAsync(AIDeployment deployment)
        {
            return new((IImageGenerator)null);
        }

        public ValueTask<IImageGenerator> CreateImageGeneratorAsync(AIDeployment deployment, Action<ImageGeneratorBuilder> configurePipeline)
        {
            return CreateImageGeneratorAsync(deployment);
        }

        public ValueTask<ISpeechToTextClient> CreateSpeechToTextClientAsync(AIDeployment deployment)
        {
            return new((ISpeechToTextClient)null);
        }

        public ValueTask<ISpeechToTextClient> CreateSpeechToTextClientAsync(AIDeployment deployment, Action<SpeechToTextClientBuilder> configurePipeline)
        {
            return CreateSpeechToTextClientAsync(deployment);
        }

        public ValueTask<ITextToSpeechClient> CreateTextToSpeechClientAsync(AIDeployment deployment)
        {
            return new((ITextToSpeechClient)null);
        }

        public ValueTask<ITextToSpeechClient> CreateTextToSpeechClientAsync(AIDeployment deployment, Action<TextToSpeechClientBuilder> configurePipeline)
        {
            return CreateTextToSpeechClientAsync(deployment);
        }

#pragma warning disable MEAI001
        public ValueTask<Microsoft.Extensions.AI.IRealtimeClient> CreateRealtimeClientAsync(AIDeployment deployment)
        {
            return new((Microsoft.Extensions.AI.IRealtimeClient)null);
        }
#pragma warning restore MEAI001
    }

    private sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly IReadOnlyDictionary<string, float[]> _vectors;

        public FakeEmbeddingGenerator(IReadOnlyDictionary<string, float[]> vectors)
        {
            _vectors = vectors;
        }

        public EmbeddingGeneratorMetadata Metadata { get; } = new("fake");

        object IEmbeddingGenerator.GetService(Type serviceType, object serviceKey)
        {
            return null;
        }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions options = null, CancellationToken cancellationToken = default)
        {
            var embeddings = new GeneratedEmbeddings<Embedding<float>>();

            foreach (var value in values)
            {
                embeddings.Add(new Embedding<float>(_vectors[value]));
            }

            return Task.FromResult(embeddings);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeDataSourceContentManager : IDataSourceContentManager
    {
        public Task<long> DeleteByDataSourceIdAsync(IIndexProfileInfo indexProfile, string dataSourceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }

        public Task<IEnumerable<DataSourceSearchResult>> SearchAsync(IIndexProfileInfo indexProfile, float[] embedding, string dataSourceId, int topN, string filter = null, CancellationToken cancellationToken = default)
        {
            IEnumerable<DataSourceSearchResult> results = embedding[0] switch
            {
                1f =>
                [
                    new()
                    {
                        ReferenceId = "about",
                        ReferenceType = "Web",
                        ChunkIndex = 0,
                        Title = "About Us",
                        Content = "Since 1941, the 20th Century Theater has been part of Cincinnati's story.",
                        Score = 0.45f,
                    },
                    new()
                    {
                        ReferenceId = "history",
                        ReferenceType = "Web",
                        ChunkIndex = 0,
                        Title = "History",
                        Content = "The restored Art Deco theater has hosted events across generations.",
                        Score = 0.44f,
                    },
                    new()
                    {
                        ReferenceId = "venue",
                        ReferenceType = "Web",
                        ChunkIndex = 0,
                        Title = "Venue",
                        Content = "The theater remains a landmark wedding and live-events venue.",
                        Score = 0.43f,
                    },
                ],
                2f =>
                [
                    new()
                    {
                        ReferenceId = "parking",
                        ReferenceType = "Web",
                        ChunkIndex = 0,
                        Title = "Plan Your Visit",
                        Content = "Directions and parking information.",
                        Score = 0.95f,
                    },
                    new()
                    {
                        ReferenceId = "contact",
                        ReferenceType = "Web",
                        ChunkIndex = 0,
                        Title = "Contact",
                        Content = "Call the venue team during business hours.",
                        Score = 0.94f,
                    },
                    new()
                    {
                        ReferenceId = "gallery",
                        ReferenceType = "Web",
                        ChunkIndex = 0,
                        Title = "Gallery",
                        Content = "Photo gallery and event inspiration.",
                        Score = 0.93f,
                    },
                ],
                _ => [],
            };

            return Task.FromResult(results);
        }
    }

    private sealed class FakeTemplateService : ITemplateService
    {
        public Task<Template> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Template>(null);
        }

        public Task<IReadOnlyList<Template>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Template>>([]);
        }

        public Task<string> MergeAsync(IEnumerable<string> ids, IDictionary<string, object> arguments = null, string separator = "\n\n", CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Join(separator, ids));
        }

        public Task<string> RenderAsync(string id, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(id == AITemplateIds.DataSourceContextHeader ? "[Retrieved Data Source Context]" : string.Empty);
        }
    }
}
