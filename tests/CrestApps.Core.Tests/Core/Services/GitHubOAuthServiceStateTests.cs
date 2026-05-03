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

    private static DefaultHttpContext NewHttpContext(string stateCookieValue = null)
    {
        var context = new DefaultHttpContext();

        if (stateCookieValue is not null)
        {
            context.Request.Headers.Cookie = $"{StateCookieName}={stateCookieValue}";
        }

        return context;
    }

    private static string CapturedStateCookieValue(HttpContext context)
    {
        // ASP.NET Core writes Set-Cookie headers to Response.Headers.
        var setCookies = context.Response.Headers.SetCookie;

        foreach (var raw in setCookies)
        {
            if (raw is null)
            {
                continue;
            }

            var prefix = $"{StateCookieName}=";
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
