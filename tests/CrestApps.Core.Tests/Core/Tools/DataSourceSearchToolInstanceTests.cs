using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.Tooling.Instances.DataSources;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Tools;

public sealed class DataSourceSearchToolInstanceTests
{
    private const string DataSourceId = "data-source-1";
    private const string ProviderName = "TestProvider";
    private const string KnowledgeBaseName = "kb-index";
    private const string EmbeddingDeploymentName = "text-embedding-3-small";

    /// <summary>
    /// Verifies that the source materializes a search function whose model-facing name and description are
    /// derived from the configured instance.
    /// </summary>
    [Fact]
    public void CreateTool_DerivesNameAndDescriptionFromInstance()
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-1",
            Source = DataSourceSearchToolConstants.SourceName,
            Name = "policy-handbook",
            Description = "Searches the employee policy handbook.",
        };

        instance.Put(new DataSourceSearchToolSettings
        {
            DataSourceId = DataSourceId,
        });

        var tool = new DataSourceSearchToolInstanceSource().CreateTool(instance);

        var function = Assert.IsType<DataSourceSearchToolFunction>(tool);

        Assert.Equal(instance.GetFunctionName(), function.Name);
        Assert.Equal("Searches the employee policy handbook.", function.Description);
    }

    /// <summary>
    /// Verifies that an instance without a description still exposes a usable description to the model.
    /// </summary>
    [Fact]
    public void CreateTool_FallsBackToDefaultDescription_WhenInstanceHasNone()
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-2",
            Source = DataSourceSearchToolConstants.SourceName,
            Name = "policy-handbook",
        };

        var function = Assert.IsType<DataSourceSearchToolFunction>(new DataSourceSearchToolInstanceSource().CreateTool(instance));

        Assert.False(string.IsNullOrWhiteSpace(function.Description));
        Assert.NotEqual(function.Name, function.Description);
    }

    /// <summary>
    /// Verifies that an instance saved without a data source reports the misconfiguration instead of
    /// searching an unrelated knowledge base.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ReportsMisconfiguration_WhenNoDataSourceIsSelected()
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-3",
            Source = DataSourceSearchToolConstants.SourceName,
            Name = "unconfigured",
        };

        var function = (DataSourceSearchToolFunction)new DataSourceSearchToolInstanceSource().CreateTool(instance);

        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object>
        {
            ["query"] = "vacation policy",
        })
        {
            Services = new ServiceCollection().BuildServiceProvider(),
        },
        cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("No data source is configured", result?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that the query is embedded with the knowledge base index profile's own embedding deployment
    /// — the same one the chunks were indexed with — and that the configured retrieval parameters reach the
    /// vector search.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_EmbedsQueryWithIndexProfileDeployment_AndAppliesConfiguredParameters()
    {
        var contentManager = new RecordingContentManager(
        [
            new DataSourceSearchResult
            {
                ReferenceId = "doc-1",
                Title = "Vacation policy",
                Content = "Employees accrue 15 days per year.",
                ChunkIndex = 0,
                ReferenceType = "Content",
                Score = 0.9f,
            },
        ]);

        var embeddingGenerator = new RecordingEmbeddingGenerator();
        var services = BuildServices(contentManager, embeddingGenerator);

        var function = CreateFunction(new DataSourceSearchToolSettings
        {
            DataSourceId = DataSourceId,
            TopNDocuments = 7,
            Strictness = 1,
            Filter = "status eq 'published'",
        });

        var result = await InvokeAsync(function, services, "vacation policy");

        Assert.Equal(EmbeddingDeploymentName, embeddingGenerator.RequestedDeploymentName);
        Assert.Equal(["vacation policy"], embeddingGenerator.ReceivedInputs);

        Assert.Equal(DataSourceId, contentManager.ReceivedDataSourceId);
        Assert.Equal("translated:status eq 'published'", contentManager.ReceivedFilter);
        Assert.Equal(DataSourceSearchResultSelector.GetCandidateCount(7), contentManager.ReceivedTopN);

        Assert.Contains("Employees accrue 15 days per year.", result);
        Assert.Contains("Vacation policy", result);
        Assert.Contains("[doc:1] = doc-1", result);
    }

    /// <summary>
    /// Verifies that chunk retrieval — the default — returns the matching chunks rather than reading the
    /// full source documents back.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ChunkMode_ReturnsMatchingChunksOnly()
    {
        var contentManager = new RecordingContentManager(
        [
            new DataSourceSearchResult
            {
                ReferenceId = "doc-1",
                Title = "Vacation policy",
                Content = "Employees accrue 15 days per year.",
                ChunkIndex = 1,
                Score = 0.9f,
            },
        ]);

        var sourceHandler = new RecordingSourceHandler(new Dictionary<string, SourceDocument>
        {
            ["doc-1"] = new SourceDocument
            {
                Title = "Vacation policy",
                Content = "The full vacation policy, start to finish.",
            },
        });

        var services = BuildServices(contentManager, new RecordingEmbeddingGenerator(), sourceHandler);

        var function = CreateFunction(new DataSourceSearchToolSettings
        {
            DataSourceId = DataSourceId,
            RetrievalMode = DataSourceRetrievalMode.Chunk,
        });

        var result = await InvokeAsync(function, services, "vacation policy");

        Assert.Contains("Employees accrue 15 days per year.", result);
        Assert.DoesNotContain("The full vacation policy, start to finish.", result);
        Assert.False(sourceHandler.WasRead);
    }

    /// <summary>
    /// Verifies that hierarchical retrieval collapses the matching chunks onto their source document and
    /// returns that document's complete text under a single citation.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_HierarchicalMode_ReturnsFullSourceDocumentOnce()
    {
        var contentManager = new RecordingContentManager(
        [
            new DataSourceSearchResult
            {
                ReferenceId = "doc-1",
                Title = "Vacation policy",
                Content = "Employees accrue 15 days per year.",
                ChunkIndex = 0,
                Score = 0.9f,
            },
            new DataSourceSearchResult
            {
                ReferenceId = "doc-1",
                Title = "Vacation policy",
                Content = "Unused days roll over once.",
                ChunkIndex = 1,
                Score = 0.8f,
            },
        ]);

        var sourceHandler = new RecordingSourceHandler(new Dictionary<string, SourceDocument>
        {
            ["doc-1"] = new SourceDocument
            {
                Title = "Vacation policy",
                Content = "The full vacation policy, start to finish.",
            },
        });

        var services = BuildServices(contentManager, new RecordingEmbeddingGenerator(), sourceHandler);

        var function = CreateFunction(new DataSourceSearchToolSettings
        {
            DataSourceId = DataSourceId,
            RetrievalMode = DataSourceRetrievalMode.Hierarchical,
        });

        var result = await InvokeAsync(function, services, "vacation policy");

        Assert.Equal(["doc-1"], sourceHandler.ReceivedDocumentIds);
        Assert.Contains("The full vacation policy, start to finish.", result);
        Assert.DoesNotContain("Unused days roll over once.", result);

        // The two chunks belong to one document, so the document is rendered once under one citation.
        Assert.Equal(1, CountOccurrences(result, "The full vacation policy, start to finish."));
        Assert.Equal(1, CountOccurrences(result, "[doc:1] Title:"));
        Assert.DoesNotContain("[doc:2]", result);
    }

    /// <summary>
    /// Verifies that hierarchical retrieval degrades to the matching chunks when the source documents can no
    /// longer be read, rather than returning nothing at all.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_HierarchicalMode_FallsBackToChunks_WhenSourceCannotBeRead()
    {
        var contentManager = new RecordingContentManager(
        [
            new DataSourceSearchResult
            {
                ReferenceId = "doc-1",
                Title = "Vacation policy",
                Content = "Employees accrue 15 days per year.",
                ChunkIndex = 0,
                Score = 0.9f,
            },
        ]);

        var sourceHandler = new RecordingSourceHandler(new Dictionary<string, SourceDocument>(), throwOnRead: true);
        var services = BuildServices(contentManager, new RecordingEmbeddingGenerator(), sourceHandler);

        var function = CreateFunction(new DataSourceSearchToolSettings
        {
            DataSourceId = DataSourceId,
            RetrievalMode = DataSourceRetrievalMode.Hierarchical,
        });

        var result = await InvokeAsync(function, services, "vacation policy");

        Assert.Contains("Employees accrue 15 days per year.", result);
    }

    /// <summary>
    /// Verifies that a strictness high enough to reject every match tells the model the knowledge base has
    /// no answer instead of returning an empty block of context.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ReportsNoMatches_WhenStrictnessRejectsEveryResult()
    {
        var contentManager = new RecordingContentManager(
        [
            new DataSourceSearchResult
            {
                ReferenceId = "doc-1",
                Content = "Employees accrue 15 days per year.",
                Score = 0.1f,
            },
        ]);

        var services = BuildServices(contentManager, new RecordingEmbeddingGenerator());

        var function = CreateFunction(new DataSourceSearchToolSettings
        {
            DataSourceId = DataSourceId,
            Strictness = AIDataSourceOptions.MaxStrictness,
            IsInScope = true,
        });

        var result = await InvokeAsync(function, services, "vacation policy");

        Assert.Contains("strictness", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not available", result, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOccurrences(string text, string value)
    {
        return text.Split(value).Length - 1;
    }

    private static DataSourceSearchToolFunction CreateFunction(DataSourceSearchToolSettings settings)
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-search",
            Source = DataSourceSearchToolConstants.SourceName,
            Name = "knowledge-base",
            Description = "Searches the knowledge base.",
        };

        instance.Put(settings);

        return (DataSourceSearchToolFunction)new DataSourceSearchToolInstanceSource().CreateTool(instance);
    }

    private static async Task<string> InvokeAsync(DataSourceSearchToolFunction function, IServiceProvider services, string query)
    {
        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object>
        {
            ["query"] = query,
        })
        {
            Services = services,
        },
        cancellationToken: TestContext.Current.CancellationToken);

        return result?.ToString() ?? string.Empty;
    }

    private static ServiceProvider BuildServices(
        RecordingContentManager contentManager,
        RecordingEmbeddingGenerator embeddingGenerator,
        RecordingSourceHandler sourceHandler = null)
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
            .Setup(manager => manager.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string name, CancellationToken _) =>
            {
                embeddingGenerator.RequestedDeploymentName = name;

                return ValueTask.FromResult(string.Equals(name, EmbeddingDeploymentName, StringComparison.Ordinal) ? deployment : null);
            });

        var clientFactory = new Mock<IAIClientFactory>();
        clientFactory
            .Setup(factory => factory.CreateEmbeddingGeneratorAsync(It.IsAny<AIDeployment>()))
            .ReturnsAsync(embeddingGenerator);

        var textNormalizer = new Mock<IAITextNormalizer>();
        textNormalizer
            .Setup(normalizer => normalizer.NormalizeTitle(It.IsAny<string>()))
            .Returns((string title) => title);

        var filterTranslator = new Mock<IODataFilterTranslator>();
        filterTranslator
            .Setup(translator => translator.Translate(It.IsAny<string>()))
            .Returns((string filter) => $"translated:{filter}");

        var services = new ServiceCollection();

        services.AddSingleton(dataSourceStore.Object);
        services.AddSingleton(indexProfileStore.Object);
        services.AddSingleton(deploymentManager.Object);
        services.AddSingleton(clientFactory.Object);
        services.AddSingleton(textNormalizer.Object);
        services.AddKeyedSingleton<IDataSourceContentManager>(ProviderName, contentManager);
        services.AddKeyedSingleton<IODataFilterTranslator>(ProviderName, filterTranslator.Object);
        services.AddSingleton<IOptionsMonitor<AIDataSourceOptions>>(new StaticOptionsMonitor<AIDataSourceOptions>(new AIDataSourceOptions()));
        services.AddLogging();

        if (sourceHandler != null)
        {
            services.AddKeyedSingleton<IAIDataSourceSourceHandler>(AIDataSourceSourceTypes.SearchIndexProfile, sourceHandler);
        }

        return services.BuildServiceProvider();
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value)
        {
            CurrentValue = value;
        }

        public T CurrentValue { get; }

        public T Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<T, string> listener) => null;
    }

    private sealed class RecordingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public string RequestedDeploymentName { get; set; }

        public List<string> ReceivedInputs { get; } = [];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions options = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedInputs.AddRange(values);

            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                ReceivedInputs.Select(_ => new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f })).ToList()));
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingContentManager : IDataSourceContentManager
    {
        private readonly IReadOnlyList<DataSourceSearchResult> _results;

        public RecordingContentManager(IReadOnlyList<DataSourceSearchResult> results)
        {
            _results = results;
        }

        public string ReceivedDataSourceId { get; private set; }

        public string ReceivedFilter { get; private set; }

        public int ReceivedTopN { get; private set; }

        public Task<IEnumerable<DataSourceSearchResult>> SearchAsync(
            IIndexProfileInfo indexProfile,
            float[] embedding,
            string dataSourceId,
            int topN,
            string filter = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedDataSourceId = dataSourceId;
            ReceivedFilter = filter;
            ReceivedTopN = topN;

            return Task.FromResult<IEnumerable<DataSourceSearchResult>>(_results);
        }

        public Task<long> DeleteByDataSourceIdAsync(
            IIndexProfileInfo indexProfile,
            string dataSourceId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }
    }

    private sealed class RecordingSourceHandler : IAIDataSourceSourceHandler
    {
        private readonly IReadOnlyDictionary<string, SourceDocument> _documents;
        private readonly bool _throwOnRead;

        public RecordingSourceHandler(IReadOnlyDictionary<string, SourceDocument> documents, bool throwOnRead = false)
        {
            _documents = documents;
            _throwOnRead = throwOnRead;
        }

        public string SourceType => AIDataSourceSourceTypes.SearchIndexProfile;

        public bool WasRead { get; private set; }

        public List<string> ReceivedDocumentIds { get; } = [];

        public ValueTask ValidateAsync(AIDataSource dataSource, CrestApps.Core.Models.ValidationResultDetails result, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<string> GetReferenceTypeAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(AIDataSourceSourceTypes.SearchIndexProfile);
        }

        public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadAsync(
            AIDataSource dataSource,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;

            foreach (var pair in _documents)
            {
                yield return pair;
            }
        }

        public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadByIdsAsync(
            AIDataSource dataSource,
            IEnumerable<string> documentIds,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;

            WasRead = true;
            ReceivedDocumentIds.AddRange(documentIds);

            if (_throwOnRead)
            {
                throw new InvalidOperationException("The source index is unavailable.");
            }

            foreach (var id in ReceivedDocumentIds)
            {
                if (_documents.TryGetValue(id, out var document))
                {
                    yield return new KeyValuePair<string, SourceDocument>(id, document);
                }
            }
        }
    }
}
