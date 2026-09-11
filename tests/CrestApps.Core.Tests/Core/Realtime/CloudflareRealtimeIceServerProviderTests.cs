using System.Net;
using System.Text;
using CrestApps.Core.AI.Chat.Realtime;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.AI.Chat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace CrestApps.Core.Tests.Core.Realtime;

public sealed class CloudflareRealtimeIceServerProviderTests
{
    private const string ValidResponse = """
    {
        "iceServers": [
            { "urls": ["stun:stun.cloudflare.com:3478"] },
            {
                "urls": [
                    "turn:turn.cloudflare.com:3478?transport=udp",
                    "turns:turn.cloudflare.com:443?transport=tcp"
                ],
                "username": "cf-user",
                "credential": "cf-credential"
            }
        ]
    }
    """;

    [Fact]
    public async Task GetIceServersAsync_WhenTheKeyIsNotConfigured_UsesTheConfiguredServersAndCallsNothing()
    {
        // Arrange
        // Registering the Cloudflare provider must be harmless until a key is supplied, so a deployment
        // running its own coturn is not disturbed by the registration alone.
        var handler = new RecordingHandler(ValidResponse);
        var provider = CreateProvider(handler, new CloudflareTurnOptions(), out _);

        // Act
        var servers = await provider.GetIceServersAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(handler.Requests);
        var server = Assert.Single(servers);
        Assert.Equal(["stun:fallback.example.com:3478"], server.Urls);
    }

    [Fact]
    public async Task GetIceServersAsync_WhenConfigured_ReturnsTheServersCloudflareIssued()
    {
        // Arrange
        var handler = new RecordingHandler(ValidResponse);
        var provider = CreateProvider(handler, Configured(), out _);

        // Act
        var servers = await provider.GetIceServersAsync(TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://cloudflare.test/v1/turn/keys/token-id-1/credentials/generate-ice-servers",
            request.Url);
        Assert.Equal("Bearer token-1", request.Authorization);
        Assert.Contains("\"ttl\":600", request.Body);

        // Returned verbatim: the TLS entry on 443 is what gets a caller out of a locked-down network, so
        // narrowing the list would quietly remove the case TURN exists for.
        Assert.Equal(2, servers.Count);
        Assert.Equal(["stun:stun.cloudflare.com:3478"], servers[0].Urls);
        Assert.Equal(
            ["turn:turn.cloudflare.com:3478?transport=udp", "turns:turn.cloudflare.com:443?transport=tcp"],
            servers[1].Urls);
        Assert.Equal("cf-user", servers[1].Username);
        Assert.Equal("cf-credential", servers[1].Credential);
    }

    [Fact]
    public async Task GetIceServersAsync_WithinHalfTheLifetime_ReusesTheCredentialsAlreadyIssued()
    {
        // Arrange
        var handler = new RecordingHandler(ValidResponse);
        var provider = CreateProvider(handler, Configured(ttlSeconds: 600), out var time);

        // Act
        await provider.GetIceServersAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(299));
        await provider.GetIceServersAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetIceServersAsync_PastHalfTheLifetime_MintsAFreshSet()
    {
        // Arrange
        // Refreshing at the halfway mark is what guarantees a credential handed to a browser stays valid for
        // at least as long again, so no call can outlive the credentials it started with.
        var handler = new RecordingHandler(ValidResponse);
        var provider = CreateProvider(handler, Configured(ttlSeconds: 600), out var time);

        // Act
        await provider.GetIceServersAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(301));
        await provider.GetIceServersAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetIceServersAsync_WhenCloudflareFails_KeepsServingTheCredentialsAlreadyIssued()
    {
        // Arrange
        // The cached credentials are good for about half their lifetime, so an outage must not drop callers
        // behind a strict NAT to STUN only.
        var handler = new RecordingHandler(ValidResponse);
        var provider = CreateProvider(handler, Configured(ttlSeconds: 600), out var time);

        var issued = await provider.GetIceServersAsync(TestContext.Current.CancellationToken);

        handler.FailWith(HttpStatusCode.ServiceUnavailable);
        time.Advance(TimeSpan.FromSeconds(301));

        // Act
        var servers = await provider.GetIceServersAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(issued[1].Username, servers[1].Username);
        Assert.Equal(issued[1].Credential, servers[1].Credential);
    }

    [Fact]
    public async Task GetIceServersAsync_WhenCloudflareFailsBeforeAnythingWasIssued_FallsBackInsteadOfThrowing()
    {
        // Arrange
        var handler = new RecordingHandler(ValidResponse);
        handler.FailWith(HttpStatusCode.Unauthorized);
        var provider = CreateProvider(handler, Configured(), out _);

        // Act
        var servers = await provider.GetIceServersAsync(TestContext.Current.CancellationToken);

        // Assert
        // Degraded, but a session that can still connect directly is better than an exception on the hub.
        var server = Assert.Single(servers);
        Assert.Equal(["stun:fallback.example.com:3478"], server.Urls);
    }

    [Fact]
    public async Task GetIceServersAsync_AfterAFailure_RetriesWithoutWaitingOutTheWholeLifetime()
    {
        // Arrange
        var handler = new RecordingHandler(ValidResponse);
        handler.FailWith(HttpStatusCode.ServiceUnavailable);
        var provider = CreateProvider(handler, Configured(ttlSeconds: 86400), out var time);

        await provider.GetIceServersAsync(TestContext.Current.CancellationToken);

        handler.Recover();

        // Act
        // Half of a 24 hour lifetime is 12 hours away; a failed attempt must not wait that long to retry.
        time.Advance(TimeSpan.FromSeconds(31));
        var servers = await provider.GetIceServersAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("cf-user", servers[1].Username);
    }

    [Fact]
    public async Task GetIceServersAsync_WhenTheKeyChanges_MintsAgainWithTheNewKey()
    {
        // Arrange
        // The key or token can be rotated from an administration screen or by a secret store. Continuing to
        // serve credentials minted with the old key would make the change look as though it were ignored.
        var handler = new RecordingHandler(ValidResponse);
        var provider = CreateProvider(handler, Configured(), out _, out var monitor);

        await provider.GetIceServersAsync(TestContext.Current.CancellationToken);

        monitor.Set(Configured(tokenId: "token-id-2", apiToken: "token-2"));

        // Act
        await provider.GetIceServersAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("/keys/token-id-1/credentials/generate-ice-servers", handler.Requests[0].Url, StringComparison.Ordinal);
        Assert.EndsWith("/keys/token-id-2/credentials/generate-ice-servers", handler.Requests[1].Url, StringComparison.Ordinal);
        Assert.Equal("Bearer token-2", handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task GetIceServersAsync_WithConcurrentCallers_MintsOnlyOneSet()
    {
        // Arrange
        // Every realtime connection resolves servers, so a cold cache under load must not turn into a burst
        // of identical calls to Cloudflare.
        var handler = new RecordingHandler(ValidResponse) { DelayMilliseconds = 25 };
        var provider = CreateProvider(handler, Configured(), out _);

        // Act
        await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
            await provider.GetIceServersAsync(TestContext.Current.CancellationToken)).ToArray());

        // Assert
        Assert.Single(handler.Requests);
    }

    private static CloudflareTurnOptions Configured(
        string tokenId = "token-id-1",
        string apiToken = "token-1",
        int ttlSeconds = 600)
        => new()
        {
            TokenId = tokenId,
            ApiToken = apiToken,
            TtlSeconds = ttlSeconds,
            ApiBaseAddress = "https://cloudflare.test",
        };

    private static CloudflareRealtimeIceServerProvider CreateProvider(
        RecordingHandler handler,
        CloudflareTurnOptions options,
        out TestTimeProvider time)
        => CreateProvider(handler, options, out time, out _);

    private static CloudflareRealtimeIceServerProvider CreateProvider(
        RecordingHandler handler,
        CloudflareTurnOptions options,
        out TestTimeProvider time,
        out TestOptionsMonitor<CloudflareTurnOptions> monitor)
    {
        time = new TestTimeProvider();
        monitor = new TestOptionsMonitor<CloudflareTurnOptions>(options);

        // A deployment that has not opted into Cloudflare still has its own configured servers; this stands in
        // for them so the fallback is observable.
        var services = new ServiceCollection()
            .Configure<RealtimeTransportOptions>(o => o.StunUrls = ["stun:fallback.example.com:3478"])
            .BuildServiceProvider();

        return new CloudflareRealtimeIceServerProvider(
            new TestHttpClientFactory(handler),
            monitor,
            new OptionsRealtimeIceServerProvider(services),
            time,
            NullLogger<CloudflareRealtimeIceServerProvider>.Instance);
    }

    private sealed record RecordedRequest(HttpMethod Method, string Url, string Authorization, string Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _body;
        private HttpStatusCode? _failure;

        public RecordingHandler(string body)
        {
            _body = body;
        }

        public List<RecordedRequest> Requests { get; } = [];

        public int DelayMilliseconds { get; init; }

        public void FailWith(HttpStatusCode statusCode) => _failure = statusCode;

        public void Recover() => _failure = null;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (Requests)
            {
                Requests.Add(new RecordedRequest(
                    request.Method,
                    request.RequestUri!.ToString(),
                    request.Headers.Authorization?.ToString(),
                    body));
            }

            if (DelayMilliseconds > 0)
            {
                await Task.Delay(DelayMilliseconds, cancellationToken);
            }

            return _failure is { } failure
                ? new HttpResponseMessage(failure)
                : new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                };
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public TestHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly List<Action<T, string>> _listeners = [];

        public TestOptionsMonitor(T value)
        {
            CurrentValue = value;
        }

        public T CurrentValue { get; private set; }

        public T Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<T, string> listener)
        {
            _listeners.Add(listener);

            return new Unsubscriber(() => _listeners.Remove(listener));
        }

        public void Set(T value)
        {
            CurrentValue = value;

            foreach (var listener in _listeners.ToArray())
            {
                listener(value, Options.DefaultName);
            }
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly Action _dispose;

            public Unsubscriber(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose() => _dispose();
        }
    }
    [Fact]
    public void AddCloudflareRealtimeTurn_BindsTheKeyFromConfiguration()
    {
        // Arrange
        // The key is supplied as configuration in every real deployment, never in code. Without the binding
        // the options stay empty and the provider quietly defers to the fallback -- indistinguishable from
        // nothing having been configured at all, which is the worst way for this to fail.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CrestApps:AI:RealtimeTransport:Cloudflare:TokenId"] = "token-id-from-configuration",
                ["CrestApps:AI:RealtimeTransport:Cloudflare:ApiToken"] = "token-from-configuration",
                ["CrestApps:AI:RealtimeTransport:Cloudflare:TtlSeconds"] = "1200",
            })
            .Build();

        // Act
        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddCloudflareRealtimeTurn()
            .BuildServiceProvider();

        // Assert
        var options = services.GetRequiredService<IOptionsMonitor<CloudflareTurnOptions>>().CurrentValue;
        Assert.Equal("token-id-from-configuration", options.TokenId);
        Assert.Equal("token-from-configuration", options.ApiToken);
        Assert.Equal(1200, options.TtlSeconds);
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void AddCloudflareRealtimeTurn_WithNoConfiguration_LeavesTheProviderDormant()
    {
        // Registering it must be safe before a key exists.
        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddCloudflareRealtimeTurn()
            .BuildServiceProvider();

        Assert.False(services.GetRequiredService<IOptionsMonitor<CloudflareTurnOptions>>().CurrentValue.IsConfigured);
    }
    [Fact]
    public void AddCloudflareRealtimeTurn_CalledTwice_RegistersOneProviderAndOneBinding()
    {
        // Arrange
        // Hosts enable several realtime surfaces and call the transport registrations per feature. A second
        // call must not wrap the provider around itself, nor bind the configuration twice -- the binder
        // appends to collection properties rather than replacing them, which is how duplicate STUN and TURN
        // URLs got offered to the browser once before.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CrestApps:AI:RealtimeTransport:Cloudflare:TokenId"] = "token-id-from-configuration",
                ["CrestApps:AI:RealtimeTransport:Cloudflare:ApiToken"] = "token-from-configuration",
            })
            .Build();

        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);

        // Act
        services.AddCloudflareRealtimeTurn();
        services.AddCloudflareRealtimeTurn();

        // Assert
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRealtimeIceServerProvider) && descriptor.ImplementationFactory is not null);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<CloudflareTurnOptions>>().CurrentValue;
        Assert.Equal("token-id-from-configuration", options.TokenId);
    }

    [Fact]
    public void AddCloudflareRealtimeTurn_CalledTwiceWithConfigure_StillAppliesTheSecondCallback()
    {
        // The guard must skip the registrations, not the caller's configuration.
        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddCloudflareRealtimeTurn(options => options.TokenId = "first")
            .AddCloudflareRealtimeTurn(options => options.ApiToken = "second")
            .BuildServiceProvider();

        var options = services.GetRequiredService<IOptionsMonitor<CloudflareTurnOptions>>().CurrentValue;
        Assert.Equal("first", options.TokenId);
        Assert.Equal("second", options.ApiToken);
        Assert.True(options.IsConfigured);
    }
    [Theory]
    [InlineData("aaa/../../v1/other")]
    [InlineData("aaa?evil=1")]
    [InlineData("aaa#fragment")]
    public async Task GetIceServersAsync_WithAMalformedTokenId_CannotRetargetTheRequest(string tokenId)
    {
        // Arrange
        // The token id is interpolated into the path, where Uri gives several characters meaning: '..'
        // segments are collapsed and '?' or '#' truncate the path. Unescaped, a token id of
        // "aaa/../../v1/other" drops the keys segment and posts to a different endpoint entirely, which is
        // a far more confusing failure than a 404.
        var handler = new RecordingHandler(ValidResponse);
        var provider = CreateProvider(handler, Configured(tokenId: tokenId), out _);

        // Act
        await provider.GetIceServersAsync(TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(handler.Requests);
        var uri = new Uri(request.Url);

        Assert.StartsWith("/v1/turn/keys/", uri.AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith("/credentials/generate-ice-servers", uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Empty(uri.Query);
        Assert.Empty(uri.Fragment);
    }

    [Fact]
    public async Task GetIceServersAsync_TrimsWhitespaceAroundTheCredentials()
    {
        // Arrange
        // Values pasted out of a dashboard routinely carry surrounding whitespace. A trailing newline in the
        // API token makes the Authorization header throw, and a trailing space on the token id becomes %20
        // in the path; both would show up only as a failure to obtain credentials and a quiet drop to STUN.
        var handler = new RecordingHandler(ValidResponse);
        // (char)10 rather than an escape sequence, so the newline this test is about is unmistakable.
        var provider = CreateProvider(handler, Configured(tokenId: "  token-id-1" + (char)10, apiToken: " token-1 "), out _);

        // Act
        await provider.GetIceServersAsync(TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://cloudflare.test/v1/turn/keys/token-id-1/credentials/generate-ice-servers",
            request.Url);
        Assert.Equal("Bearer token-1", request.Authorization);
    }
}
