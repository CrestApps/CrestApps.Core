using System.Text;
using CrestApps.Core.AI.Copilot.Models;
using CrestApps.Core.AI.Copilot.Services;
using CrestApps.Core.Tests.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class GitHubOAuthServiceStateTests
{
    private const string CallbackUrl = "https://app.example.com/oauth/callback";
    private const string ReturnUrl = "/dashboard";
    private const string StateCookieName = ".crestapps.gh-oauth-state";
    private const string HostStateCookieName = "__Host-crestapps-gh-oauth-state";

    [Fact]
    public void TryValidateCallbackState_Roundtrip_ReturnsTrueAndRecoversReturnUrl()
    {
        var time = new TestTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var (service, accessor) = CreateService(time);

        accessor.HttpContext = NewHttpContext();
        var url = service.GetAuthorizationUrl(CallbackUrl, ReturnUrl);

        var nonce = ExtractStateParameter(url);
        var protectedState = CapturedStateCookieValue(accessor.HttpContext);

        Assert.NotNull(protectedState);
        Assert.NotEmpty(nonce);

        // Simulate the GitHub callback with the state parameter and the cookie.
        accessor.HttpContext = NewHttpContext(stateCookieValue: protectedState);

        var ok = service.TryValidateCallbackState(nonce, out var recoveredReturnUrl);

        Assert.True(ok);
        Assert.Equal(ReturnUrl, recoveredReturnUrl);
    }

    [Fact]
    public void TryValidateCallbackState_AfterASuccessfulValidation_DeletesTheStateCookie()
    {
        var time = new TestTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var (service, accessor) = CreateService(time);

        accessor.HttpContext = NewHttpContext();
        var nonce = ExtractStateParameter(service.GetAuthorizationUrl(CallbackUrl, ReturnUrl));
        var protectedState = CapturedStateCookieValue(accessor.HttpContext);

        var callback = NewHttpContext(stateCookieValue: protectedState);
        accessor.HttpContext = callback;

        Assert.True(service.TryValidateCallbackState(nonce, out _));

        // The cookie is one use: left in place, the same state could be replayed until it expired.
        var deletion = Assert.Single(callback.Response.Headers.SetCookie, header => header.StartsWith($"{StateCookieName}=", StringComparison.Ordinal));
        Assert.Contains("expires=Thu, 01 Jan 1970", deletion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidateCallbackState_AfterAFailedValidation_LeavesTheStateCookie()
    {
        var time = new TestTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var (service, accessor) = CreateService(time);

        accessor.HttpContext = NewHttpContext();
        service.GetAuthorizationUrl(CallbackUrl, ReturnUrl);
        var callback = NewHttpContext(stateCookieValue: CapturedStateCookieValue(accessor.HttpContext));
        accessor.HttpContext = callback;

        Assert.False(service.TryValidateCallbackState("an-attacker-supplied-nonce", out _));
        Assert.Equal(0, callback.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void TryValidateCallbackState_ForAnotherUser_ReturnsFalse()
    {
        // A state cookie planted from a sibling subdomain is valid for whoever it was issued to; it must not link the
        // attacker's account to the user who follows the attacker's callback link.
        var time = new TestTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var (service, accessor) = CreateService(time);

        accessor.HttpContext = NewHttpContext(userId: "attacker");
        var nonce = ExtractStateParameter(service.GetAuthorizationUrl(CallbackUrl, ReturnUrl));
        var protectedState = CapturedStateCookieValue(accessor.HttpContext);

        accessor.HttpContext = NewHttpContext(stateCookieValue: protectedState, userId: "victim");

        Assert.False(service.TryValidateCallbackState(nonce, out var returnUrl));
        Assert.Null(returnUrl);
    }

    [Fact]
    public void TryValidateCallbackState_ForTheSameUser_ReturnsTrue()
    {
        var time = new TestTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var (service, accessor) = CreateService(time);

        accessor.HttpContext = NewHttpContext(userId: "alice");
        var nonce = ExtractStateParameter(service.GetAuthorizationUrl(CallbackUrl, ReturnUrl));
        var protectedState = CapturedStateCookieValue(accessor.HttpContext);

        accessor.HttpContext = NewHttpContext(stateCookieValue: protectedState, userId: "alice");

        Assert.True(service.TryValidateCallbackState(nonce, out var returnUrl));
        Assert.Equal(ReturnUrl, returnUrl);
    }

    [Fact]
    public void TryValidateCallbackState_ForAUserWhoWasAnonymousWhenTheFlowStarted_ReturnsFalse()
    {
        var time = new TestTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var (service, accessor) = CreateService(time);

        accessor.HttpContext = NewHttpContext();
        var nonce = ExtractStateParameter(service.GetAuthorizationUrl(CallbackUrl, ReturnUrl));
        var protectedState = CapturedStateCookieValue(accessor.HttpContext);

        accessor.HttpContext = NewHttpContext(stateCookieValue: protectedState, userId: "victim");

        Assert.False(service.TryValidateCallbackState(nonce, out _));
    }

    [Fact]
    public void GetAuthorizationUrl_OverHttps_WritesAHostPrefixedCookieThatSiblingSubdomainsCannotSet()
    {
        var time = new TestTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var (service, accessor) = CreateService(time);

        accessor.HttpContext = NewHttpContext(https: true, userId: "alice");
        var nonce = ExtractStateParameter(service.GetAuthorizationUrl(CallbackUrl, ReturnUrl));

        var setCookie = Assert.Single(accessor.HttpContext.Response.Headers.SetCookie);
        Assert.StartsWith($"{HostStateCookieName}=", setCookie, StringComparison.Ordinal);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);

        var protectedState = CapturedStateCookieValue(accessor.HttpContext, HostStateCookieName);
        accessor.HttpContext = NewHttpContext(stateCookieValue: protectedState, userId: "alice", https: true);

        Assert.True(service.TryValidateCallbackState(nonce, out _));
        Assert.Contains(accessor.HttpContext.Response.Headers.SetCookie, header => header.StartsWith($"{HostStateCookieName}=", StringComparison.Ordinal));
    }

    [Fact]
    public void GetAuthorizationUrl_WithoutAnHttpContext_Throws()
    {
        // The state it would issue is the return URL itself, which TryValidateCallbackState can never accept.
        var (service, accessor) = CreateService(new TestTimeProvider(DateTimeOffset.UtcNow));
        accessor.HttpContext = null;

        Assert.Throws<InvalidOperationException>(() => service.GetAuthorizationUrl(CallbackUrl, ReturnUrl));
    }

    [Fact]
    public void GetAuthorizationUrl_IssuesAnUnguessableNonceEachTime()
    {
        var (service, accessor) = CreateService(new TestTimeProvider(DateTimeOffset.UtcNow));

        accessor.HttpContext = NewHttpContext();
        var first = ExtractStateParameter(service.GetAuthorizationUrl(CallbackUrl, ReturnUrl));
        accessor.HttpContext = NewHttpContext();
        var second = ExtractStateParameter(service.GetAuthorizationUrl(CallbackUrl, ReturnUrl));

        Assert.NotEqual(first, second);
        Assert.True(first.Length >= 32);
    }

    [Theory]
    [InlineData("__popup__")]
    [InlineData("/dashboard")]
    public void TryValidateCallbackState_WithoutAnHttpContext_Fails(string state)
    {
        var (service, accessor) = CreateService(new TestTimeProvider(DateTimeOffset.UtcNow));
        accessor.HttpContext = null;

        var ok = service.TryValidateCallbackState(state, out var returnUrl);

        Assert.False(ok);
        Assert.Null(returnUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryValidateCallbackState_MissingStateParameter_ReturnsFalse(string state)
    {
        var (service, accessor) = CreateService(new TestTimeProvider(DateTimeOffset.UtcNow));
        accessor.HttpContext = NewHttpContext();

        var ok = service.TryValidateCallbackState(state, out var returnUrl);

        Assert.False(ok);
        Assert.Null(returnUrl);
    }

    [Fact]
    public void TryValidateCallbackState_MissingCookie_ReturnsFalse()
    {
        var (service, accessor) = CreateService(new TestTimeProvider(DateTimeOffset.UtcNow));
        accessor.HttpContext = NewHttpContext();

        var ok = service.TryValidateCallbackState("some-nonce", out var returnUrl);

        Assert.False(ok);
        Assert.Null(returnUrl);
    }

    [Fact]
    public void TryValidateCallbackState_TamperedCookie_ReturnsFalse()
    {
        var time = new TestTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var (service, accessor) = CreateService(time);

        accessor.HttpContext = NewHttpContext();
        var url = service.GetAuthorizationUrl(CallbackUrl, ReturnUrl);
        var nonce = ExtractStateParameter(url);
        var protectedState = CapturedStateCookieValue(accessor.HttpContext);

        // Flip a character to corrupt the protected payload.
        var tampered = TamperString(protectedState);
        Assert.NotEqual(protectedState, tampered);

        accessor.HttpContext = NewHttpContext(stateCookieValue: tampered);

        var ok = service.TryValidateCallbackState(nonce, out var returnUrl);

        Assert.False(ok);
        Assert.Null(returnUrl);
    }

    [Fact]
    public void TryValidateCallbackState_NonceMismatch_ReturnsFalse()
    {
        var time = new TestTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var (service, accessor) = CreateService(time);

        accessor.HttpContext = NewHttpContext();
        service.GetAuthorizationUrl(CallbackUrl, ReturnUrl);
        var protectedState = CapturedStateCookieValue(accessor.HttpContext);

        accessor.HttpContext = NewHttpContext(stateCookieValue: protectedState);

        var ok = service.TryValidateCallbackState("an-attacker-supplied-nonce", out var returnUrl);

        Assert.False(ok);
        Assert.Null(returnUrl);
    }

    [Fact]
    public void TryValidateCallbackState_ExpiredCookie_ReturnsFalse()
    {
        var time = new TestTimeProvider(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var (service, accessor) = CreateService(time);

        accessor.HttpContext = NewHttpContext();
        var url = service.GetAuthorizationUrl(CallbackUrl, ReturnUrl);
        var nonce = ExtractStateParameter(url);
        var protectedState = CapturedStateCookieValue(accessor.HttpContext);

        // Advance past the 10-minute state lifetime.
        time.Advance(TimeSpan.FromMinutes(11));

        accessor.HttpContext = NewHttpContext(stateCookieValue: protectedState);

        var ok = service.TryValidateCallbackState(nonce, out var returnUrl);

        Assert.False(ok);
        Assert.Null(returnUrl);
    }

    private static (GitHubOAuthService service, TestHttpContextAccessor accessor) CreateService(TimeProvider time)
    {
        var dpp = new EphemeralDataProtectionProvider();
        var options = new TestOptionsMonitor<CopilotOptions>
        {
            CurrentValue = new CopilotOptions
            {
                ClientId = "test-client-id",
                ClientSecret = "test-client-secret",
                Scopes = ["user:email", "read:org"],
            },
        };

        var accessor = new TestHttpContextAccessor();
        var service = new GitHubOAuthService(
            credentialStore: null,
            dataProtectionProvider: dpp,
            options: options,
            httpClientFactory: null,
            timeProvider: time,
            httpContextAccessor: accessor,
            logger: NullLogger<GitHubOAuthService>.Instance);

        return (service, accessor);
    }

    private static DefaultHttpContext NewHttpContext(string stateCookieValue = null, string userId = null, bool https = false)
    {
        var context = new DefaultHttpContext();

        if (https)
        {
            context.Request.Scheme = "https";
            context.Request.IsHttps = true;
        }

        if (userId is not null)
        {
            context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId)], "Test"));
        }

        if (stateCookieValue is not null)
        {
            context.Request.Headers.Cookie = $"{(https ? HostStateCookieName : StateCookieName)}={stateCookieValue}";
        }

        return context;
    }

    private static string CapturedStateCookieValue(HttpContext context, string cookieName = StateCookieName)
    {
        // ASP.NET Core writes Set-Cookie headers to Response.Headers.
        var setCookies = context.Response.Headers.SetCookie;

        foreach (var raw in setCookies)
        {
            if (raw is null)
            {
                continue;
            }

            var prefix = $"{cookieName}=";
            var idx = raw.IndexOf(prefix, StringComparison.Ordinal);

            if (idx < 0)
            {
                continue;
            }

            var start = idx + prefix.Length;
            var end = raw.IndexOf(';', start);
            return end < 0 ? raw[start..] : raw[start..end];
        }

        return null;
    }

    private static string ExtractStateParameter(string url)
    {
        var marker = "&state=";
        var idx = url.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"state parameter missing from URL: {url}");
        var start = idx + marker.Length;
        var end = url.IndexOf('&', start);
        var raw = end < 0 ? url[start..] : url[start..end];

        return Uri.UnescapeDataString(raw);
    }

    private static string TamperString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        // Flip a byte in the middle of the payload to invalidate the signature.
        bytes[bytes.Length / 2] ^= 0x01;
        return Encoding.UTF8.GetString(bytes);
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext HttpContext { get; set; }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
