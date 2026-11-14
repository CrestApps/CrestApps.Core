using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Security;
using CrestApps.Core.Filters;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI.Documents;

public sealed class CookieAuthenticationAntiforgeryEndpointFilterTests
{
    [Fact]
    public async Task ACookieAuthenticatedRequest_WithoutAValidToken_IsRejected()
    {
        var (result, nextCalled) = await RunAsync(Identity("Identity.Application"), RejectingAntiforgery());

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task ACookieAuthenticatedRequest_WithAValidToken_IsAllowed()
    {
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery.Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

        var (_, nextCalled) = await RunAsync(Identity("Identity.Application"), antiforgery.Object);

        Assert.True(nextCalled);
        antiforgery.Verify(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()), Times.Once);
    }

    [Fact]
    public async Task AnAuthorizationHeaderDoesNotExemptACookieAuthenticatedRequest()
    {
        // Any value will do: the header says nothing about which scheme authenticated the request.
        var (_, nextCalled) = await RunAsync(Identity("Identity.Application"), RejectingAntiforgery(), authorizationHeader: "Bearer junk");

        Assert.False(nextCalled);
    }

    [Theory]
    [InlineData("Negotiate")]
    [InlineData("NTLM")]
    [InlineData("Basic")]
    public async Task ABrowserAttachedAuthorizationHeaderDoesNotExemptTheRequest(string authenticationType)
    {
        var (_, nextCalled) = await RunAsync(Identity(authenticationType), RejectingAntiforgery(), authorizationHeader: "Negotiate abc");

        Assert.False(nextCalled);
    }

    [Theory]
    [InlineData("Bearer")]
    [InlineData("ApiKey")]
    [InlineData("OpenIddict.Validation.AspNetCore")]
    public async Task ARequestAuthenticatedByATokenTheClientSendsOnPurpose_NeedsNoAntiforgeryToken(string authenticationType)
    {
        var (_, nextCalled) = await RunAsync(Identity(authenticationType), RejectingAntiforgery(), authorizationHeader: "Bearer abc");

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task ARequestWithATokenIdentityAndACookieIdentity_IsValidated()
    {
        var principal = new ClaimsPrincipal(
        [
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "a")], "Bearer"),
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "a")], "Identity.Application"),
        ]);

        var (_, nextCalled) = await RunAsync(principal, RejectingAntiforgery());

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task AnAnonymousRequest_WithTheVisitorCookie_IsValidated()
    {
        var options = new AntiforgeryEndpointFilterOptions();
        options.AmbientCookieNames.Add("crestapps-ai-visitor");

        var (_, nextCalled) = await RunAsync(new ClaimsPrincipal(new ClaimsIdentity()), RejectingAntiforgery(), cookie: "crestapps-ai-visitor=abc", options: options);

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task AnAnonymousRequest_WithoutAnyAmbientCookie_NeedsNoToken()
    {
        var options = new AntiforgeryEndpointFilterOptions();
        options.AmbientCookieNames.Add("crestapps-ai-visitor");

        var (_, nextCalled) = await RunAsync(new ClaimsPrincipal(new ClaimsIdentity()), RejectingAntiforgery(), options: options);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task ASafeMethod_IsNeverValidated()
    {
        var (_, nextCalled) = await RunAsync(Identity("Identity.Application"), RejectingAntiforgery(), method: HttpMethods.Get);

        Assert.True(nextCalled);
    }

    [Fact]
    public void TheDocumentRegistration_AddsAntiforgeryAndNamesTheVisitorCookie()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.Configure<AIVisitorIdentityOptions>(options => options.CookieName = "my-visitor");
        CrestApps.Core.AI.Documents.ServiceCollectionExtensions.AddCoreAIDocumentProcessing(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IAntiforgery>());
        Assert.Contains("my-visitor", provider.GetRequiredService<IOptions<AntiforgeryEndpointFilterOptions>>().Value.AmbientCookieNames);
    }

    [Theory]
    [InlineData("/ai/chat-sessions/upload-document")]
    [InlineData("/ai/chat-sessions/remove-document")]
    [InlineData("/ai/chat-interactions/upload-document")]
    [InlineData("/ai/chat-interactions/remove-document")]
    public async Task EachDocumentUploadAndRemoveEndpoint_RejectsACookieAuthenticatedRequestWithoutAToken(string route)
    {
        await using var app = await StartAppAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Test-User", "alice");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "junk");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("antiforgery", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<WebApplication> StartAppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, CookieLikeTestHandler>("Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery();
        builder.Services.AddSingleton(Mock.Of<IStoreCommitter>());

        var app = builder.Build();
        app.UseAuthentication();
        app.AddUploadChatSessionDocumentEndpoint();
        app.AddRemoveChatSessionDocumentEndpoint();
        app.AddUploadChatInteractionDocumentEndpoint();
        app.AddRemoveChatInteractionDocumentEndpoint();

        await app.StartAsync(TestContext.Current.CancellationToken);

        return app;
    }

    // Authenticates any request that names a user, as an identity with the type a cookie scheme produces.
    private sealed class CookieLikeTestHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public CookieLikeTestHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], "Identity.Application"));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        }
    }

    private static ClaimsPrincipal Identity(string authenticationType)
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], authenticationType));
    }

    private static IAntiforgery RejectingAntiforgery()
    {
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery
            .Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .ThrowsAsync(new AntiforgeryValidationException("missing"));

        return antiforgery.Object;
    }

    private static async Task<(object Result, bool NextCalled)> RunAsync(
        ClaimsPrincipal user,
        IAntiforgery antiforgery,
        string authorizationHeader = null,
        string cookie = null,
        string method = null,
        AntiforgeryEndpointFilterOptions options = null)
    {
        var services = new ServiceCollection().AddSingleton(antiforgery).BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services, User = user };
        httpContext.Request.Method = method ?? HttpMethods.Post;

        if (authorizationHeader is not null)
        {
            httpContext.Request.Headers.Authorization = authorizationHeader;
        }

        if (cookie is not null)
        {
            httpContext.Request.Headers.Cookie = cookie;
        }

        var nextCalled = false;
        var filter = new CookieAuthenticationAntiforgeryEndpointFilter(Options.Create(options ?? new AntiforgeryEndpointFilterOptions()));
        var result = await filter.InvokeAsync(
            EndpointFilterInvocationContext.Create(httpContext),
            _ =>
            {
                nextCalled = true;

                return ValueTask.FromResult<object>(TypedResults.Ok());
            });

        return (result, nextCalled);
    }
}
