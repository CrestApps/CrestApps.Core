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
/// Resolves stable visitor identities for AI chat requests.
/// </summary>
public sealed class DefaultAIVisitorIdentityResolver : IAIVisitorIdentityResolver
{
    private const string RemoteAddressProtectionPurpose = "CrestApps.Core.AI.VisitorIdentity.RemoteAddress";

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

    private string ResolveAnonymousVisitorId(HttpContext httpContext)
    {
        var options = _options.Value;

        if (httpContext.Request.Cookies.TryGetValue(options.CookieName, out var cookieValue) &&
            !string.IsNullOrWhiteSpace(cookieValue))
        {
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

        return visitorId;
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

        var remoteAddress = ResolveRemoteAddress(httpContext);

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

    private static string ResolveRemoteAddress(HttpContext httpContext)
    {
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].ToString();

        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var forwardedAddress = forwardedFor.Split(',')[0].Trim();

            if (!string.IsNullOrWhiteSpace(forwardedAddress))
            {
                return forwardedAddress;
            }
        }

        return httpContext.Connection.RemoteIpAddress?.ToString();
    }
}
