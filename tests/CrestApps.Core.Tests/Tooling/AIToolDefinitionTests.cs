using System.Net;
using System.Text.Json;
using CrestApps.Core;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.Tooling.Sources;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace CrestApps.Core.Tests.Tooling;

public sealed class AIToolDefinitionTests
{
    [Fact]
    public void GetFunctionName_CombinesSourceAndItemId()
    {
        var definition = new AIToolDefinition
        {
            ItemId = "abc123",
            Source = "http-api-request",
        };

        var name = AIToolDefinitionNaming.GetFunctionName(definition);

        Assert.Equal("http-api-request_abc123", name);
    }

    [Fact]
    public void GetFunctionName_SanitizesDisallowedCharacters()
    {
        var definition = new AIToolDefinition
        {
            ItemId = "id with spaces!",
            Source = "weird source",
        };

        var name = AIToolDefinitionNaming.GetFunctionName(definition);

        Assert.DoesNotContain(' ', name);
        Assert.DoesNotContain('!', name);
        Assert.Equal("weird_source_id_with_spaces_", name);
    }

    [Fact]
    public void GetFunctionName_ProducesDistinctNamesForDistinctDefinitions()
    {
        var first = new AIToolDefinition { ItemId = "one", Source = "http-api-request" };
        var second = new AIToolDefinition { ItemId = "two", Source = "http-api-request" };

        Assert.NotEqual(
            AIToolDefinitionNaming.GetFunctionName(first),
            AIToolDefinitionNaming.GetFunctionName(second));
    }

    [Fact]
    public void GetFunctionName_TruncatesToSixtyFourCharacters()
    {
        var definition = new AIToolDefinition
        {
            ItemId = new string('a', 100),
            Source = "source",
        };

        var name = AIToolDefinitionNaming.GetFunctionName(definition);

        Assert.True(name.Length <= 64);
    }

    [Fact]
    public void AddAIToolSource_RegistersEnumerableSourceWithMetadata()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiRequestToolSource();

        using var provider = services.BuildServiceProvider();

        var source = provider.GetServices<AIToolSource>()
            .SingleOrDefault(s => s.Name == HttpApiRequestToolConstants.SourceName);

        Assert.NotNull(source);
        Assert.IsType<HttpApiRequestToolSource>(source);
        Assert.Equal("Integrations", source.Category);
        Assert.Equal("HTTP API Request", source.DisplayName.Value);
    }

    [Fact]
    public async Task GetToolsAsync_SurfacesDistinctDefinitionsOfSameSource()
    {
        var definitions = new List<AIToolDefinition>
        {
            CreateWeatherDefinition("weather-a", "Gets weather from provider A."),
            CreateWeatherDefinition("weather-b", "Gets weather from provider B."),
        };

        var provider = BuildProvider(definitions);
        var registryProvider = new ToolDefinitionRegistryProvider(
            provider,
            provider.GetServices<AIToolSource>(),
            provider.GetRequiredService<ILogger<ToolDefinitionRegistryProvider>>());

        var context = new AICompletionContext
        {
            ToolDefinitionIds = ["weather-a", "weather-b"],
        };

        var entries = await registryProvider.GetToolsAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(2, entries.Count);
        Assert.Equal(2, entries.Select(e => e.Name).Distinct().Count());
        Assert.Equal(2, entries.Select(e => e.Description).Distinct().Count());
        Assert.Contains(entries, e => e.Description == "Gets weather from provider A.");
        Assert.Contains(entries, e => e.Description == "Gets weather from provider B.");

        foreach (var entry in entries)
        {
            var tool = await entry.CreateAsync(provider);
            var function = Assert.IsAssignableFrom<AIFunction>(tool);
            Assert.Equal(entry.Name, function.Name);
            Assert.Equal(entry.Description, function.Description);
        }
    }

    [Fact]
    public async Task GetToolsAsync_ReturnsEmptyWhenNoDefinitionIds()
    {
        var provider = BuildProvider([]);
        var registryProvider = new ToolDefinitionRegistryProvider(
            provider,
            provider.GetServices<AIToolSource>(),
            provider.GetRequiredService<ILogger<ToolDefinitionRegistryProvider>>());

        var entries = await registryProvider.GetToolsAsync(new AICompletionContext(), TestContext.Current.CancellationToken);

        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetToolsAsync_SkipsDefinitionsWithUnknownSource()
    {
        var definitions = new List<AIToolDefinition>
        {
            new()
            {
                ItemId = "orphan",
                Source = "not-registered",
                DisplayText = "Orphan",
                Description = "References a missing source.",
            },
        };

        var provider = BuildProvider(definitions);
        var registryProvider = new ToolDefinitionRegistryProvider(
            provider,
            provider.GetServices<AIToolSource>(),
            provider.GetRequiredService<ILogger<ToolDefinitionRegistryProvider>>());

        var context = new AICompletionContext
        {
            ToolDefinitionIds = ["orphan"],
        };

        var entries = await registryProvider.GetToolsAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(entries);
    }

    [Fact]
    public async Task HttpApiRequestToolFunction_BuildsRequestFromSettingsAndModelArguments()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK, "{\"ok\":true}");
        var provider = BuildHttpProvider(handler);

        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com/v1",
            HttpMethod = "POST",
            AuthenticationType = HttpApiRequestAuthenticationType.Bearer,
            BearerToken = "secret-token",
            DefaultHeaders = new Dictionary<string, string> { ["Accept"] = "application/json" },
            AllowModelProvidedPath = true,
            AllowModelProvidedQuery = true,
            AllowModelProvidedBody = true,
        };

        var function = new HttpApiRequestToolFunction("weather", "Gets the weather.", settings);

        var arguments = new AIFunctionArguments
        {
            ["path"] = "forecast",
            ["query"] = new Dictionary<string, object> { ["city"] = "Seattle" },
            ["body"] = new Dictionary<string, object> { ["days"] = "3" },
            Services = provider,
        };

        var result = await function.InvokeAsync(arguments, TestContext.Current.CancellationToken);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal("https://api.example.com/v1/forecast?city=Seattle", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("secret-token", handler.LastRequest.Headers.Authorization.Parameter);
        Assert.Contains(handler.LastRequest.Headers, h => h.Key == "Accept");
        Assert.Equal("{\"days\":\"3\"}", handler.LastRequestBody);

        using var document = JsonDocument.Parse(result!.ToString()!);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(200, document.RootElement.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task HttpApiRequestToolFunction_OmitsPathWhenModelProvidedPathDisabled()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK, "{}");
        var provider = BuildHttpProvider(handler);

        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com/fixed",
            HttpMethod = "GET",
            AuthenticationType = HttpApiRequestAuthenticationType.None,
            AllowModelProvidedPath = false,
            AllowModelProvidedQuery = false,
            AllowModelProvidedBody = false,
        };

        var function = new HttpApiRequestToolFunction("fixed", "Fixed endpoint.", settings);

        var arguments = new AIFunctionArguments
        {
            ["path"] = "should-be-ignored",
            Services = provider,
        };

        await function.InvokeAsync(arguments, TestContext.Current.CancellationToken);

        Assert.Equal("https://api.example.com/fixed", handler.LastRequest!.RequestUri!.ToString());
    }

    private static AIToolDefinition CreateWeatherDefinition(string itemId, string description)
    {
        var definition = new AIToolDefinition
        {
            ItemId = itemId,
            Source = HttpApiRequestToolConstants.SourceName,
            DisplayText = itemId,
            Description = description,
        };

        definition.Put(new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com",
            HttpMethod = "GET",
            AuthenticationType = HttpApiRequestAuthenticationType.None,
        });

        return definition;
    }

    private static ServiceProvider BuildProvider(IReadOnlyCollection<AIToolDefinition> definitions)
    {
        var catalog = new Mock<ISourceCatalog<AIToolDefinition>>();
        catalog
            .Setup(c => c.GetAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) =>
                definitions.Where(d => ids.Contains(d.ItemId)).ToArray());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(catalog.Object);
        services.AddSingleton<AIToolSource, HttpApiRequestToolSource>();

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildHttpProvider(CapturingHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(handler));

        return services.BuildServiceProvider();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly CapturingHttpMessageHandler _handler;

        public StubHttpClientFactory(CapturingHttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public CapturingHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        public HttpRequestMessage LastRequest { get; private set; }

        public string LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody),
            };
        }
    }
}
