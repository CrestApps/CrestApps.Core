using System.Net;
using System.Text.Json;
using CrestApps.Core;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.Tooling.Instances;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Tooling;

public sealed class AIToolInstanceTests
{
    [Fact]
    public void GetFunctionName_CombinesSourceAndItemId()
    {
        var instance = new AIToolInstance
        {
            ItemId = "abc123",
            Source = "http-api-request",
        };

        var name = AIToolInstanceNaming.GetFunctionName(instance);

        Assert.Equal("http-api-request_abc123", name);
    }

    [Fact]
    public void GetFunctionName_SanitizesDisallowedCharacters()
    {
        var instance = new AIToolInstance
        {
            ItemId = "id with spaces!",
            Source = "weird source",
        };

        var name = AIToolInstanceNaming.GetFunctionName(instance);

        Assert.DoesNotContain(' ', name);
        Assert.DoesNotContain('!', name);
        Assert.Equal("weird_source_id_with_spaces_", name);
    }

    [Fact]
    public void GetFunctionName_ProducesDistinctNamesForDistinctInstances()
    {
        var first = new AIToolInstance { ItemId = "one", Source = "http-api-request" };
        var second = new AIToolInstance { ItemId = "two", Source = "http-api-request" };

        Assert.NotEqual(
            AIToolInstanceNaming.GetFunctionName(first),
            AIToolInstanceNaming.GetFunctionName(second));
    }

    [Fact]
    public void GetFunctionName_TruncatesToSixtyFourCharacters()
    {
        var instance = new AIToolInstance
        {
            ItemId = new string('a', 100),
            Source = "source",
        };

        var name = AIToolInstanceNaming.GetFunctionName(instance);

        Assert.True(name.Length <= 64);
    }

    [Fact]
    public void AddAIToolInstanceDefinition_RegistersKeyedDefinitionAndMetadata()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiRequestToolInstance();

        using var provider = services.BuildServiceProvider();

        var definition = provider.GetKeyedService<IAIToolInstanceDefinition>(HttpApiRequestToolConstants.DefinitionName);
        var options = provider.GetRequiredService<IOptions<AIToolInstanceDefinitionOptions>>().Value;

        Assert.NotNull(definition);
        Assert.IsType<HttpApiRequestToolDefinition>(definition);
        Assert.True(options.TryGet(HttpApiRequestToolConstants.DefinitionName, out var entry));
        Assert.Equal("Integrations", entry.Category);
    }

    [Fact]
    public async Task GetToolsAsync_SurfacesDistinctInstancesOfSameDefinition()
    {
        var instances = new List<AIToolInstance>
        {
            CreateWeatherInstance("weather-a", "Gets weather from provider A."),
            CreateWeatherInstance("weather-b", "Gets weather from provider B."),
        };

        var provider = BuildProvider(instances);
        var registryProvider = new ToolInstanceRegistryProvider(
            provider,
            provider.GetRequiredService<ILogger<ToolInstanceRegistryProvider>>());

        var context = new AICompletionContext
        {
            ToolInstanceIds = ["weather-a", "weather-b"],
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
    public async Task GetToolsAsync_ReturnsEmptyWhenNoInstanceIds()
    {
        var provider = BuildProvider([]);
        var registryProvider = new ToolInstanceRegistryProvider(
            provider,
            provider.GetRequiredService<ILogger<ToolInstanceRegistryProvider>>());

        var entries = await registryProvider.GetToolsAsync(new AICompletionContext(), TestContext.Current.CancellationToken);

        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetToolsAsync_SkipsInstancesWithUnknownDefinition()
    {
        var instances = new List<AIToolInstance>
        {
            new()
            {
                ItemId = "orphan",
                Source = "not-registered",
                DisplayText = "Orphan",
                Description = "References a missing definition.",
            },
        };

        var provider = BuildProvider(instances);
        var registryProvider = new ToolInstanceRegistryProvider(
            provider,
            provider.GetRequiredService<ILogger<ToolInstanceRegistryProvider>>());

        var context = new AICompletionContext
        {
            ToolInstanceIds = ["orphan"],
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

    private static AIToolInstance CreateWeatherInstance(string itemId, string description)
    {
        var instance = new AIToolInstance
        {
            ItemId = itemId,
            Source = HttpApiRequestToolConstants.DefinitionName,
            DisplayText = itemId,
            Description = description,
        };

        instance.Put(new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com",
            HttpMethod = "GET",
            AuthenticationType = HttpApiRequestAuthenticationType.None,
        });

        return instance;
    }

    private static ServiceProvider BuildProvider(IReadOnlyCollection<AIToolInstance> instances)
    {
        var catalog = new Mock<ISourceCatalog<AIToolInstance>>();
        catalog
            .Setup(c => c.GetAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) =>
                instances.Where(i => ids.Contains(i.ItemId)).ToArray());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(catalog.Object);
        services.AddSingleton<HttpApiRequestToolDefinition>();
        services.AddKeyedSingleton<IAIToolInstanceDefinition>(
            HttpApiRequestToolConstants.DefinitionName,
            (sp, _) => sp.GetRequiredService<HttpApiRequestToolDefinition>());

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
