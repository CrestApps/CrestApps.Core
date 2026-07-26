using System.Net;
using System.Text.Json;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.Tooling.Instances;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Tooling;

public sealed class AIToolInstanceTests
{
    [Fact]
    public void GetFunctionName_UsesTheInstanceName()
    {
        var instance = new AIToolInstance
        {
            ItemId = "abc123",
            Source = "http-api-request",
            Name = "get_weather",
        };

        var name = instance.GetFunctionName();

        Assert.Equal("tool_instance_get_weather", name);
    }

    [Fact]
    public void GetFunctionName_IsPrefixedSoItCannotCollideWithCodeRegisteredTools()
    {
        var instance = new AIToolInstance
        {
            ItemId = "abc123",
            Source = "http-api-request",
            Name = "get-weather",
        };

        var name = instance.GetFunctionName();

        Assert.StartsWith(AIToolInstanceExtensions.FunctionNamePrefix, name);
        Assert.NotEqual("get-weather", name);
    }

    [Fact]
    public void GetFunctionName_SanitizesDisallowedCharacters()
    {
        var instance = new AIToolInstance
        {
            ItemId = "abc123",
            Source = "http-api-request",
            Name = "weird name!",
        };

        var name = instance.GetFunctionName();

        Assert.DoesNotContain(' ', name);
        Assert.DoesNotContain('!', name);
        Assert.StartsWith("tool_instance_weird_name_", name);
    }

    [Fact]
    public void GetFunctionName_ProducesDistinctNamesWhenSanitizationWouldCollide()
    {
        var first = new AIToolInstance { ItemId = "one", Source = "http-api-request", Name = "weather.api" };
        var second = new AIToolInstance { ItemId = "two", Source = "http-api-request", Name = "weather_api" };

        var firstName = first.GetFunctionName();
        var secondName = second.GetFunctionName();

        Assert.NotEqual(firstName, secondName);
        Assert.DoesNotContain('.', firstName);
        Assert.True(firstName.Length <= 64);
        Assert.True(secondName.Length <= 64);
    }

    [Fact]
    public void Clone_CreatesIndependentPropertiesCopy()
    {
        var original = new AIToolInstance { ItemId = "i", Source = "s", Name = "n", Description = "d" };
        original.Put(new HttpApiRequestToolSettings { BaseUrl = "https://api.example.com" });

        var clone = original.Clone();
        clone.Put(new HttpApiRequestTokenState { AccessToken = "tok" });

        Assert.False(original.TryGet<HttpApiRequestTokenState>(out _));
        Assert.True(clone.TryGet<HttpApiRequestTokenState>(out _));
    }

    [Fact]
    public void GetFunctionName_FallsBackToItemIdWhenNameMissing()
    {
        var instance = new AIToolInstance
        {
            ItemId = "abc123",
            Source = "http-api-request",
        };

        var name = instance.GetFunctionName();

        Assert.Equal("tool_instance_abc123", name);
    }

    [Fact]
    public void GetFunctionName_ProducesDistinctNamesForDistinctInstances()
    {
        var first = new AIToolInstance { ItemId = "one", Source = "http-api-request", Name = "weather_a" };
        var second = new AIToolInstance { ItemId = "two", Source = "http-api-request", Name = "weather_b" };

        Assert.NotEqual(first.GetFunctionName(), second.GetFunctionName());
    }

    [Fact]
    public void GetFunctionName_TruncatesToSixtyFourCharacters()
    {
        var instance = new AIToolInstance
        {
            ItemId = "abc123",
            Source = "source",
            Name = new string('a', 100),
        };

        var name = instance.GetFunctionName();

        Assert.True(name.Length <= 64);
    }

    [Fact]
    public void AddAIToolInstanceSource_RegistersKeyedSourceWithMetadata()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAIToolInstanceSource<HttpApiRequestToolInstanceSource>(HttpApiRequestToolConstants.SourceName, options =>
        {
            options.DisplayName = new LocalizedString(HttpApiRequestToolConstants.SourceName, "HTTP API Request");
            options.Description = new LocalizedString(HttpApiRequestToolConstants.SourceName, "Calls an external HTTP API.");
            options.Category = new LocalizedString("Integrations", "Integrations");
        });

        using var provider = services.BuildServiceProvider();

        var source = provider.GetKeyedService<IAIToolInstanceSource>(HttpApiRequestToolConstants.SourceName);

        Assert.NotNull(source);
        Assert.IsType<HttpApiRequestToolInstanceSource>(source);

        var options = provider.GetRequiredService<IOptions<AIOptions>>().Value;

        Assert.True(options.ToolInstanceSources.TryGetValue(HttpApiRequestToolConstants.SourceName, out var entry));
        Assert.Equal("Integrations", entry.Category.Value);
        Assert.Equal("HTTP API Request", entry.DisplayName.Value);
    }

    [Fact]
    public async Task GetToolsAsync_SurfacesDistinctInstancesOfSameSource()
    {
        var instances = new List<AIToolInstance>
        {
            CreateWeatherInstance("weather-a", "weather_a", "Gets weather from provider A."),
            CreateWeatherInstance("weather-b", "weather_b", "Gets weather from provider B."),
        };

        var provider = BuildProvider(instances);
        var registryProvider = new ToolInstanceRegistryProvider(
            provider,
            provider.GetRequiredService<ILogger<ToolInstanceRegistryProvider>>());

        var context = new AICompletionContext
        {
            ToolInstanceNames = ["weather_a", "weather_b"],
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
    public async Task GetToolsAsync_ReturnsEmptyWhenNoInstanceNames()
    {
        var provider = BuildProvider([]);
        var registryProvider = new ToolInstanceRegistryProvider(
            provider,
            provider.GetRequiredService<ILogger<ToolInstanceRegistryProvider>>());

        var entries = await registryProvider.GetToolsAsync(new AICompletionContext(), TestContext.Current.CancellationToken);

        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetToolsAsync_SkipsInstancesWithUnknownSource()
    {
        var instances = new List<AIToolInstance>
        {
            new()
            {
                ItemId = "orphan",
                Source = "not-registered",
                Name = "orphan",
                Description = "References a missing source.",
            },
        };

        var provider = BuildProvider(instances);
        var registryProvider = new ToolInstanceRegistryProvider(
            provider,
            provider.GetRequiredService<ILogger<ToolInstanceRegistryProvider>>());

        var context = new AICompletionContext
        {
            ToolInstanceNames = ["orphan"],
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

    [Fact]
    public async Task HttpApiRequestToolFunction_KeepsModelProvidedPathOnConfiguredHost()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK, "{}");
        var provider = BuildHttpProvider(handler);

        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com/v1",
            HttpMethod = "GET",
            AuthenticationType = HttpApiRequestAuthenticationType.None,
            AllowModelProvidedPath = true,
        };

        var function = new HttpApiRequestToolFunction("weather", "Gets the weather.", settings);

        var arguments = new AIFunctionArguments
        {
            ["path"] = "https://evil.example.net/steal",
            Services = provider,
        };

        await function.InvokeAsync(arguments, TestContext.Current.CancellationToken);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("api.example.com", handler.LastRequest!.RequestUri!.Host);
    }

    [Fact]
    public async Task HttpApiRequestToolFunction_OAuth2_AcquiresCachesAndReusesToken()
    {
        const string tokenEndpoint = "https://login.example.com/token";
        var handler = new OAuthRoutingHandler(tokenEndpoint, "{\"access_token\":\"tok1\",\"token_type\":\"Bearer\",\"expires_in\":3600}");
        var time = new FixedTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var provider = BuildOAuthProvider(handler, time);

        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com",
            HttpMethod = "GET",
            AuthenticationType = HttpApiRequestAuthenticationType.OAuth2,
            TokenEndpoint = tokenEndpoint,
            ClientId = "client",
            ClientSecret = "secret",
            Scope = "api.read",
            AllowModelProvidedPath = false,
            AllowModelProvidedQuery = false,
            AllowModelProvidedBody = false,
        };

        var instance = new AIToolInstance
        {
            ItemId = "oauth-1",
            Source = HttpApiRequestToolConstants.SourceName,
            Name = "oauth_tool",
        };
        instance.Put(settings);

        var function = new HttpApiRequestToolFunction("oauth_tool", "Calls an OAuth2 API.", settings, instance);

        await function.InvokeAsync(new AIFunctionArguments { Services = provider }, TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.TokenRequestCount);
        Assert.Single(handler.ApiRequests);
        Assert.Equal("Bearer", handler.ApiRequests[0].Headers.Authorization!.Scheme);
        Assert.Equal("tok1", handler.ApiRequests[0].Headers.Authorization.Parameter);
        Assert.Contains("grant_type=client_credentials", handler.TokenRequestBodies[0]);
        Assert.True(instance.TryGet<HttpApiRequestTokenState>(out var cachedState));
        Assert.Equal("tok1", cachedState.AccessToken);

        await function.InvokeAsync(new AIFunctionArguments { Services = provider }, TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.TokenRequestCount);
        Assert.Equal(2, handler.ApiRequests.Count);
        Assert.Equal("tok1", handler.ApiRequests[1].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task HttpApiRequestToolFunction_OAuth2_ProtectsCachedTokenWhenDataProtectionAvailable()
    {
        const string tokenEndpoint = "https://login.example.com/token";
        var handler = new OAuthRoutingHandler(tokenEndpoint, "{\"access_token\":\"tok-secret\",\"token_type\":\"Bearer\",\"expires_in\":3600}");
        var time = new FixedTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var provider = BuildOAuthProvider(handler, time, withDataProtection: true);

        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com",
            HttpMethod = "GET",
            AuthenticationType = HttpApiRequestAuthenticationType.OAuth2,
            TokenEndpoint = tokenEndpoint,
            ClientId = "client",
            ClientSecret = "secret",
            AllowModelProvidedPath = false,
            AllowModelProvidedQuery = false,
            AllowModelProvidedBody = false,
        };

        var instance = new AIToolInstance
        {
            ItemId = "oauth-dp",
            Source = HttpApiRequestToolConstants.SourceName,
            Name = "oauth_dp",
        };
        instance.Put(settings);

        var function = new HttpApiRequestToolFunction("oauth_dp", "Calls an OAuth2 API.", settings, instance);

        await function.InvokeAsync(new AIFunctionArguments { Services = provider }, TestContext.Current.CancellationToken);

        Assert.True(instance.TryGet<HttpApiRequestTokenState>(out var state));
        Assert.NotEqual("tok-secret", state.AccessToken);

        var unprotected = provider
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(HttpApiRequestToolConstants.DataProtectionPurpose)
            .Unprotect(state.AccessToken);

        Assert.Equal("tok-secret", unprotected);
    }

    [Fact]
    public async Task HttpApiRequestToolFunction_OAuth2_UsesRefreshTokenWhenAccessTokenExpired()
    {
        const string tokenEndpoint = "https://login.example.com/token";
        var handler = new OAuthRoutingHandler(tokenEndpoint, "{\"access_token\":\"tok2\",\"token_type\":\"Bearer\",\"expires_in\":3600}");
        var time = new FixedTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var provider = BuildOAuthProvider(handler, time);

        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com",
            HttpMethod = "GET",
            AuthenticationType = HttpApiRequestAuthenticationType.OAuth2,
            TokenEndpoint = tokenEndpoint,
            ClientId = "client",
            ClientSecret = "secret",
            AllowModelProvidedPath = false,
            AllowModelProvidedQuery = false,
            AllowModelProvidedBody = false,
        };

        var instance = new AIToolInstance
        {
            ItemId = "oauth-2",
            Source = HttpApiRequestToolConstants.SourceName,
            Name = "oauth_tool",
        };
        instance.Put(settings);
        instance.Put(new HttpApiRequestTokenState
        {
            AccessToken = "old-token",
            RefreshToken = "refresh-1",
            TokenType = "Bearer",
            ExpiresAtUtc = time.GetUtcNow().AddMinutes(-5),
        });

        var function = new HttpApiRequestToolFunction("oauth_tool", "Calls an OAuth2 API.", settings, instance);

        await function.InvokeAsync(new AIFunctionArguments { Services = provider }, TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.TokenRequestCount);
        Assert.Contains("grant_type=refresh_token", handler.TokenRequestBodies[0]);
        Assert.Contains("refresh_token=refresh-1", handler.TokenRequestBodies[0]);
        Assert.Equal("tok2", handler.ApiRequests[0].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task HttpApiRequestToolFunction_OAuth2_UsesPasswordGrantWhenUsernameConfigured()
    {
        const string tokenEndpoint = "https://login.example.com/token";
        var handler = new OAuthRoutingHandler(tokenEndpoint, "{\"access_token\":\"tok3\",\"token_type\":\"Bearer\",\"expires_in\":3600}");
        var time = new FixedTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var provider = BuildOAuthProvider(handler, time);

        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com",
            HttpMethod = "GET",
            AuthenticationType = HttpApiRequestAuthenticationType.OAuth2,
            TokenEndpoint = tokenEndpoint,
            ClientId = "client",
            ClientSecret = "secret",
            Username = "resource-owner",
            Password = "owner-secret",
            AllowModelProvidedPath = false,
            AllowModelProvidedQuery = false,
            AllowModelProvidedBody = false,
        };

        var instance = new AIToolInstance
        {
            ItemId = "oauth-3",
            Source = HttpApiRequestToolConstants.SourceName,
            Name = "oauth_tool",
        };
        instance.Put(settings);

        var function = new HttpApiRequestToolFunction("oauth_tool", "Calls an OAuth2 API.", settings, instance);

        await function.InvokeAsync(new AIFunctionArguments { Services = provider }, TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.TokenRequestCount);
        Assert.Contains("grant_type=password", handler.TokenRequestBodies[0]);
        Assert.Contains("username=resource-owner", handler.TokenRequestBodies[0]);
        Assert.Contains("password=owner-secret", handler.TokenRequestBodies[0]);
        Assert.Equal("tok3", handler.ApiRequests[0].Headers.Authorization!.Parameter);
    }

    private static AIToolInstance CreateWeatherInstance(string itemId, string name, string description)
    {
        var instance = new AIToolInstance
        {
            ItemId = itemId,
            Source = HttpApiRequestToolConstants.SourceName,
            Name = name,
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
        var catalog = new Mock<INamedCatalog<AIToolInstance>>();
        catalog
            .Setup(c => c.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) =>
                instances.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(catalog.Object);
        services.AddKeyedSingleton<IAIToolInstanceSource, HttpApiRequestToolInstanceSource>(HttpApiRequestToolConstants.SourceName);

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildHttpProvider(CapturingHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(handler));

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildOAuthProvider(HttpMessageHandler handler, TimeProvider timeProvider, bool withDataProtection = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(handler));
        services.AddSingleton(timeProvider);

        if (withDataProtection)
        {
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        }

        return services.BuildServiceProvider();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
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

    private sealed class OAuthRoutingHandler : HttpMessageHandler
    {
        private readonly string _tokenEndpoint;
        private readonly Queue<string> _tokenResponses;

        public OAuthRoutingHandler(string tokenEndpoint, params string[] tokenResponses)
        {
            _tokenEndpoint = tokenEndpoint;
            _tokenResponses = new Queue<string>(tokenResponses);
        }

        public int TokenRequestCount { get; private set; }

        public List<string> TokenRequestBodies { get; } = [];

        public List<HttpRequestMessage> ApiRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (string.Equals(request.RequestUri!.ToString(), _tokenEndpoint, StringComparison.Ordinal))
            {
                TokenRequestCount++;

                if (request.Content is not null)
                {
                    TokenRequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
                }

                var body = _tokenResponses.Count > 1
                    ? _tokenResponses.Dequeue()
                    : _tokenResponses.Peek();

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body),
                };
            }

            ApiRequests.Add(request);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            };
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }
    }
}
