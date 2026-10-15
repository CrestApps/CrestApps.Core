#pragma warning disable OPENAI001 // The AuthenticationPolicy constructor of the OpenAI SDK is for evaluation purposes only.
#pragma warning disable OPENAI002 // The OpenAI Realtime API is experimental and requires explicit opt-in at each usage site.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.AI.OpenAI.Azure.Services;
using CrestApps.Core.Tests.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Realtime;

namespace CrestApps.Core.Tests.Framework.AI.OpenAI.Azure;

/// <summary>
/// Verifies that the Azure OpenAI provider builds OpenAI SDK clients for the resource's <c>/openai/v1/</c> endpoint,
/// caches them per connection and credential, and puts the connection's credential on realtime WebSocket handshakes.
/// </summary>
[Collection(AzureOpenAIClientCacheCollection.Name)]
public sealed class AzureOpenAIClientFactoryTests
{
    private const string ApiKey = "azure-test-key";
    private const string ResourceEndpoint = "https://unit-test.openai.azure.com/";
    private const string V1Endpoint = "https://unit-test.openai.azure.com/openai/v1/";

    [Theory]
    [InlineData("https://unit-test.openai.azure.com", V1Endpoint)]
    [InlineData("https://unit-test.openai.azure.com/", V1Endpoint)]
    [InlineData("https://unit-test.openai.azure.com/openai/v1/", V1Endpoint)]
    [InlineData("https://unit-test.services.ai.azure.com/", "https://unit-test.services.ai.azure.com/openai/v1/")]
    public void GetEndpoint_ReturnsTheResourceV1Endpoint(string endpoint, string expected)
    {
        var connection = ApiKeyConnection(ApiKey, endpoint);

        Assert.Equal(new Uri(expected), AzureOpenAIClientFactory.GetEndpoint(connection));
    }

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("ManagedIdentity")]
    [InlineData("Default")]
    public void Create_UsesTheV1Endpoint_ForEveryAuthenticationType(string authenticationType)
    {
        AzureOpenAIClientFactory.ClearCache();
        var connection = Connection(("Endpoint", ResourceEndpoint), ("ApiKey", ApiKey), ("AuthenticationType", authenticationType));

        var client = AzureOpenAIClientFactory.Create(connection, NullLoggerFactory.Instance, new AzureClientOptions());

        Assert.Equal(new Uri(V1Endpoint), client.Endpoint);
    }

    [Fact]
    public void Create_SameConnection_ReturnsTheCachedClient()
    {
        AzureOpenAIClientFactory.ClearCache();
        var options = new AzureClientOptions();

        var first = AzureOpenAIClientFactory.Create(ApiKeyConnection(ApiKey), NullLoggerFactory.Instance, options);
        var second = AzureOpenAIClientFactory.Create(ApiKeyConnection(ApiKey), NullLoggerFactory.Instance, options);

        Assert.Same(first, second);
    }

    [Fact]
    public void Create_RotatedApiKey_ReturnsANewClient()
    {
        AzureOpenAIClientFactory.ClearCache();
        var options = new AzureClientOptions();

        var first = AzureOpenAIClientFactory.Create(ApiKeyConnection("key-before-rotation"), NullLoggerFactory.Instance, options);
        var second = AzureOpenAIClientFactory.Create(ApiKeyConnection("key-after-rotation"), NullLoggerFactory.Instance, options);

        Assert.NotSame(first, second);
    }

    [Fact]
    public void Create_DifferentAuthenticationType_ReturnsANewClient()
    {
        AzureOpenAIClientFactory.ClearCache();
        var options = new AzureClientOptions();

        var apiKeyClient = AzureOpenAIClientFactory.Create(ApiKeyConnection(ApiKey), NullLoggerFactory.Instance, options);
        var entraClient = AzureOpenAIClientFactory.Create(Connection(("Endpoint", ResourceEndpoint), ("AuthenticationType", "Default")), NullLoggerFactory.Instance, options);

        Assert.NotSame(apiKeyClient, entraClient);
    }

    [Fact]
    public void ClearCache_ForgetsTheCachedClients()
    {
        AzureOpenAIClientFactory.ClearCache();
        var options = new AzureClientOptions();
        var first = AzureOpenAIClientFactory.Create(ApiKeyConnection(ApiKey), NullLoggerFactory.Instance, options);

        AzureOpenAIClientFactory.ClearCache();
        var second = AzureOpenAIClientFactory.Create(ApiKeyConnection(ApiKey), NullLoggerFactory.Instance, options);

        Assert.NotSame(first, second);
    }

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("ManagedIdentity")]
    [InlineData("Default")]
    public void CreateRealtimeClient_UsesTheV1Endpoint_ForEveryAuthenticationType(string authenticationType)
    {
        var connection = Connection(("Endpoint", ResourceEndpoint), ("ApiKey", ApiKey), ("AuthenticationType", authenticationType));

        var client = AzureOpenAIClientFactory.CreateRealtimeClient(connection);

        Assert.IsType<AzureRealtimeClient>(client);
        Assert.Equal(new Uri(V1Endpoint), client.Endpoint);
    }

    [Fact]
    public async Task CreateRealtimeClient_WithApiKey_SendsTheKeyOnTheHandshake_ToTheDeploymentOnV1()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var server = RealtimeHandshakeServer.Start();
        var client = AzureOpenAIClientFactory.CreateRealtimeClient(ApiKeyConnection(ApiKey, server.Endpoint.AbsoluteUri));

        using var session = await client.StartConversationSessionAsync("gpt-realtime", cancellationToken: timeout.Token);
        var handshake = await server.NextHandshakeAsync(timeout.Token);

        Assert.Equal("/openai/v1/realtime", handshake.Url.AbsolutePath);
        Assert.Equal("?model=gpt-realtime", handshake.Url.Query);
        Assert.Equal(ApiKey, handshake.Headers["api-key"]);
        Assert.Null(handshake.Headers["Authorization"]);
    }

    [Fact]
    public async Task AzureRealtimeClient_AcquiresTheAuthenticationHeader_WhenEachSessionStarts()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var server = RealtimeHandshakeServer.Start();
        var tokensAcquired = 0;
        var client = new AzureRealtimeClient(
            ApiKeyAuthenticationPolicy.CreateBearerAuthorizationPolicy(new ApiKeyCredential("unused-by-the-handshake")),
            new RealtimeClientOptions { Endpoint = new Uri(server.Endpoint, "/openai/v1/") },
            _ => ValueTask.FromResult(new KeyValuePair<string, string>("Authorization", $"Bearer token-{++tokensAcquired}")));

        using var firstSession = await client.StartConversationSessionAsync("gpt-realtime", cancellationToken: timeout.Token);
        var firstHandshake = await server.NextHandshakeAsync(timeout.Token);
        using var secondSession = await client.StartConversationSessionAsync("gpt-realtime", cancellationToken: timeout.Token);
        var secondHandshake = await server.NextHandshakeAsync(timeout.Token);

        Assert.Equal("Bearer token-1", firstHandshake.Headers["Authorization"]);
        Assert.Equal("Bearer token-2", secondHandshake.Headers["Authorization"]);
        Assert.Equal(2, tokensAcquired);
    }

    private static AIProviderConnectionEntry ApiKeyConnection(string apiKey, string endpoint = ResourceEndpoint)
        => Connection(("Endpoint", endpoint), ("ApiKey", apiKey), ("AuthenticationType", "ApiKey"));

    private static AIProviderConnectionEntry Connection(params (string Key, string Value)[] values)
        => new(values.ToDictionary(pair => pair.Key, object (pair) => pair.Value));

    /// <summary>
    /// A loopback WebSocket server that records the URL and headers of every handshake it accepts.
    /// </summary>
    private sealed class RealtimeHandshakeServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Channel<(Uri Url, NameValueCollection Headers)> _handshakes = Channel.CreateUnbounded<(Uri, NameValueCollection)>();

        private RealtimeHandshakeServer(int port)
        {
            Endpoint = new Uri($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add(Endpoint.AbsoluteUri);
            _listener.Start();
            _ = AcceptAsync();
        }

        /// <summary>
        /// Gets the server's address, which stands in for the Azure OpenAI resource endpoint.
        /// </summary>
        public Uri Endpoint { get; }

        /// <summary>
        /// Starts a server on a free loopback port.
        /// </summary>
        public static RealtimeHandshakeServer Start()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            return new RealtimeHandshakeServer(port);
        }

        /// <summary>
        /// Waits for the next handshake and returns the URL and headers the client sent.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask<(Uri Url, NameValueCollection Headers)> NextHandshakeAsync(CancellationToken cancellationToken)
            => _handshakes.Reader.ReadAsync(cancellationToken);

        /// <summary>
        /// Stops listening.
        /// </summary>
        public void Dispose()
            => _listener.Close();

        /// <summary>
        /// Accepts connections until the listener is closed, recording each handshake and completing it as a WebSocket.
        /// </summary>
        private async Task AcceptAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                await _handshakes.Writer.WriteAsync((context.Request.Url, context.Request.Headers));

                var webSocketContext = await context.AcceptWebSocketAsync("realtime");

                webSocketContext.WebSocket.Dispose();
            }
        }
    }
}
