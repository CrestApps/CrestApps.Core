#pragma warning disable MEAI001 // The realtime types from Microsoft.Extensions.AI are for evaluation purposes only.
#nullable enable
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.OpenAI.Azure.Realtime;

/// <summary>
/// An <see cref="IRealtimeClient"/> that talks to the Azure OpenAI realtime WebSocket endpoint directly.
/// </summary>
/// <remarks>
/// This is a temporary transport used because the current <c>Azure.AI.OpenAI</c> package targets an older
/// <c>OpenAI</c> SDK than the one <c>Microsoft.Extensions.AI.OpenAI</c> requires, so
/// <c>AzureOpenAIClient.GetRealtimeClient()</c> throws <see cref="MissingMethodException"/> at runtime. Delete the
/// whole <c>Realtime</c> folder and route back through the SDK once a compatible <c>Azure.AI.OpenAI</c> ships.
/// </remarks>
internal sealed class AzureRealtimeClient : IRealtimeClient
{
    /// <summary>Establishes the WebSocket connection. Overridable so the transport can be faked in tests.</summary>
    /// <param name="uri">The fully-qualified realtime WebSocket URI.</param>
    /// <param name="headers">The request headers to attach (authentication).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public delegate ValueTask<WebSocket> ConnectHandler(Uri uri, IReadOnlyList<KeyValuePair<string, string>> headers, CancellationToken cancellationToken);

    private readonly Uri _endpoint;
    private readonly string _deployment;
    private readonly Func<CancellationToken, ValueTask<KeyValuePair<string, string>>> _authHeaderFactory;
    private readonly ConnectHandler _connect;

    public AzureRealtimeClient(
        Uri endpoint,
        string deployment,
        Func<CancellationToken, ValueTask<KeyValuePair<string, string>>> authHeaderFactory,
        ConnectHandler? connect = null)
    {
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _deployment = !string.IsNullOrEmpty(deployment) ? deployment : throw new ArgumentException("A deployment name is required.", nameof(deployment));
        _authHeaderFactory = authHeaderFactory ?? throw new ArgumentNullException(nameof(authHeaderFactory));
        _connect = connect ?? DefaultConnectAsync;
    }

    /// <inheritdoc />
    public async Task<IRealtimeClientSession> CreateSessionAsync(RealtimeSessionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var deployment = string.IsNullOrEmpty(options?.Model) ? _deployment : options!.Model!;
        var uri = BuildRealtimeUri(deployment);
        var authHeader = await _authHeaderFactory(cancellationToken);
        var headers = new[] { authHeader };

        var socket = await _connect(uri, headers, cancellationToken);

        var session = new AzureRealtimeClientSession(socket, options);
        try
        {
            if (options is not null)
            {
                await session.SendAsync(new SessionUpdateRealtimeClientMessage(options), cancellationToken);
            }

            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The client owns no unmanaged resources; sessions are disposed independently.
    }

    private Uri BuildRealtimeUri(string deployment)
    {
        // Azure OpenAI GA realtime endpoint: /openai/v1/realtime with the deployment passed as ?model=,
        // and no api-version query parameter. See
        // https://learn.microsoft.com/azure/foundry/openai/how-to/realtime-audio-websockets.
        var scheme = string.Equals(_endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ? "ws" : "wss";

        return new Uri($"{scheme}://{_endpoint.Authority}/openai/v1/realtime?model={Uri.EscapeDataString(deployment)}");
    }

    private static async ValueTask<WebSocket> DefaultConnectAsync(Uri uri, IReadOnlyList<KeyValuePair<string, string>> headers, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        try
        {
            foreach (var header in headers)
            {
                socket.Options.SetRequestHeader(header.Key, header.Value);
            }

            await socket.ConnectAsync(uri, cancellationToken);
            return socket;
        }
        catch (WebSocketException exception)
        {
            // Surface the handshake failure with the exact URL and any Azure error details so configuration
            // problems (wrong endpoint, deployment name, or key) are diagnosable from the test page.
            var detail = new StringBuilder();
            detail.Append($"Failed to open the Azure realtime WebSocket. URL: {uri}. HTTP status: {(int)socket.HttpStatusCode} ({socket.HttpStatusCode}).");

            if (socket.HttpResponseHeaders is not null)
            {
                foreach (var key in new[] { "x-ms-error-code", "azureml-error-code", "apim-request-id", "www-authenticate" })
                {
                    if (socket.HttpResponseHeaders.TryGetValue(key, out var values))
                    {
                        detail.Append($" {key}: {string.Join(", ", values)}.");
                    }
                }
            }

            socket.Dispose();

            var probe = await ProbeDeploymentsAsync(uri, headers, cancellationToken);
            if (!string.IsNullOrEmpty(probe))
            {
                detail.Append(' ').Append(probe);
            }

            throw new InvalidOperationException(detail.ToString(), exception);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Diagnostic-only: when the realtime handshake fails, calls the plain REST "list deployments" endpoint with the
    /// same credentials to distinguish an invalid key (401) from a missing/mismatched deployment name (200 with a
    /// list that does or does not contain the requested model).
    /// </summary>
    private static async Task<string?> ProbeDeploymentsAsync(Uri realtimeUri, IReadOnlyList<KeyValuePair<string, string>> headers, CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient();
            var restUri = new Uri($"https://{realtimeUri.Authority}/openai/deployments?api-version=2023-05-15");

            using var request = new HttpRequestMessage(HttpMethod.Get, restUri);
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 700)
            {
                body = body[..700];
            }

            return $"Credentials check via GET {restUri}: {(int)response.StatusCode} ({response.StatusCode}). Body: {body}";
        }
        catch (HttpRequestException exception)
        {
            return $"Credentials check failed: {exception.Message}";
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
