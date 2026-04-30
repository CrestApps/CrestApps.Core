using CrestApps.Core.AI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

#pragma warning disable MEAI001
namespace CrestApps.Core.Tests.Core.Chat;

public sealed class DocumentPreemptiveRagHandlerTests
{
    [Fact]
    public async Task HandleAsync_ProfileKnowledgeDocuments_InjectsRetrievedChunksAndReferences()
    {
        var indexProfile = new SearchIndexProfile
        {
            Name = "docs-index",
            ProviderName = "test-provider",
        };
        indexProfile.Put(new DataSourceIndexProfileMetadata
        {
            EmbeddingDeploymentName = "embedding",
        });
        var indexProfileStore = new Mock<ISearchIndexProfileStore>();
        indexProfileStore.Setup(store => store
            .FindByNameAsync("docs-index"))
            .ReturnsAsync(indexProfile);
        var deploymentManager = new Mock<IAIDeploymentManager>();
        deploymentManager.Setup(manager => manager
            .FindByNameAsync("embedding"))
            .ReturnsAsync(new AIDeployment
            {
                ItemId = "embedding-id",
                Name = "embedding",
                ModelName = "embedding",
                ClientName = "OpenAI",
                ConnectionName = "Default",
                Type = AIDeploymentType.Embedding,
            });
        var vectorSearchService = new Mock<IVectorSearchService>();
        vectorSearchService.Setup(service => service
            .SearchAsync(indexProfile, It.IsAny<float[]>(), "profile-1", AIReferenceTypes.Document.Profile, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DocumentChunkSearchResult
            {
                DocumentKey = "doc-1",
                FileName = "race.pdf",
                Score = 0.95f,
                Chunk = new ChatInteractionDocumentChunk
                {
                    Index = 0,
                    Text = "Carla and Mark race their go carts, and Carla wins the race.",
                },
            },]);
        var services = new ServiceCollection()
            .AddSingleton<IAIClientFactory>(new FakeAIClientFactory(new FakeEmbeddingGenerator([0.1f, 0.2f])))
            .AddSingleton<IAIDeploymentManager>(deploymentManager.Object)
            .AddSingleton<ISearchIndexProfileStore>(indexProfileStore.Object)
            .AddSingleton<ITemplateService, FakeTemplateService>()
            .AddSingleton<IOptions<InteractionDocumentOptions>>(Options.Create(new InteractionDocumentOptions
            {
                IndexProfileName = "docs-index",
                TopN = 3,
            }))
            .AddLogging()
            .AddKeyedSingleton<IVectorSearchService>("test-provider", vectorSearchService.Object)
            .AddCoreAIDocumentProcessing()
            .BuildServiceProvider();
        var handler = services.GetServices<IPreemptiveRagHandler>().Single();
        var profile = new AIProfile
        {
            ItemId = "profile-1"
        };
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext(),
            Documents = [new ChatDocumentInfo
            {
                DocumentId = "doc-1",
                FileName = "race.pdf",
            }, ],
        };
        var builtContext = new OrchestrationContextBuiltContext(profile, context);
        var canHandle = await handler.CanHandleAsync(builtContext);
        Assert.True(canHandle);
        await handler.HandleAsync(new PreemptiveRagContext(context, profile, ["tell me about car race story"]));
        var systemMessage = context.SystemMessageBuilder.ToString();
        Assert.Contains("[Retrieved Document Context]", systemMessage);
        Assert.Contains("Carla and Mark race their go carts", systemMessage);
    }

    [Fact]
    public async Task HandleAsync_NoIndexProfileConfigured_DoesNotModifySystemMessage()
    {
        var services = new ServiceCollection()
            .AddSingleton<IAIClientFactory>(new FakeAIClientFactory(new FakeEmbeddingGenerator([0.1f])))
            .AddSingleton<IAIDeploymentManager>(Mock.Of<IAIDeploymentManager>())
            .AddSingleton<ISearchIndexProfileStore>(Mock.Of<ISearchIndexProfileStore>())
            .AddSingleton<ITemplateService, FakeTemplateService>()
            .AddSingleton<IOptions<InteractionDocumentOptions>>(Options.Create(new InteractionDocumentOptions()))
            .AddLogging()
            .AddCoreAIDocumentProcessing()
            .BuildServiceProvider();
        var handler = services.GetServices<IPreemptiveRagHandler>().Single();
        var profile = new AIProfile
        {
            ItemId = "profile-1"
        };
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext(),
            Documents = [new ChatDocumentInfo
            {
                DocumentId = "doc-1",
                FileName = "race.pdf",
            }, ],
        };
        await handler.HandleAsync(new PreemptiveRagContext(context, profile, ["tell me about car race story"]));
        Assert.Equal(string.Empty, context.SystemMessageBuilder.ToString());
        Assert.False(context.Properties.ContainsKey("DocumentReferences"));
    }

    [Fact]
    public async Task HandleAsync_SummarizeUploadedChatInteractionDocument_InjectsFullDocumentContent()
    {
        var documentStore = new Mock<IAIDocumentStore>();
        documentStore.Setup(store => store.FindByIdAsync("doc-1"))
            .Returns(new ValueTask<AIDocument>(new AIDocument
            {
                ItemId = "doc-1",
                FileName = "race.pdf",
            }));
        var chunkStore = new Mock<IAIDocumentChunkStore>();
        chunkStore.Setup(store => store.GetChunksByAIDocumentIdAsync("doc-1"))
            .ReturnsAsync((IReadOnlyCollection<AIDocumentChunk>)[
                new AIDocumentChunk
                {
                    AIDocumentId = "doc-1",
                    Index = 0,
                    Content = "Carla and Mark race their go carts.",
                },
                new AIDocumentChunk
                {
                    AIDocumentId = "doc-1",
                    Index = 1,
                    Content = "Carla wins the race by one lap.",
                },
            ]);
        var services = new ServiceCollection()
            .AddSingleton<IAIClientFactory>(new FakeAIClientFactory(new FakeEmbeddingGenerator([0.1f])))
            .AddSingleton<IAIDeploymentManager>(Mock.Of<IAIDeploymentManager>())
            .AddSingleton<ISearchIndexProfileStore>(Mock.Of<ISearchIndexProfileStore>())
            .AddSingleton<IAIDocumentStore>(documentStore.Object)
            .AddSingleton<IAIDocumentChunkStore>(chunkStore.Object)
            .AddSingleton<ITemplateService, FakeTemplateService>()
            .AddSingleton<IOptions<InteractionDocumentOptions>>(Options.Create(new InteractionDocumentOptions()))
            .AddLogging()
            .AddCoreAIDocumentProcessing()
            .BuildServiceProvider();
        var handler = services.GetServices<IPreemptiveRagHandler>().Single();
        var interaction = new ChatInteraction
        {
            ItemId = "chat-1",
            Documents =
            [
                new ChatDocumentInfo
                {
                    DocumentId = "doc-1",
                    FileName = "race.pdf",
                },
            ],
        };
        var context = new OrchestrationContext
        {
            UserMessage = "Summarize this document.",
            CompletionContext = new AICompletionContext(),
            Documents =
            [
                new ChatDocumentInfo
                {
                    DocumentId = "doc-1",
                    FileName = "race.pdf",
                },
            ],
        };

        await handler.HandleAsync(new PreemptiveRagContext(context, interaction, ["summarize the uploaded document"]));

        var systemMessage = context.SystemMessageBuilder.ToString();
        Assert.Contains("full text of the user's uploaded documents", systemMessage);
        Assert.Contains("Carla and Mark race their go carts.", systemMessage);
        Assert.Contains("Carla wins the race by one lap.", systemMessage);
        Assert.Contains("[doc:1]", systemMessage);
        Assert.True(context.Properties.ContainsKey("DocumentReferences"));
    }

    [Fact]
    public async Task HandleAsync_SummarizeSessionDocument_InjectsFullSessionDocumentContent()
    {
        var documentStore = new Mock<IAIDocumentStore>();
        documentStore.Setup(store => store.FindByIdAsync("doc-1"))
            .Returns(new ValueTask<AIDocument>(new AIDocument
            {
                ItemId = "doc-1",
                FileName = "race.pdf",
            }));
        var chunkStore = new Mock<IAIDocumentChunkStore>();
        chunkStore.Setup(store => store.GetChunksByAIDocumentIdAsync("doc-1"))
            .ReturnsAsync((IReadOnlyCollection<AIDocumentChunk>)[
                new AIDocumentChunk
                {
                    AIDocumentId = "doc-1",
                    Index = 0,
                    Content = "Carla and Mark race their go carts.",
                },
                new AIDocumentChunk
                {
                    AIDocumentId = "doc-1",
                    Index = 1,
                    Content = "Carla wins the race by one lap.",
                },
            ]);
        var services = new ServiceCollection()
            .AddSingleton<IAIClientFactory>(new FakeAIClientFactory(new FakeEmbeddingGenerator([0.1f])))
            .AddSingleton<IAIDeploymentManager>(Mock.Of<IAIDeploymentManager>())
            .AddSingleton<ISearchIndexProfileStore>(Mock.Of<ISearchIndexProfileStore>())
            .AddSingleton<IAIDocumentStore>(documentStore.Object)
            .AddSingleton<IAIDocumentChunkStore>(chunkStore.Object)
            .AddSingleton<ITemplateService, FakeTemplateService>()
            .AddSingleton<IOptions<InteractionDocumentOptions>>(Options.Create(new InteractionDocumentOptions()))
            .AddLogging()
            .AddCoreAIDocumentProcessing()
            .BuildServiceProvider();
        var handler = services.GetServices<IPreemptiveRagHandler>().Single();
        var profile = new AIProfile
        {
            ItemId = "profile-1",
        };
        var session = new AIChatSession
        {
            SessionId = "session-1",
            Documents =
            [
                new ChatDocumentInfo
                {
                    DocumentId = "doc-1",
                    FileName = "race.pdf",
                },
            ],
        };
        var completionContext = new AICompletionContext();
        completionContext.AdditionalProperties["Session"] = session;
        var context = new OrchestrationContext
        {
            UserMessage = "Please summarize it.",
            CompletionContext = completionContext,
            Documents =
            [
                new ChatDocumentInfo
                {
                    DocumentId = "doc-1",
                    FileName = "race.pdf",
                },
            ],
        };

        await handler.HandleAsync(new PreemptiveRagContext(context, profile, ["summarize the uploaded document"]));

        var systemMessage = context.SystemMessageBuilder.ToString();
        Assert.Contains("full text of the user's uploaded documents", systemMessage);
        Assert.Contains("Carla and Mark race their go carts.", systemMessage);
        Assert.Contains("Carla wins the race by one lap.", systemMessage);
        Assert.Contains("[doc:1]", systemMessage);
        Assert.True(context.Properties.ContainsKey("DocumentReferences"));
    }

    [Fact]
    public async Task HandleAsync_HierarchicalMode_InjectsFullMatchedDocumentText()
    {
        var indexProfile = new SearchIndexProfile
        {
            Name = "docs-index",
            ProviderName = "test-provider",
        };
        indexProfile.Put(new DataSourceIndexProfileMetadata
        {
            EmbeddingDeploymentName = "embedding",
        });
        var indexProfileStore = new Mock<ISearchIndexProfileStore>();
        indexProfileStore.Setup(store => store
            .FindByNameAsync("docs-index"))
            .ReturnsAsync(indexProfile);
        var deploymentManager = new Mock<IAIDeploymentManager>();
        deploymentManager.Setup(manager => manager
            .FindByNameAsync("embedding"))
            .ReturnsAsync(new AIDeployment
            {
                ItemId = "embedding-id",
                Name = "embedding",
                ModelName = "embedding",
                ClientName = "OpenAI",
                ConnectionName = "Default",
                Type = AIDeploymentType.Embedding,
            });
        var vectorSearchService = new Mock<IVectorSearchService>();
        vectorSearchService.Setup(service => service
            .SearchAsync(indexProfile, It.IsAny<float[]>(), "profile-1", AIReferenceTypes.Document.Profile, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DocumentChunkSearchResult
            {
                DocumentKey = "doc-1",
                FileName = "race.pdf",
                Score = 0.95f,
                Chunk = new ChatInteractionDocumentChunk
                {
                    Index = 0,
                    Text = "Carla and Mark race their go carts.",
                },
            },]);
        var documentStore = new Mock<IAIDocumentStore>();
        documentStore.Setup(store => store.FindByIdAsync("doc-1"))
            .Returns(new ValueTask<AIDocument>(new AIDocument
            {
                ItemId = "doc-1",
                FileName = "race.pdf",
            }));
        var chunkStore = new Mock<IAIDocumentChunkStore>();
        chunkStore.Setup(store => store.GetChunksByAIDocumentIdAsync("doc-1"))
            .ReturnsAsync((IReadOnlyCollection<AIDocumentChunk>)[
                new AIDocumentChunk
                {
                    AIDocumentId = "doc-1",
                    Index = 0,
                    Content = "Carla and Mark race their go carts.",
                },
                new AIDocumentChunk
                {
                    AIDocumentId = "doc-1",
                    Index = 1,
                    Content = "Carla wins the race by one lap.",
                },
            ]);
        var services = new ServiceCollection()
            .AddSingleton<IAIClientFactory>(new FakeAIClientFactory(new FakeEmbeddingGenerator([0.1f, 0.2f])))
            .AddSingleton<IAIDeploymentManager>(deploymentManager.Object)
            .AddSingleton<ISearchIndexProfileStore>(indexProfileStore.Object)
            .AddSingleton<IAIDocumentStore>(documentStore.Object)
            .AddSingleton<IAIDocumentChunkStore>(chunkStore.Object)
            .AddSingleton<ITemplateService, FakeTemplateService>()
            .AddSingleton<IOptions<InteractionDocumentOptions>>(Options.Create(new InteractionDocumentOptions
            {
                IndexProfileName = "docs-index",
                TopN = 2,
                RetrievalMode = DocumentRetrievalMode.Hierarchical,
            }))
            .AddLogging()
            .AddKeyedSingleton<IVectorSearchService>("test-provider", vectorSearchService.Object)
            .AddCoreAIDocumentProcessing()
            .BuildServiceProvider();
        var handler = services.GetServices<IPreemptiveRagHandler>().Single();
        var profile = new AIProfile
        {
            ItemId = "profile-1"
        };
        profile.Put(new DocumentsMetadata
        {
            DocumentTopN = 2,
            RetrievalMode = DocumentRetrievalMode.Hierarchical,
        });
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext(),
            Documents = [new ChatDocumentInfo
            {
                DocumentId = "doc-1",
                FileName = "race.pdf",
            }, ],
        };

        await handler.HandleAsync(new PreemptiveRagContext(context, profile, ["tell me about car race story"]));

        documentStore.Verify(store => store.FindByIdAsync("doc-1"), Times.Once);
        chunkStore.Verify(store => store.GetChunksByAIDocumentIdAsync("doc-1"), Times.Once);

        var systemMessage = context.SystemMessageBuilder.ToString();
        Assert.Contains("Carla and Mark race their go carts.", systemMessage);
        Assert.Contains("Carla wins the race by one lap.", systemMessage);
    }

    private sealed class FakeTemplateService : ITemplateService
    {
        public Task<IReadOnlyList<Template>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Template>>([]);
        public Task<Template> GetAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<Template>(null);
        public Task<string> RenderAsync(string id, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
        {
            if (id == AITemplateIds.DocumentContextHeader)
            {
                if (arguments?.TryGetValue("hasFullUserDocumentContext", out var hasFullUserDocumentContext) == true &&
                    hasFullUserDocumentContext is true)
                {
                    return Task.FromResult("[Retrieved Document Context]\nThe following content includes the full text of the user's uploaded documents.");
                }

                return Task.FromResult("[Retrieved Document Context]");
            }

            return Task.FromResult($"[Template: {id}]");
        }

        public Task<string> MergeAsync(IEnumerable<string> ids, IDictionary<string, object> arguments = null, string separator = "\n\n", CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Join(separator, ids));
        }
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

        public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(AIDeployment deployment)
        {
            return new(_embeddingGenerator);
        }

        public ValueTask<IImageGenerator> CreateImageGeneratorAsync(AIDeployment deployment)
        {
            return new((IImageGenerator)null);
        }

        public ValueTask<ISpeechToTextClient> CreateSpeechToTextClientAsync(AIDeployment deployment)
        {
            return new((ISpeechToTextClient)null);
        }

        public ValueTask<ITextToSpeechClient> CreateTextToSpeechClientAsync(AIDeployment deployment)
        {
            return new((ITextToSpeechClient)null);
        }
    }

    private sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly float[] _fixedVector;
        public FakeEmbeddingGenerator(float[] fixedVector)
        {
            _fixedVector = fixedVector;
        }

        public EmbeddingGeneratorMetadata Metadata { get; } = new("fake");

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions options = null, CancellationToken cancellationToken = default)
        {
            var embeddings = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var _ in values)
            {
                embeddings.Add(new Embedding<float>(_fixedVector));
            }

            return Task.FromResult(embeddings);
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;
        public void Dispose()
        {
        }
    }
}
