using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.Tooling.Instances.DataSources;
using CrestApps.Core.AI.Tools;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Tools;

/// <summary>
/// Pins the profile-bound search tool's argument schema to the tool instance's. A model that has learned to
/// narrow a search to figures or charts on one of them must be able to do the same on the other, so the two
/// speak one vocabulary rather than two.
/// </summary>
public sealed class DataSourceSearchToolSchemaTests
{
    private const string DataSourceId = "data-source-1";
    private const string ProviderName = "TestProvider";
    private const string KnowledgeBaseName = "kb-index";
    private const string EmbeddingDeploymentName = "text-embedding-3-small";

    /// <summary>
    /// Verifies that both tools offer the same kinds of knowledge, under the same argument name.
    /// </summary>
    [Fact]
    public void JsonSchema_ExposesTheSameContentTypeVocabularyAsTheToolInstance()
    {
        var systemTool = ReadContentTypeEnum(new DataSourceSearchTool().JsonSchema);
        var instance = ReadContentTypeEnum(CreateInstanceFunction().JsonSchema);

        Assert.Equal(["text", "figure", "chart", "table", "article", "document"], instance);
        Assert.Equal(instance, systemTool);
    }

    /// <summary>
    /// Verifies that the wording describing the argument is the one the tool instance already proved, rather
    /// than a second description the model has to reconcile with the first.
    /// </summary>
    [Fact]
    public void JsonSchema_DescribesContentTypesWithTheSameWordingAsTheToolInstance()
    {
        var systemTool = ReadContentTypesProperty(new DataSourceSearchTool().JsonSchema);
        var instance = ReadContentTypesProperty(CreateInstanceFunction().JsonSchema);

        Assert.Equal(
            instance.GetProperty("description").GetString(),
            systemTool.GetProperty("description").GetString());
    }

    /// <summary>
    /// Verifies that the narrowing argument is optional and that the search phrase is still required, because
    /// every existing caller passes nothing but a query.
    /// </summary>
    [Fact]
    public void JsonSchema_KeepsQueryRequired_AndLeavesContentTypesOptional()
    {
        var schema = new DataSourceSearchTool().JsonSchema;
        var properties = schema.GetProperty("properties");

        Assert.Equal("string", properties.GetProperty("query").GetProperty("type").GetString());
        Assert.Equal("array", properties.GetProperty("contentTypes").GetProperty("type").GetString());

        var required = schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToArray();

        Assert.Equal(["query"], required);
    }

    /// <summary>
    /// Verifies that the name the orchestration handlers inject into the prompt templates is unchanged, since
    /// those templates name the tool literally.
    /// </summary>
    [Fact]
    public void Name_IsUnchanged()
    {
        Assert.Equal("search_data_sources", new DataSourceSearchTool().Name);
    }

    /// <summary>
    /// Verifies that the kinds the model asks for reach the index as a filter, so asking for charts searches
    /// charts rather than everything.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_NarrowsTheIndexFilter_ToTheRequestedContentTypes()
    {
        var recorder = new SearchRecorder();

        await using var services = BuildServices(recorder);

        await InvokeAsync(services, new Dictionary<string, object>
        {
            ["query"] = "quarterly figures",
            ["contentTypes"] = new[] { "figure", "chart" },
        });

        Assert.Contains("contentType eq 'figure'", recorder.Filter, StringComparison.Ordinal);
        Assert.Contains("contentType eq 'chart'", recorder.Filter, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the kinds survive arriving as raw JSON, which is the shape a model's tool call takes
    /// before anything deserializes it.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_NarrowsTheIndexFilter_WhenTheKindsArriveAsJson()
    {
        var recorder = new SearchRecorder();

        await using var services = BuildServices(recorder);

        await InvokeAsync(services, new Dictionary<string, object>
        {
            ["query"] = "quarterly figures",
            ["contentTypes"] = JsonSerializer.Deserialize<JsonElement>("""["table"]"""),
        });

        Assert.Contains("contentType eq 'table'", recorder.Filter, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a caller that names no kinds still searches everything, so the existing query-only calls
    /// behave exactly as they did.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_SearchesEveryKind_WhenNoContentTypesAreRequested()
    {
        var recorder = new SearchRecorder();

        await using var services = BuildServices(recorder);

        await InvokeAsync(services, new Dictionary<string, object>
        {
            ["query"] = "quarterly figures",
        });

        Assert.Null(recorder.Filter);
    }

    private static async Task<string> InvokeAsync(IServiceProvider services, Dictionary<string, object> arguments)
    {
        var profile = new AIProfile
        {
            ItemId = "profile-1",
            Name = "grounded",
            Type = AIProfileType.Chat,
        };

        using var scope = AIInvocationScope.Begin();

        AIInvocationScope.Current.DataSourceId = DataSourceId;
        AIInvocationScope.Current.ToolExecutionContext = new AIToolExecutionContext(profile);

        var result = await new DataSourceSearchTool().InvokeAsync(new AIFunctionArguments(arguments)
        {
            Services = services,
        },
        cancellationToken: TestContext.Current.CancellationToken);

        return result?.ToString() ?? string.Empty;
    }

    private static DataSourceSearchToolFunction CreateInstanceFunction()
    {
        return new DataSourceSearchToolFunction(
            "knowledge-base",
            "Searches the knowledge base.",
            new DataSourceSearchToolSettings
            {
                DataSourceId = DataSourceId,
            });
    }

    private static JsonElement ReadContentTypesProperty(JsonElement schema)
    {
        return schema.GetProperty("properties").GetProperty("contentTypes");
    }

    private static string[] ReadContentTypeEnum(JsonElement schema)
    {
        return ReadContentTypesProperty(schema)
            .GetProperty("items")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
    }

    private static ServiceProvider BuildServices(SearchRecorder recorder)
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

        var results = new DataSourceSearchResult[]
        {
            new()
            {
                ReferenceId = "doc-1",
                Title = "Quarterly figures",
                Content = "The chart shows a steady rise.",
                ChunkIndex = 0,
                ReferenceType = "Content",
                Score = 0.9f,
            },
        };

        var contentManager = new Mock<IDataSourceContentManager>();
        contentManager
            .Setup(manager => manager.SearchAsync(
                It.IsAny<IIndexProfileInfo>(),
                It.IsAny<float[]>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback((IIndexProfileInfo _, float[] _, string _, int _, string filter, CancellationToken _) => recorder.Filter = filter)
            .ReturnsAsync(results);

        var textNormalizer = new Mock<IAITextNormalizer>();
        textNormalizer
            .Setup(normalizer => normalizer.NormalizeTitle(It.IsAny<string>()))
            .Returns((string title) => title);

        // The provider's own filter syntax is not what is under test, so the translator hands back what it was
        // given and the assertions read the composed expression directly.
        var filterTranslator = new Mock<IODataFilterTranslator>();
        filterTranslator
            .Setup(translator => translator.Translate(It.IsAny<string>()))
            .Returns((string filter) => filter);

        var services = new ServiceCollection();

        services.AddSingleton(dataSourceStore.Object);
        services.AddSingleton(indexProfileStore.Object);
        services.AddSingleton(deploymentManager.Object);
        services.AddSingleton(clientFactory.Object);
        services.AddSingleton(textNormalizer.Object);
        services.AddKeyedSingleton(ProviderName, contentManager.Object);
        services.AddKeyedSingleton(ProviderName, filterTranslator.Object);
        services.AddSingleton<IOptionsMonitor<AIDataSourceOptions>>(new TestOptionsMonitor<AIDataSourceOptions>
        {
            CurrentValue = new AIDataSourceOptions(),
        });
        services.AddLogging();

        return services.BuildServiceProvider();
    }

    private sealed class SearchRecorder
    {
        public string Filter { get; set; }
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
