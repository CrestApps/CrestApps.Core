using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
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

    /// <summary>
    /// Verifies that a data source attached directly to a chat interaction can still show a picture.
    /// </summary>
    /// <remarks>
    /// Preemptive RAG is a second retrieval path beside the search tool, and for a long time only the tool
    /// path named figures. A data source attached through the Knowledge panel therefore retrieved a figure's
    /// text and then had no way to show it, because nothing handed the model a label to write. This asserts
    /// the label reaches the system message and the image reference is registered under it.
    /// </remarks>
    [Fact]
    public async Task HandleAsync_WhenAFigureIsRetrieved_NamesItsLabelAndRegistersTheImage()
    {
        var dataSourceStore = new Mock<IAIDataSourceStore>();
        dataSourceStore.Setup(store => store.FindByIdAsync("data-source-1"))
            .ReturnsAsync(new AIDataSource
            {
                ItemId = "data-source-1",
                Source = AIDataSourceSourceTypes.File,
                AIKnowledgeBaseIndexProfileName = "kb-index",
            });

        var indexProfileStore = new Mock<ISearchIndexProfileStore>();
        indexProfileStore.Setup(store => store.FindByNameAsync("kb-index"))
            .ReturnsAsync(new SearchIndexProfile
            {
                Name = "kb-index",
                ProviderName = "figure-provider",
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
            });

        var textNormalizer = new Mock<IAITextNormalizer>();
        textNormalizer.Setup(normalizer => normalizer.NormalizeTitle(It.IsAny<string>()))
            .Returns<string>(value => value);

        var linkResolver = new Mock<IAIReferenceLinkResolver>();
        linkResolver
            .Setup(instance => instance.ResolveLink("figure:abc:1:0", It.IsAny<IDictionary<string, object>>()))
            .Returns("https://localhost/figures/figure-1");

        var services = new ServiceCollection()
            .AddSingleton<IAIDataSourceStore>(dataSourceStore.Object)
            .AddSingleton<ISearchIndexProfileStore>(indexProfileStore.Object)
            .AddSingleton<IAIDeploymentManager>(deploymentManager.Object)
            .AddSingleton<IAIClientFactory>(new FakeAIClientFactory(new FakeEmbeddingGenerator(new Dictionary<string, float[]>
            {
                ["Show me a picture."] = [3f],
            })))
            .AddSingleton<ITemplateService, FakeTemplateService>()
            .AddSingleton<IAITextNormalizer>(textNormalizer.Object)
            .AddSingleton<IOptionsMonitor<AIDataSourceOptions>>(new TestOptionsMonitor<AIDataSourceOptions>
            {
                CurrentValue = new AIDataSourceOptions
                {
                    DefaultStrictness = 1,
                    DefaultTopNDocuments = 3,
                },
            })
            .AddLogging()
            .AddKeyedSingleton<IDataSourceContentManager>("figure-provider", new FakeFigureContentManager())
            .AddKeyedSingleton<IODataFilterTranslator>("figure-provider", new PassThroughFilterTranslator())
            .AddKeyedSingleton(AIDataSourceSourceTypes.File, linkResolver.Object)
            .BuildServiceProvider();

        var handler = new DataSourcePreemptiveRagHandler(
            services,
            services.GetRequiredService<IAIClientFactory>(),
            services.GetRequiredService<ITemplateService>(),
            services.GetRequiredService<IAIDeploymentManager>(),
            services.GetRequiredService<IAITextNormalizer>(),
            services.GetRequiredService<IOptionsMonitor<AIDataSourceOptions>>(),
            NullLogger<DataSourcePreemptiveRagHandler>.Instance);

        var profile = new AIProfile { ItemId = "profile-1", };
        var context = new OrchestrationContext
        {
            UserMessage = "Show me a picture.",
            CompletionContext = new AICompletionContext { DataSourceId = "data-source-1", },
        };

        using var scope = AIInvocationScope.Begin();

        await handler.HandleAsync(new PreemptiveRagContext(context, profile, []));

        var systemMessage = context.SystemMessageBuilder.ToString();
        var invocationContext = AIInvocationScope.Current;

        // The prose the plain search found is still there; the picture is added beside it, not instead of it.
        Assert.Contains("The measurements are discussed at length.", systemMessage, StringComparison.Ordinal);

        // The label is named, and no address is handed to the model to copy.
        Assert.Contains("[fig:1]", systemMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("https://localhost/figures/figure-1", systemMessage, StringComparison.Ordinal);

        // The image is registered under exactly the label that was rendered, so the host can expand it.
        var image = Assert.Single(invocationContext.ToolReferences, pair => pair.Value.IsImage);
        Assert.Contains(image.Key, systemMessage, StringComparison.Ordinal);
        Assert.Equal("https://localhost/figures/figure-1", image.Value.Link);
    }

    /// <summary>
    /// Verifies that a knowledge base which could not be searched is reported to the model as unreachable,
    /// while a knowledge base that was searched and matched nothing still says nothing at all.
    /// </summary>
    /// <remarks>
    /// The preemptive path injects what it retrieved into the system message and returns silently when it
    /// retrieved nothing, so a failed search and an empty one both left the model with no context — and a
    /// model with no context answers from its own knowledge as though it had checked the data source and
    /// found it wanting.
    /// </remarks>
    [Fact]
    public async Task HandleAsync_WhenTheSearchFails_TellsTheModelTheKnowledgeBaseCouldNotBeSearched()
    {
        var unreachable = await InjectAsync(new UnreachableContentManager());
        var searchable = await InjectAsync(new EmptyContentManager());

        var failedMessage = unreachable.SystemMessageBuilder.ToString();
        var emptyMessage = searchable.SystemMessageBuilder.ToString();

        // A search that failed and a search that matched nothing must not leave the model in the same place.
        Assert.NotEqual(emptyMessage, failedMessage);
        Assert.Contains("could not be searched", failedMessage, StringComparison.Ordinal);

        // An index that was reached and matched nothing adds nothing, exactly as it always has.
        Assert.Equal(string.Empty, emptyMessage);

        // Recorded for the handlers that finish building the system message, which otherwise read "no
        // references" as "the knowledge sources hold nothing".
        Assert.True(unreachable.Properties.ContainsKey("DataSourceSearchFailed"));
        Assert.False(searchable.Properties.ContainsKey("DataSourceSearchFailed"));
    }

    /// <summary>
    /// Runs the preemptive handler against one content manager and returns the context it built.
    /// </summary>
    /// <param name="contentManager">The provider's vector search service.</param>
    private static async Task<OrchestrationContext> InjectAsync(IDataSourceContentManager contentManager)
    {
        const string userMessage = "Does the handbook cover the calibration steps?";

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
            });

        var textNormalizer = new Mock<IAITextNormalizer>();
        textNormalizer.Setup(normalizer => normalizer.NormalizeTitle(It.IsAny<string>()))
            .Returns<string>(value => value);

        await using var services = new ServiceCollection()
            .AddSingleton<IAIDataSourceStore>(dataSourceStore.Object)
            .AddSingleton<ISearchIndexProfileStore>(indexProfileStore.Object)
            .AddSingleton<IAIDeploymentManager>(deploymentManager.Object)
            .AddSingleton<IAIClientFactory>(new FakeAIClientFactory(new FakeEmbeddingGenerator(new Dictionary<string, float[]>
            {
                [userMessage] = [1f],
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
            .AddKeyedSingleton<IDataSourceContentManager>("test-provider", contentManager)
            .BuildServiceProvider();

        var handler = new DataSourcePreemptiveRagHandler(
            services,
            services.GetRequiredService<IAIClientFactory>(),
            services.GetRequiredService<ITemplateService>(),
            services.GetRequiredService<IAIDeploymentManager>(),
            services.GetRequiredService<IAITextNormalizer>(),
            services.GetRequiredService<IOptionsMonitor<AIDataSourceOptions>>(),
            NullLogger<DataSourcePreemptiveRagHandler>.Instance);

        var context = new OrchestrationContext
        {
            UserMessage = userMessage,
            CompletionContext = new AICompletionContext
            {
                DataSourceId = "data-source-1",
            },
        };

        await handler.HandleAsync(new PreemptiveRagContext(context, new AIProfile { ItemId = "profile-1", }, []));

        return context;
    }

    /// <summary>
    /// Stands in for a provider whose index cannot be reached — a stopped database, a refused credential.
    /// </summary>
    private sealed class UnreachableContentManager : IDataSourceContentManager
    {
        public Task<long> DeleteByDataSourceIdAsync(IIndexProfileInfo indexProfile, string dataSourceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }

        public Task<DataSourceSearchOutcome> TrySearchAsync(IIndexProfileInfo indexProfile, float[] embedding, string dataSourceId, int topN, string filter = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DataSourceSearchOutcome.Failure());
        }

        public Task<IEnumerable<DataSourceSearchResult>> SearchAsync(IIndexProfileInfo indexProfile, float[] embedding, string dataSourceId, int topN, string filter = null, CancellationToken cancellationToken = default)
        {
            // All the older contract can say about a dead index, and the whole problem with it: it reads as
            // an index with nothing in it.
            return Task.FromResult(Enumerable.Empty<DataSourceSearchResult>());
        }
    }

    /// <summary>
    /// Stands in for an index that was searched and simply matched nothing. It does not implement the outcome
    /// contract, so it also covers a provider that has not adopted it.
    /// </summary>
    private sealed class EmptyContentManager : IDataSourceContentManager
    {
        public Task<long> DeleteByDataSourceIdAsync(IIndexProfileInfo indexProfile, string dataSourceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }

        public Task<IEnumerable<DataSourceSearchResult>> SearchAsync(IIndexProfileInfo indexProfile, float[] embedding, string dataSourceId, int topN, string filter = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Enumerable.Empty<DataSourceSearchResult>());
        }
    }

    /// <summary>
    /// Stands in for a provider's filter translator. A real one rewrites the clause into the provider's own
    /// dialect; what matters here is only that a clause survives the trip, because a provider with no
    /// translator is never asked for pictures at all.
    /// </summary>
    /// <summary>
    /// Verifies that a profile restricted to text is not handed pictures anyway.
    /// </summary>
    /// <remarks>
    /// Pictures are fetched by a second search of their own, because a plain search ranks the prose about a
    /// subject above the pictures of it. That second search never saw the operator's restriction, so a
    /// restriction applied only to the first would be undone by the pass that runs after it -- the narrowing
    /// would appear to work everywhere except where it matters.
    /// </remarks>
    [Fact]
    public async Task HandleAsync_WhenTheProfileIsNarrowedToText_AddsNoPictures()
    {
        var dataSourceStore = new Mock<IAIDataSourceStore>();
        dataSourceStore.Setup(store => store.FindByIdAsync("data-source-1"))
            .ReturnsAsync(new AIDataSource
            {
                ItemId = "data-source-1",
                Source = AIDataSourceSourceTypes.File,
                AIKnowledgeBaseIndexProfileName = "kb-index",
            });

        var indexProfileStore = new Mock<ISearchIndexProfileStore>();
        indexProfileStore.Setup(store => store.FindByNameAsync("kb-index"))
            .ReturnsAsync(new SearchIndexProfile
            {
                Name = "kb-index",
                ProviderName = "narrowing-provider",
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
            });

        var textNormalizer = new Mock<IAITextNormalizer>();
        textNormalizer.Setup(normalizer => normalizer.NormalizeTitle(It.IsAny<string>()))
            .Returns<string>(value => value);

        var linkResolver = new Mock<IAIReferenceLinkResolver>();
        linkResolver
            .Setup(instance => instance.ResolveLink("figure:abc:1:0", It.IsAny<IDictionary<string, object>>()))
            .Returns("https://localhost/figures/figure-1");

        var services = new ServiceCollection()
            .AddSingleton<IAIDataSourceStore>(dataSourceStore.Object)
            .AddSingleton<ISearchIndexProfileStore>(indexProfileStore.Object)
            .AddSingleton<IAIDeploymentManager>(deploymentManager.Object)
            .AddSingleton<IAIClientFactory>(new FakeAIClientFactory(new FakeEmbeddingGenerator(new Dictionary<string, float[]>
            {
                ["Show me a picture."] = [3f],
            })))
            .AddSingleton<ITemplateService, FakeTemplateService>()
            .AddSingleton<IAITextNormalizer>(textNormalizer.Object)
            .AddSingleton<IOptionsMonitor<AIDataSourceOptions>>(new TestOptionsMonitor<AIDataSourceOptions>
            {
                CurrentValue = new AIDataSourceOptions
                {
                    DefaultStrictness = 1,
                    DefaultTopNDocuments = 3,
                },
            })
            .AddLogging()
            .AddKeyedSingleton<IDataSourceContentManager>("narrowing-provider", new FilterHonouringContentManager())
            .AddKeyedSingleton<IODataFilterTranslator>("narrowing-provider", new PassThroughFilterTranslator())
            .AddKeyedSingleton(AIDataSourceSourceTypes.File, linkResolver.Object)
            .BuildServiceProvider();

        var handler = new DataSourcePreemptiveRagHandler(
            services,
            services.GetRequiredService<IAIClientFactory>(),
            services.GetRequiredService<ITemplateService>(),
            services.GetRequiredService<IAIDeploymentManager>(),
            services.GetRequiredService<IAITextNormalizer>(),
            services.GetRequiredService<IOptionsMonitor<AIDataSourceOptions>>(),
            NullLogger<DataSourcePreemptiveRagHandler>.Instance);

        var profile = new AIProfile { ItemId = "profile-1", };
        profile.Put(new AIDataSourceRagMetadata
        {
            ObjectTypes = [KnowledgeObjectTypes.Text],
        });

        var context = new OrchestrationContext
        {
            UserMessage = "Show me a picture.",
            CompletionContext = new AICompletionContext { DataSourceId = "data-source-1", },
        };

        using var scope = AIInvocationScope.Begin();

        await handler.HandleAsync(new PreemptiveRagContext(context, profile, []));

        var systemMessage = context.SystemMessageBuilder.ToString();
        var invocationContext = AIInvocationScope.Current;

        // The text the restriction does admit is still retrieved and injected.
        Assert.Contains("The measurements are discussed at length.", systemMessage, StringComparison.Ordinal);

        // The picture is not, and no image is registered for the host to expand.
        Assert.DoesNotContain("[fig:", systemMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("A measured plot", systemMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(invocationContext.ToolReferences, pair => pair.Value.IsImage);
    }

    private sealed class PassThroughFilterTranslator : IODataFilterTranslator
    {
        public string Translate(string odataFilter)
        {
            return odataFilter;
        }
    }

    /// <summary>
    /// Returns whatever the filter it was handed actually asks for, so a test can tell a search that was
    /// narrowed from one that only looked as though it was.
    /// </summary>
    private sealed class FilterHonouringContentManager : IDataSourceContentManager
    {
        public Task<long> DeleteByDataSourceIdAsync(IIndexProfileInfo indexProfile, string dataSourceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }

        public Task<IEnumerable<DataSourceSearchResult>> SearchAsync(IIndexProfileInfo indexProfile, float[] embedding, string dataSourceId, int topN, string filter = null, CancellationToken cancellationToken = default)
        {
            var wantsFigure = filter?.Contains($"'{KnowledgeObjectTypes.Figure}'", StringComparison.Ordinal) == true;
            var wantsText = string.IsNullOrEmpty(filter) || filter.Contains($"'{KnowledgeObjectTypes.Text}'", StringComparison.Ordinal);

            var results = new List<DataSourceSearchResult>();

            if (wantsText)
            {
                results.Add(new DataSourceSearchResult
                {
                    ReferenceId = "article:abc:1",
                    ReferenceType = AIDataSourceSourceTypes.File,
                    ContentType = KnowledgeObjectTypes.Text,
                    ChunkIndex = 0,
                    Title = "The measurements, written up.",
                    Content = "The measurements are discussed at length.",
                    Page = 8,
                    Score = 0.9f,
                });
            }

            if (wantsFigure)
            {
                results.Add(new DataSourceSearchResult
                {
                    ReferenceId = "figure:abc:1:0",
                    ReferenceType = AIDataSourceSourceTypes.File,
                    ContentType = KnowledgeObjectTypes.Figure,
                    ChunkIndex = 0,
                    Title = "A measured plot.",
                    Content = "A measured plot, described.",
                    Page = 8,
                    Score = 0.4f,
                });
            }

            return Task.FromResult<IEnumerable<DataSourceSearchResult>>(results);
        }
    }

    private sealed class FakeFigureContentManager : IDataSourceContentManager
    {
        public Task<long> DeleteByDataSourceIdAsync(IIndexProfileInfo indexProfile, string dataSourceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }

        public Task<IEnumerable<DataSourceSearchResult>> SearchAsync(IIndexProfileInfo indexProfile, float[] embedding, string dataSourceId, int topN, string filter = null, CancellationToken cancellationToken = default)
        {
            // Modelled on what a real corpus does, because it is the whole reason the separate pass exists:
            // a question about a subject ranks the prose about it above the pictures of it, so an unfiltered
            // search returns no picture at all. Only the search restricted to pictures finds one.
            IEnumerable<DataSourceSearchResult> results = string.IsNullOrEmpty(filter)
                ?
                [
                    new()
                    {
                        ReferenceId = "article:abc:1",
                        ReferenceType = AIDataSourceSourceTypes.File,
                        ContentType = KnowledgeObjectTypes.Text,
                        ChunkIndex = 0,
                        Title = "The measurements, written up.",
                        Content = "The measurements are discussed at length.",
                        Page = 8,
                        Score = 0.9f,
                    },
                ]
                :
                [
                    new()
                    {
                        ReferenceId = "figure:abc:1:0",
                        ReferenceType = AIDataSourceSourceTypes.File,
                        ContentType = KnowledgeObjectTypes.Figure,
                        ChunkIndex = 0,
                        Title = "A measured plot.",
                        Content = "A measured plot, described.",
                        Page = 8,
                        Score = 0.4f,
                    },
                ];

            return Task.FromResult(results);
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
            // A real provider honours the filter. These rows are all prose, so the separate search for
            // pictures finds none -- which is what lets this fixture prove the prose ranking on its own.
            if (!string.IsNullOrEmpty(filter))
            {
                return Task.FromResult(Enumerable.Empty<DataSourceSearchResult>());
            }

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
