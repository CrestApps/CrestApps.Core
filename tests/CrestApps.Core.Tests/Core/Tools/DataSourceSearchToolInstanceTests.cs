using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Tooling.Instances.DataSources;
using CrestApps.Core.AI.Tools;
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
            ["queries"] = new[] { "vacation policy" },
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
    /// Verifies that a strictness high enough to reject every match says so, instead of returning an empty
    /// block of context.
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
        });

        var result = await InvokeAsync(function, services, "vacation policy");

        Assert.Contains("strictness", result, StringComparison.OrdinalIgnoreCase);
        AssertStatesTheFactWithoutDirectingTheModel(result);
    }

    /// <summary>
    /// Verifies that a search finding nothing reports exactly that, without telling the model whether it may
    /// answer from general knowledge. A standalone tool cannot hold the model to an answering policy — that
    /// has to reach the system prompt — so it states the fact and leaves the decision where it belongs.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ReportsNoResults_WithoutDirectingHowToAnswer()
    {
        var contentManager = new RecordingContentManager([]);
        var services = BuildServices(contentManager, new RecordingEmbeddingGenerator());

        var function = CreateFunction(new DataSourceSearchToolSettings { DataSourceId = DataSourceId });

        var result = await InvokeAsync(function, services, "vacation policy");

        Assert.Contains("No relevant content was found in the data source", result, StringComparison.OrdinalIgnoreCase);
        AssertStatesTheFactWithoutDirectingTheModel(result);
    }

    /// <summary>
    /// Verifies that the profile-bound tool still states the profile's answering policy on an empty result.
    /// Only that tool can: the same policy reaches the system prompt through the orchestration handlers, so
    /// repeating it in the tool result reinforces something the model is already bound by. A tool instance,
    /// which has no such backing, would only be asserting a constraint it cannot enforce.
    /// </summary>
    /// <param name="isInScope">The profile's in-scope setting.</param>
    /// <param name="expected">The guidance the empty result must carry.</param>
    [Theory]
    [InlineData(true, "not available in the configured data source")]
    [InlineData(false, "Answer using your general knowledge instead")]
    public async Task ProfileBoundTool_StillStatesTheProfilePolicy_OnEmptyResults(bool isInScope, string expected)
    {
        var contentManager = new RecordingContentManager([]);
        var services = BuildServices(contentManager, new RecordingEmbeddingGenerator());

        var profile = new AIProfile
        {
            ItemId = "profile-1",
            Name = "grounded",
            Type = AIProfileType.Chat,
        };

        profile.Put(new AIDataSourceRagMetadata { IsInScope = isInScope });

        using var scope = AIInvocationScope.Begin();

        AIInvocationScope.Current.DataSourceId = DataSourceId;
        AIInvocationScope.Current.ToolExecutionContext = new AIToolExecutionContext(profile);

        var result = await new DataSourceSearchTool().InvokeAsync(new AIFunctionArguments(new Dictionary<string, object>
        {
            ["query"] = "vacation policy",
        })
        {
            Services = services,
        },
        cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(expected, result?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Asserts an empty-result message reports what happened without prescribing how the model should answer.
    /// </summary>
    /// <param name="result">The tool result.</param>
    private static void AssertStatesTheFactWithoutDirectingTheModel(string result)
    {
        Assert.DoesNotContain("general knowledge", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not available in the configured data source", result, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that several phrases are embedded in one batched call and each gets its own index query, so
    /// covering N topics costs one embedding round trip rather than N.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_MultiplePhrases_EmbedsInOneBatchAndSearchesEach()
    {
        var contentManager = new RecordingContentManager(
        [
            new DataSourceSearchResult
            {
                ReferenceId = "doc-1",
                Content = "Employees accrue 15 days per year.",
                Score = 0.9f,
            },
        ]);

        var embeddingGenerator = new RecordingEmbeddingGenerator();
        var services = BuildServices(contentManager, embeddingGenerator);

        var function = CreateFunction(new DataSourceSearchToolSettings { DataSourceId = DataSourceId });

        await InvokeAsync(function, services, "vacation policy", "sick leave policy");

        Assert.Equal(1, embeddingGenerator.BatchCount);
        Assert.Equal(["vacation policy", "sick leave policy"], embeddingGenerator.ReceivedInputs);
        Assert.Equal(2, contentManager.SearchCount);
    }

    /// <summary>
    /// Verifies that a chunk matched by more than one phrase is returned once under a single citation, rather
    /// than repeated per phrase as separate independent calls would.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_MultiplePhrases_ReturnsOverlappingChunkOnce()
    {
        var shared = new DataSourceSearchResult
        {
            ReferenceId = "doc-1",
            Title = "Leave policy",
            Content = "Employees accrue 15 days per year.",
            ChunkIndex = 0,
            Score = 0.9f,
        };

        // Both phrases return the same chunk; only the second also returns a chunk of its own.
        var contentManager = new RecordingContentManager(_ =>
        [
            shared,
            new DataSourceSearchResult
            {
                ReferenceId = "doc-2",
                Title = "Sick leave",
                Content = "Sick days do not roll over.",
                ChunkIndex = 0,
                Score = 0.7f,
            },
        ]);

        var services = BuildServices(contentManager, new RecordingEmbeddingGenerator());
        var function = CreateFunction(new DataSourceSearchToolSettings { DataSourceId = DataSourceId });

        var result = await InvokeAsync(function, services, "vacation policy", "sick leave policy");

        Assert.Equal(1, CountOccurrences(result, "Employees accrue 15 days per year."));
        Assert.Equal(1, CountOccurrences(result, "Sick days do not roll over."));
        Assert.Equal(1, CountOccurrences(result, "[doc:1] = doc-1"));
    }

    /// <summary>
    /// Verifies that a model which ignores the schema cap is truncated rather than rejected, so an over-eager
    /// caller still gets an answer and never runs more index queries than the cap allows.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_CapsThePhraseCount_AndDropsExactRepeats()
    {
        var contentManager = new RecordingContentManager(
        [
            new DataSourceSearchResult
            {
                ReferenceId = "doc-1",
                Content = "Employees accrue 15 days per year.",
                Score = 0.9f,
            },
        ]);

        var embeddingGenerator = new RecordingEmbeddingGenerator();
        var services = BuildServices(contentManager, embeddingGenerator);
        var function = CreateFunction(new DataSourceSearchToolSettings { DataSourceId = DataSourceId });

        var result = await InvokeAsync(function, services, "one", "two", "  two  ", "three", "four", "five");

        Assert.Equal(DataSourceRetrieval.MaxQueries, contentManager.SearchCount);
        Assert.Equal(["one", "two", "three"], embeddingGenerator.ReceivedInputs);
        Assert.Contains("Employees accrue 15 days per year.", result);
    }

    /// <summary>
    /// Verifies that a model sending a bare string instead of the array the schema asks for is still served,
    /// rather than losing a tool call to a shape mismatch it has to diagnose.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_AcceptsABareStringInsteadOfAnArray()
    {
        var contentManager = new RecordingContentManager(
        [
            new DataSourceSearchResult
            {
                ReferenceId = "doc-1",
                Content = "Employees accrue 15 days per year.",
                Score = 0.9f,
            },
        ]);

        var embeddingGenerator = new RecordingEmbeddingGenerator();
        var services = BuildServices(contentManager, embeddingGenerator);
        var function = CreateFunction(new DataSourceSearchToolSettings { DataSourceId = DataSourceId });

        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object>
        {
            ["queries"] = "vacation policy",
        })
        {
            Services = services,
        },
        cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["vacation policy"], embeddingGenerator.ReceivedInputs);
        Assert.Equal(1, contentManager.SearchCount);
        Assert.Contains("Employees accrue 15 days per year.", result?.ToString());
    }

    /// <summary>
    /// Verifies that a single phrase still ranks purely by score, so the fusion added for multiple phrases
    /// leaves the one-phrase path — the profile-bound tool's path — behaving exactly as it did.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_SinglePhrase_RanksByScoreDescending()
    {
        var contentManager = new RecordingContentManager(
        [
            new DataSourceSearchResult { ReferenceId = "doc-low", Content = "Third best.", Score = 0.5f },
            new DataSourceSearchResult { ReferenceId = "doc-high", Content = "Best match.", Score = 0.95f },
            new DataSourceSearchResult { ReferenceId = "doc-mid", Content = "Second best.", Score = 0.8f },
        ]);

        var services = BuildServices(contentManager, new RecordingEmbeddingGenerator());
        var function = CreateFunction(new DataSourceSearchToolSettings { DataSourceId = DataSourceId });

        var result = await InvokeAsync(function, services, "vacation policy");

        Assert.Equal(1, contentManager.SearchCount);
        Assert.True(result.IndexOf("Best match.", StringComparison.Ordinal) < result.IndexOf("Second best.", StringComparison.Ordinal));
        Assert.True(result.IndexOf("Second best.", StringComparison.Ordinal) < result.IndexOf("Third best.", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that ranking is by position within each phrase's own results, not by raw similarity across
    /// phrases. A phrase whose whole result set scores lower still gets its best hit near the top, instead of
    /// being buried under a phrase that happens to produce higher similarities.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_MultiplePhrases_RankByPositionNotRawScoreAcrossPhrases()
    {
        // Similarity scores are only comparable within one query vector. The broad phrase's best match scores
        // 0.60 while every hit for the narrow phrase scores above 0.90 — ranking the union by raw score would
        // bury the broad phrase's answer beneath all three of the narrow phrase's.
        var narrow = new DataSourceSearchResult[]
        {
            new() { ReferenceId = "narrow-1", Content = "Narrow first.", Score = 0.95f },
            new() { ReferenceId = "narrow-2", Content = "Narrow second.", Score = 0.93f },
            new() { ReferenceId = "narrow-3", Content = "Narrow third.", Score = 0.91f },
        };

        var broad = new DataSourceSearchResult[]
        {
            new() { ReferenceId = "broad-1", Content = "Broad first.", Score = 0.60f },
        };

        var narrowVector = "narrow phrase".GetHashCode(StringComparison.Ordinal);

        var contentManager = new RecordingContentManager(vector => vector[0] == narrowVector ? narrow : broad);
        var services = BuildServices(contentManager, new RecordingEmbeddingGenerator());
        var function = CreateFunction(new DataSourceSearchToolSettings { DataSourceId = DataSourceId });

        var result = await InvokeAsync(function, services, "narrow phrase", "broad phrase");

        var narrowFirst = result.IndexOf("Narrow first.", StringComparison.Ordinal);
        var narrowSecond = result.IndexOf("Narrow second.", StringComparison.Ordinal);
        var broadFirst = result.IndexOf("Broad first.", StringComparison.Ordinal);

        Assert.All(new[] { narrowFirst, narrowSecond, broadFirst }, index => Assert.True(index >= 0));

        // Each phrase's top hit outranks the narrow phrase's runner-up, despite scoring 0.33 lower.
        Assert.True(narrowFirst < broadFirst, "The highest-ranked result overall should still come first.");
        Assert.True(broadFirst < narrowSecond, "A phrase's own top hit should outrank another phrase's runner-up.");
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

    private static async Task<string> InvokeAsync(DataSourceSearchToolFunction function, IServiceProvider services, params string[] queries)
    {
        var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object>
        {
            ["queries"] = queries,
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

        public int BatchCount { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions options = null,
            CancellationToken cancellationToken = default)
        {
            var batch = values.ToList();

            BatchCount++;
            ReceivedInputs.AddRange(batch);

            // Each phrase gets its own distinguishable vector so a test can tell which phrase a search ran for.
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                batch.Select(value => new Embedding<float>(new float[] { value.GetHashCode(StringComparison.Ordinal) })).ToList()));
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingContentManager : IDataSourceContentManager
    {
        private readonly IReadOnlyList<DataSourceSearchResult> _results;
        private readonly Func<float[], IReadOnlyList<DataSourceSearchResult>> _resultsByVector;
        private readonly Lock _gate = new();

        public RecordingContentManager(IReadOnlyList<DataSourceSearchResult> results)
        {
            _results = results;
        }

        public RecordingContentManager(Func<float[], IReadOnlyList<DataSourceSearchResult>> resultsByVector)
        {
            _resultsByVector = resultsByVector;
        }

        public string ReceivedDataSourceId { get; private set; }

        public string ReceivedFilter { get; private set; }

        public int ReceivedTopN { get; private set; }

        public int SearchCount { get; private set; }

        public Task<IEnumerable<DataSourceSearchResult>> SearchAsync(
            IIndexProfileInfo indexProfile,
            float[] embedding,
            string dataSourceId,
            int topN,
            string filter = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<DataSourceSearchResult> results;

            lock (_gate)
            {
                ReceivedDataSourceId = dataSourceId;
                ReceivedFilter = filter;
                ReceivedTopN = topN;
                SearchCount++;

                results = _resultsByVector?.Invoke(embedding) ?? _results;
            }

            return Task.FromResult<IEnumerable<DataSourceSearchResult>>(results);
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
