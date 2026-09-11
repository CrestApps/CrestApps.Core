using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Resolves the realtime ICE servers from Cloudflare Realtime TURN, which issues short-lived credentials
/// through an API rather than exposing a shared secret.
/// </summary>
/// <remarks>
/// <para>
/// Credentials are fetched once and reused until they are halfway through their lifetime, so a call is made
/// roughly twice per <see cref="CloudflareTurnOptions.TtlSeconds"/> rather than per connection. Refreshing at
/// the halfway point means a credential handed to a browser is always valid for at least as long again, so no
/// session can outlive the credentials it started with.
/// </para>
/// <para>
/// Nothing here expires on its own: the key and token used to mint credentials are long-lived, and every
/// credential the browser sees is minted on demand. That is the whole point of preferring this over pasting a
/// generated username and password into configuration, which stops working when they lapse.
/// </para>
/// </remarks>
internal sealed class CloudflareRealtimeIceServerProvider : IRealtimeIceServerProvider, IDisposable
{
    /// <summary>
    /// The shortest interval between refresh attempts after a failure. Long enough not to hammer Cloudflare
    /// while an outage lasts, short enough to recover well inside the lifetime of the credentials in hand.
    /// </summary>
    private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<CloudflareTurnOptions> _optionsMonitor;
    private readonly OptionsRealtimeIceServerProvider _fallback;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CloudflareRealtimeIceServerProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IDisposable _optionsChangeRegistration;

    private IReadOnlyList<WebRtcIceServer> _cached;
    private DateTimeOffset _refreshAfter;

    /// <summary>
    /// Initializes a new instance of the <see cref="CloudflareRealtimeIceServerProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="optionsMonitor">The Cloudflare TURN options, watched so a change takes effect at once.</param>
    /// <param name="fallback">The configured-servers provider used while Cloudflare is not configured.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public CloudflareRealtimeIceServerProvider(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<CloudflareTurnOptions> optionsMonitor,
        OptionsRealtimeIceServerProvider fallback,
        TimeProvider timeProvider,
        ILogger<CloudflareRealtimeIceServerProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsMonitor = optionsMonitor;
        _fallback = fallback;
        _timeProvider = timeProvider;
        _logger = logger;

        // The key or token can be changed while the application is running -- from an administration screen,
        // or by a secret store rotating them underneath. Credentials minted with the previous key stay
        // accepted by Cloudflare for their remaining lifetime, so nothing breaks immediately, but continuing
        // to serve them would leave the change looking as though it had been ignored.
        _optionsChangeRegistration = _optionsMonitor.OnChange(_ => Invalidate());
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WebRtcIceServer>> GetIceServersAsync(CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;

        if (!options.IsConfigured)
        {
            // Not a Cloudflare deployment. Behave exactly as though this provider were not registered, so the
            // configured STUN and TURN servers, and a coturn shared secret, keep working untouched.
            return await _fallback.GetIceServersAsync(cancellationToken);
        }

        if (TryGetCached(out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Another caller may have refreshed while this one waited for the gate.
            if (TryGetCached(out cached))
            {
                return cached;
            }

            var servers = await FetchIceServersAsync(options, cancellationToken);

            _cached = servers;
            _refreshAfter = _timeProvider.GetUtcNow().AddSeconds(Math.Max(options.TtlSeconds / 2, 60));

            return servers;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Credentials are refreshed at the halfway mark, so whatever is cached is still valid for about
            // half its lifetime. Serving it through a Cloudflare outage is strictly better than dropping every
            // caller behind a strict NAT to STUN only, which is what returning the fallback would do.
            _refreshAfter = _timeProvider.GetUtcNow().Add(_retryDelay);

            if (_cached is not null)
            {
                _logger.LogWarning(ex, "Could not refresh the Cloudflare TURN credentials. Reusing the credentials already issued, which remain valid for about half their lifetime.");

                return _cached;
            }

            _logger.LogError(ex, "Could not obtain Cloudflare TURN credentials, and none have been issued yet. Realtime voice will fall back to the configured servers, which for most deployments means STUN only.");

            return await _fallback.GetIceServersAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Drops the cached credentials so the next caller mints a new set.
    /// </summary>
    private void Invalidate()
    {
        _cached = null;
        _refreshAfter = default;
    }

    private bool TryGetCached(out IReadOnlyList<WebRtcIceServer> servers)
    {
        servers = _cached;

        return servers is not null && _timeProvider.GetUtcNow() < _refreshAfter;
    }

    private async Task<IReadOnlyList<WebRtcIceServer>> FetchIceServersAsync(CloudflareTurnOptions options, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(CloudflareRealtimeIceServerProvider));

        // Values pasted out of a dashboard routinely carry surrounding whitespace. A trailing newline in
        // the API token makes the Authorization header throw outright, and a trailing space on the token id
        // becomes %20 in the path and is answered with a 404. Both would surface only as a failure to obtain
        // credentials and a quiet drop to STUN, so they are trimmed rather than diagnosed later.
        var tokenId = options.TokenId.Trim();
        var apiToken = options.ApiToken.Trim();

        // Cloudflare's route spells the token as a key; the value is the Token ID from the dashboard.
        //
        // Escaped because it is interpolated into the path, where Uri gives several characters meaning that
        // silently retargets the request rather than failing: '..' segments are collapsed, so a token id of
        // "a/../../v1/other" drops the keys segment entirely, and '?' or '#' truncates the path into a query
        // or fragment. A real token id is hexadecimal and escaping leaves it untouched.
        var url = $"{options.ApiBaseAddress.TrimEnd('/')}/v1/turn/keys/{Uri.EscapeDataString(tokenId)}/credentials/generate-ice-servers";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new CloudflareCredentialRequest(options.TtlSeconds)),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        using var response = await client.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CloudflareIceServersResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Cloudflare returned an empty response when asked for ICE servers.");

        if (payload.IceServers is not { Length: > 0 })
        {
            throw new InvalidOperationException("Cloudflare returned no ICE servers.");
        }

        // Returned verbatim. Cloudflare answers with its own STUN entry plus a TURN entry spanning UDP, TCP,
        // and TLS on 443 -- the last of which is what gets a caller out of a network that allows nothing else.
        // Narrowing the list, or mixing in separately configured servers, is how a deployment ends up relaying
        // through a URL the credentials were never issued for.
        return [.. payload.IceServers.Select(static server => new WebRtcIceServer
        {
            Urls = server.Urls ?? [],
            Username = server.Username,
            Credential = server.Credential,
        })];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _optionsChangeRegistration?.Dispose();
        _gate.Dispose();
    }

    private sealed record CloudflareCredentialRequest(
        [property: JsonPropertyName("ttl")] int Ttl);

    private sealed record CloudflareIceServersResponse(
        [property: JsonPropertyName("iceServers")] CloudflareIceServer[] IceServers);

    private sealed record CloudflareIceServer(
        [property: JsonPropertyName("urls")] string[] Urls,
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("credential")] string Credential);
}
