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
                new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    MaxAge = options.CookieLifetime,
                    SameSite = SameSiteMode.Lax,
                    Secure = httpContext.Request.IsHttps,
                });
        }

        return visitorId;
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
