using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CrestApps.Core.AI.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Chat.Security;

/// <summary>
/// Resolves stable visitor identities for AI chat requests. The remote address is the connection's address, never the
/// raw <c>X-Forwarded-For</c> header; a host behind a reverse proxy must enable ASP.NET Core's forwarded headers
/// middleware with its trusted proxies so the connection carries the client's address.
/// </summary>
public sealed class DefaultAIVisitorIdentityResolver : IAIVisitorIdentityResolver
{
    private const string RemoteAddressProtectionPurpose = "CrestApps.Core.AI.VisitorIdentity.RemoteAddress";

    private const string UnknownRemoteAddress = "unknown";

    // Chrome only honours this on a cookie that is already SameSite=None and Secure. Every other browser
    // ignores an attribute it does not know.
    private const string PartitionedAttributeName = "Partitioned";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptions<AIVisitorIdentityOptions> _options;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IDataProtector _remoteAddressProtector;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIVisitorIdentityResolver"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    /// <param name="options">The visitor identity options.</param>
    /// <param name="hostEnvironment">The host environment.</param>
    public DefaultAIVisitorIdentityResolver(
        IHttpContextAccessor httpContextAccessor,
        IOptions<AIVisitorIdentityOptions> options,
        IHostEnvironment hostEnvironment,
        IDataProtectionProvider dataProtectionProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options;
        _hostEnvironment = hostEnvironment;
        _remoteAddressProtector = dataProtectionProvider.CreateProtector(
            _hostEnvironment.ApplicationName,
            RemoteAddressProtectionPurpose);
    }

    /// <summary>
    /// Resolves the current visitor identity for the active request context.
    /// </summary>
    /// <returns>The resolved visitor identity.</returns>
    public AIVisitorIdentity Resolve()
    {
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext is null)
        {
            return new AIVisitorIdentity();
        }

        var userId = httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? httpContext.User?.Identity?.Name;
        var isAuthenticated = !string.IsNullOrWhiteSpace(userId);

        return new AIVisitorIdentity
        {
            VisitorId = isAuthenticated ? userId : ResolveAnonymousVisitorId(httpContext),
            IsAuthenticated = isAuthenticated,
            RemoteAddress = ResolveStoredRemoteAddress(httpContext),
            RemoteAddressHash = ResolveRemoteAddressHash(httpContext),
        };
    }

    // Resolving twice in one request (the session manager stamps the owner, an endpoint checks it) must give one visitor:
    // a new id is generated and written as a cookie each time none is found, and the second would replace the first.
    private const string ResolvedVisitorIdItemKey = "CrestApps.Core.AI.VisitorIdentity.VisitorId";

    // The value goes into an indexed column and into a cookie the client chooses, so only something that looks like an id is reused.
    private const int MaxVisitorIdLength = 64;

    private string ResolveAnonymousVisitorId(HttpContext httpContext)
    {
        var options = _options.Value;

        if (httpContext.Items.TryGetValue(ResolvedVisitorIdItemKey, out var resolved) && resolved is string resolvedId)
        {
            return resolvedId;
        }

        if (httpContext.Request.Cookies.TryGetValue(options.CookieName, out var cookieValue) && IsVisitorId(cookieValue))
        {
            httpContext.Items[ResolvedVisitorIdItemKey] = cookieValue;

            return cookieValue;
        }

        var visitorId = UniqueId.GenerateId();

        if (!httpContext.Response.HasStarted)
        {
            httpContext.Response.Cookies.Append(
                options.CookieName,
                visitorId,
                CreateCookieOptions(httpContext, options));
        }

        httpContext.Items[ResolvedVisitorIdItemKey] = visitorId;

        return visitorId;
    }

    private static bool IsVisitorId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxVisitorIdLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static CookieOptions CreateCookieOptions(HttpContext httpContext, AIVisitorIdentityOptions options)
    {
        var isHttps = httpContext.Request.IsHttps;

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            MaxAge = options.CookieLifetime,
            SameSite = SameSiteMode.Lax,
            Secure = isHttps,
        };

        // SameSite=Lax is the right default: it is what a browser keeps for a chat served from its own
        // site. A chat embedded in a frame on another site is the exception, and there the browser
        // refuses a Lax cookie outright, so SameSite=None is the only attribute that survives.
        //
        // SameSite=None is only legal together with Secure, and a Secure cookie never reaches a plain
        // HTTP page, so a request that did not arrive over HTTPS keeps the Lax cookie. Losing the cookie
        // is worse than keeping one the frame cannot read.
        if (options.AllowCrossSiteEmbedding && isHttps)
        {
            cookieOptions.SameSite = SameSiteMode.None;

            // Keeps the framed cookie in its own jar, so it cannot replace the first party cookie of the
            // same name and downgrade that one to SameSite=None as well.
            if (options.UsePartitionedCookie)
            {
                cookieOptions.Extensions.Add(PartitionedAttributeName);
            }
        }

        return cookieOptions;
    }

    private string ResolveRemoteAddressHash(HttpContext httpContext)
    {
        var options = _options.Value;

        if (options.RemoteAddressMode != AIVisitorRemoteAddressMode.Hashed &&
            options.RemoteAddressMode != AIVisitorRemoteAddressMode.Encrypted)
        {
            return null;
        }

        var remoteAddress = ResolveRemoteAddressKey(httpContext);

        if (string.IsNullOrWhiteSpace(remoteAddress))
        {
            return null;
        }

        var payload = $"{_hostEnvironment.ApplicationName}|{options.RemoteAddressHashSalt}|{remoteAddress.Trim()}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));

        return Convert.ToHexString(hashBytes);
    }

    private string ResolveStoredRemoteAddress(HttpContext httpContext)
    {
        var remoteAddress = ResolveRemoteAddress(httpContext);

        if (string.IsNullOrWhiteSpace(remoteAddress))
        {
            return null;
        }

        var normalizedRemoteAddress = remoteAddress.Trim();

        return _options.Value.RemoteAddressMode switch
        {
            AIVisitorRemoteAddressMode.PlainText => normalizedRemoteAddress,
            AIVisitorRemoteAddressMode.Encrypted => _remoteAddressProtector.Protect(normalizedRemoteAddress),
            _ => null,
        };
    }

    // The raw X-Forwarded-For header is never read: any client can send it, so trusting it lets a caller pick the
    // address the rate limiters and the abuse controls key on. A host behind a reverse proxy enables ASP.NET Core's
    // forwarded headers middleware with its known proxies or networks, which replaces the connection's remote
    // address with the forwarded one only when the request came from a trusted proxy.
    private static string ResolveRemoteAddress(HttpContext httpContext)
    {
        var address = httpContext.Connection.RemoteIpAddress;

        // No connection address (a Unix socket, a named pipe) would leave only keys the client can rotate, so the
        // limiters would fail open. Every such request shares one bucket instead.
        if (address is null)
        {
            return UnknownRemoteAddress;
        }

        // ::ffff:a.b.c.d and a.b.c.d are one client.
        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    // What the limiters key on. An IPv6 client usually holds a whole /64, so a client that rotates inside it would get a
    // fresh allowance for every address; the key is the /64.
    private static string ResolveRemoteAddressKey(HttpContext httpContext)
    {
        var address = httpContext.Connection.RemoteIpAddress;

        if (address is null || address.IsIPv4MappedToIPv6 || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return ResolveRemoteAddress(httpContext);
        }

        var bytes = address.GetAddressBytes();

        Array.Clear(bytes, 8, 8);

        return new System.Net.IPAddress(bytes) + "/64";
    }
}
