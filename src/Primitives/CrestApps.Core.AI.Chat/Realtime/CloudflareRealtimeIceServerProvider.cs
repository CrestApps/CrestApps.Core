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

        // Both credentials are non-null and trimmed by the time this runs: CloudflareTurnOptions trims on
        // assignment, and the caller reached here only because IsConfigured rejected null and whitespace on
        // this very instance.
        //
        // Cloudflare's route spells the token as a key; the value is the Token ID from the dashboard.
        //
        // Escaped because it is interpolated into the path, where Uri gives several characters meaning that
        // silently retargets the request rather than failing: '..' segments are collapsed, so a token id of
        // "a/../../v1/other" drops the keys segment entirely, and '?' or '#' truncates the path into a query
        // or fragment. A real token id is hexadecimal and escaping leaves it untouched.
        var url = $"{options.ApiBaseAddress.TrimEnd('/')}/v1/turn/keys/{Uri.EscapeDataString(options.TokenId)}/credentials/generate-ice-servers";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new CloudflareCredentialRequest(options.TtlSeconds)),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken);

        using var response = await client.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CloudflareIceServersResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Cloudflare returned an empty response when asked for ICE servers.");

        if (payload.IceServers is not { Length: > 0 })
        {
            throw new InvalidOperationException("Cloudflare returned no ICE servers.");
        }

        var servers = payload.IceServers.Select(static server => new WebRtcIceServer
        {
            Urls = server.Urls ?? [],
            Username = server.Username,
            Credential = server.Credential,
        });

        return KeepOneUrlPerTransport(servers);
    }

    /// <summary>
    /// Reduces the issued URLs to one per scheme and transport, keeping the order Cloudflare listed them in.
    /// </summary>
    /// <param name="servers">The servers exactly as they were issued.</param>
    /// <remarks>
    /// <para>
    /// Cloudflare answers with a matrix rather than a list: every combination of STUN and TURN, UDP, TCP and
    /// TLS, each on both its standard port and a port chosen to slip through restrictive firewalls. Browsers
    /// count URLs and not entries when they decide how much work gathering will be, and Chrome warns past five
    /// of them because it gathers against all of them -- as does the SIPSorcery peer on this side, which is
    /// handed the same list and will allocate relays it never uses.
    /// </para>
    /// <para>
    /// Keeping one URL per scheme and transport leaves every route out of a network intact -- direct, relayed
    /// over UDP, over TCP, and over TLS -- while removing only the duplicate ports that offer another way to
    /// reach a relay already reachable. Which port survives is Cloudflare's choice rather than ours: the first
    /// it lists for a transport wins, so the order it considers preferable is the order that is honored.
    /// </para>
    /// <para>
    /// This narrows what Cloudflare issued; it never mixes in servers from elsewhere. The credentials are
    /// minted for the whole matrix, so every URL kept is one they were issued for. The configured-servers
    /// provider deliberately does no such thing: that list is written by hand, and every URL in it is a
    /// deployment's explicit choice rather than a generated permutation.
    /// </para>
    /// </remarks>
    private static List<WebRtcIceServer> KeepOneUrlPerTransport(IEnumerable<WebRtcIceServer> servers)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<WebRtcIceServer>();

        foreach (var server in servers)
        {
            // Filtered within the entry rather than across the whole response, so each URL stays attached to
            // the credentials it was issued with. An entry left with nothing is dropped: an ICE server with no
            // URL is not something to hand a browser.
            var urls = server.Urls.Where(url => !string.IsNullOrWhiteSpace(url) && seen.Add(TransportKey(url))).ToArray();

            if (urls.Length == 0)
            {
                continue;
            }

            kept.Add(new WebRtcIceServer
            {
                Urls = urls,
                Username = server.Username,
                Credential = server.Credential,
            });
        }

        // Every URL being unrecognisable is not a reason to hand back nothing -- that would drop the caller to
        // the configured servers, which for a Cloudflare deployment usually means no relay at all. Whatever was
        // issued is better than that.
        return kept.Count > 0 ? kept : [.. servers];
    }

    /// <summary>
    /// Identifies the route a URL represents, so two URLs that differ only by port collapse together.
    /// </summary>
    /// <param name="url">The ICE server URL.</param>
    /// <remarks>
    /// The transport is read from the query parameter RFC 7065 defines for it. Its absence is not ambiguity:
    /// the same RFC settles the default as UDP for <c>turn:</c> and TCP for <c>turns:</c>, which is what the
    /// browser will assume, so that is what is recorded here.
    /// </remarks>
    private static string TransportKey(string url)
    {
        var scheme = url.Split(':', 2)[0].Trim();

        var transportIndex = url.IndexOf("transport=", StringComparison.OrdinalIgnoreCase);

        if (transportIndex >= 0)
        {
            var transport = url[(transportIndex + "transport=".Length)..].Split('&', 2)[0].Trim();

            return $"{scheme}/{transport}".ToLowerInvariant();
        }

        var isTls = scheme.Equals("turns", StringComparison.OrdinalIgnoreCase) ||
                    scheme.Equals("stuns", StringComparison.OrdinalIgnoreCase);

        return $"{scheme}/{(isTls ? "tcp" : "udp")}".ToLowerInvariant();
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
