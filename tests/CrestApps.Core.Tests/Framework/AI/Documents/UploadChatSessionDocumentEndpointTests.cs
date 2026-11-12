using System.Reflection;
using System.Security.Claims;
using System.Text;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Security;
using CrestApps.Core.Filters;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI.Documents;

public sealed class UploadChatSessionDocumentEndpointTests
{
    [Fact]
    public async Task NewSession_WhenNotAuthorized_IsForbiddenAndNothingIsSaved()
    {
        var harness = new Harness { Authorized = false };

        var result = await harness.InvokeAsync(profileId: "profile-1");

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        harness.SessionManager.Verify(m => m.SaveAsync(It.IsAny<AIChatSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NewSession_WhenTheSessionStartLimiterThrottles_IsRejectedAndNothingIsSaved()
    {
        var harness = new Harness { Throttled = true };

        var result = await harness.InvokeAsync(profileId: "profile-1");

        Assert.Equal(StatusCodes.Status429TooManyRequests, StatusOf(result));
        harness.SessionManager.Verify(m => m.SaveAsync(It.IsAny<AIChatSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NewSession_WhenTheLimiterAllows_ConsultsTheLimiterOnceAndStartsTheSession()
    {
        var harness = new Harness();

        var result = await harness.InvokeAsync(profileId: "profile-1");

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        harness.Limiter.Verify(l => l.EvaluateAsync(It.IsAny<PromptSecurityContext>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.SessionManager.Verify(m => m.SaveAsync(It.IsAny<AIChatSession>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task UnknownSessionId_WithAProfile_IsTreatedAsNoSession()
    {
        var harness = new Harness();

        var result = await harness.InvokeAsync(sessionId: "does-not-exist", profileId: "profile-1");

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.True(IsNewSession(result));
        harness.Limiter.Verify(l => l.EvaluateAsync(It.IsAny<PromptSecurityContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnknownSessionId_WithoutAProfile_IsNotFound()
    {
        var harness = new Harness();

        var result = await harness.InvokeAsync(sessionId: "does-not-exist");

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        harness.SessionManager.Verify(m => m.SaveAsync(It.IsAny<AIChatSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnknownSessionId_WhenNotAuthorized_StillAuthorizesBeforeCreatingASession()
    {
        var harness = new Harness { Authorized = false };

        var result = await harness.InvokeAsync(sessionId: "does-not-exist", profileId: "profile-1");

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        harness.SessionManager.Verify(m => m.SaveAsync(It.IsAny<AIChatSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExistingSessionOfAnotherUser_IsNotFoundAndNothingIsSaved()
    {
        var harness = new Harness();
        harness.AddSession(new AIChatSession { SessionId = "session-1", ProfileId = "profile-1", UserId = "owner" });

        var result = await harness.InvokeAsync(sessionId: "session-1", userId: "intruder");

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        harness.SessionManager.Verify(m => m.SaveAsync(It.IsAny<AIChatSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExistingSessionOfTheCaller_IsStillAuthorizedAgainstTheResource()
    {
        var harness = new Harness { Authorized = false };
        harness.AddSession(new AIChatSession { SessionId = "session-1", ProfileId = "profile-1", UserId = "owner" });

        var result = await harness.InvokeAsync(sessionId: "session-1", userId: "owner");

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        harness.Authorization.Verify(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()), Times.Once);
    }

    [Fact]
    public async Task ExistingSessionOfTheCaller_DoesNotConsultTheSessionStartLimiter()
    {
        var harness = new Harness();
        harness.AddSession(new AIChatSession { SessionId = "session-1", ProfileId = "profile-1", UserId = "owner" });

        var result = await harness.InvokeAsync(sessionId: "session-1", userId: "owner");

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.False(IsNewSession(result));
        harness.Limiter.Verify(l => l.EvaluateAsync(It.IsAny<PromptSecurityContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Session_AtTheDocumentCap_RejectsFurtherDocuments()
    {
        var harness = new Harness();
        harness.Options.MaxDocumentsPerSession = 2;
        harness.AddSession(new AIChatSession
        {
            SessionId = "session-1",
            ProfileId = "profile-1",
            UserId = "owner",
            Documents =
            [
                new ChatDocumentInfo { DocumentId = "d1", FileName = "a.txt", FileSize = 1 },
                new ChatDocumentInfo { DocumentId = "d2", FileName = "b.txt", FileSize = 2 },
            ],
        });

        var result = await harness.InvokeAsync(sessionId: "session-1", userId: "owner");

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Equal(0, Count(result, "uploaded"));
        Assert.Equal(1, Count(result, "failed"));
        harness.FileScanner.Verify(s => s.ScanAsync(It.IsAny<IFormFile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TheDocumentCap_DefaultsToTwenty()
    {
        Assert.Equal(20, new ChatDocumentsOptions().MaxDocumentsPerSession);
    }

    [Fact]
    public void TheEndpointMethods_ReturnTheirRouteBuilderSoHostsCanAddFilters()
    {
        var app = WebApplication.CreateBuilder().Build();

        Assert.IsAssignableFrom<RouteHandlerBuilder>(app.AddUploadChatSessionDocumentEndpoint());
        Assert.IsAssignableFrom<RouteHandlerBuilder>(app.AddRemoveChatSessionDocumentEndpoint());
        Assert.IsAssignableFrom<RouteHandlerBuilder>(app.AddUploadChatInteractionDocumentEndpoint());
        Assert.IsAssignableFrom<RouteHandlerBuilder>(app.AddRemoveChatInteractionDocumentEndpoint());
        Assert.IsAssignableFrom<RouteHandlerBuilder>(app.AddDownloadAIDocumentEndpoint());
        Assert.IsAssignableFrom<RouteHandlerBuilder>(app.AddDownloadAIDocumentFigureEndpoint());
        Assert.IsAssignableFrom<RouteHandlerBuilder>(app.AddDownloadKnowledgeFigureEndpoint());
    }

    [Fact]
    public async Task Antiforgery_UnderCookieAuthentication_RejectsARequestWithoutAValidToken()
    {
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery
            .Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .ThrowsAsync(new AntiforgeryValidationException("missing"));

        var (result, nextCalled) = await RunAntiforgeryFilterAsync(authenticated: true, antiforgery.Object);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    [Fact]
    public async Task Antiforgery_UnderCookieAuthentication_AllowsARequestWithAValidToken()
    {
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery.Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

        var (_, nextCalled) = await RunAntiforgeryFilterAsync(authenticated: true, antiforgery.Object);

        Assert.True(nextCalled);
        antiforgery.Verify(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()), Times.Once);
    }

    [Fact]
    public async Task Antiforgery_ForABearerTokenRequest_IsNotRequired()
    {
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery
            .Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .ThrowsAsync(new AntiforgeryValidationException("missing"));

        var (_, nextCalled) = await RunAntiforgeryFilterAsync(authenticated: true, antiforgery.Object, authorizationHeader: "Bearer abc");

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Antiforgery_ForAnAnonymousRequest_IsNotRequired()
    {
        var antiforgery = new Mock<IAntiforgery>();
        antiforgery
            .Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .ThrowsAsync(new AntiforgeryValidationException("missing"));

        var (_, nextCalled) = await RunAntiforgeryFilterAsync(authenticated: false, antiforgery.Object);

        Assert.True(nextCalled);
    }

    private static async Task<(object Result, bool NextCalled)> RunAntiforgeryFilterAsync(
        bool authenticated,
        IAntiforgery antiforgery,
        string authorizationHeader = null)
    {
        var services = new ServiceCollection().AddSingleton(antiforgery).BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = authenticated
                ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "Identity.Application"))
                : new ClaimsPrincipal(new ClaimsIdentity()),
        };
        httpContext.Request.Method = HttpMethods.Post;

        if (authorizationHeader is not null)
        {
            httpContext.Request.Headers.Authorization = authorizationHeader;
        }

        var nextCalled = false;
        var filter = new CookieAuthenticationAntiforgeryEndpointFilter();
        var result = await filter.InvokeAsync(
            EndpointFilterInvocationContext.Create(httpContext),
            _ =>
            {
                nextCalled = true;

                return ValueTask.FromResult<object>(TypedResults.Ok());
            });

        return (result, nextCalled);
    }

    private static int StatusOf(object result)
    {
        return Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? StatusCodes.Status200OK;
    }

    private static bool IsNewSession(object result)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;

        return (bool)value.GetType().GetProperty("isNewSession").GetValue(value);
    }

    private static int Count(object result, string property)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;

        return ((System.Collections.IEnumerable)value.GetType().GetProperty(property).GetValue(value)).Cast<object>().Count();
    }

    private sealed class Harness
    {
        private readonly Dictionary<string, AIChatSession> _sessions = [];

        public Harness()
        {
            var profile = new AIProfile { ItemId = "profile-1", Type = AIProfileType.Chat };
            profile.Put(new AIProfileSessionDocumentsMetadata { AllowSessionDocuments = true });

            SessionManager = new Mock<IAIChatSessionManager>();
            SessionManager
                .Setup(m => m.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string id, CancellationToken _) => Task.FromResult(_sessions.GetValueOrDefault(id)));
            SessionManager
                .Setup(m => m.NewAsync(It.IsAny<AIProfile>(), It.IsAny<NewAIChatSessionContext>(), It.IsAny<CancellationToken>()))
                .Returns((AIProfile p, NewAIChatSessionContext _, CancellationToken _) => Task.FromResult(new AIChatSession { SessionId = "new-session", ProfileId = p.ItemId }));
            ProfileManager = new Mock<IAIProfileManager>();
            ProfileManager
                .Setup(m => m.FindByIdAsync("profile-1", It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AIProfile>(profile));
            Authorization = new Mock<IAuthorizationService>();
            Authorization
                .Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
                .Returns(() => Task.FromResult(Authorized ? AuthorizationResult.Success() : AuthorizationResult.Failed()));
            Limiter = new Mock<IChatSessionStartRateLimiter>();
            Limiter
                .Setup(l => l.EvaluateAsync(It.IsAny<PromptSecurityContext>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<RateLimitResult>(Throttled ? RateLimitResult.Throttled(30, 5, 5) : RateLimitResult.Allowed));
            FileScanner = new Mock<IUploadedFileScanner>();
            FileScanner
                .Setup(s => s.ScanAsync(It.IsAny<IFormFile>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(FileScanResult.Infected("test", "rejected by the test"));
        }

        public bool Authorized { get; init; } = true;

        public bool Throttled { get; init; }

        public ChatDocumentsOptions Options { get; } = new();

        public Mock<IAIChatSessionManager> SessionManager { get; }

        public Mock<IAIProfileManager> ProfileManager { get; }

        public Mock<IAuthorizationService> Authorization { get; }

        public Mock<IChatSessionStartRateLimiter> Limiter { get; }

        public Mock<IUploadedFileScanner> FileScanner { get; }

        public void AddSession(AIChatSession session)
        {
            _sessions[session.SessionId] = session;
        }

        public async Task<object> InvokeAsync(string sessionId = null, string profileId = null, string userId = "user-1")
        {
            var visitor = new Mock<IAIVisitorIdentityResolver>();
            visitor.Setup(r => r.Resolve()).Returns(new AIVisitorIdentity { VisitorId = "visitor-1" });
            var services = new ServiceCollection()
                .AddSingleton(Limiter.Object)
                .AddSingleton(visitor.Object)
                .BuildServiceProvider();
            var bytes = Encoding.UTF8.GetBytes("hello");
            var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", "c.txt");
            var form = new FormCollection(
                new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
                {
                    ["sessionId"] = sessionId ?? string.Empty,
                    ["profileId"] = profileId ?? string.Empty,
                },
                new FormFileCollection { file });
            var httpContext = new DefaultHttpContext
            {
                RequestServices = services,
                User = string.IsNullOrEmpty(userId)
                    ? new ClaimsPrincipal(new ClaimsIdentity())
                    : new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test")),
            };
            httpContext.Request.Method = HttpMethods.Post;
            httpContext.Request.ContentType = "multipart/form-data; boundary=x";
            httpContext.Request.Form = form;

            var localizer = new Mock<IStringLocalizer>();
            localizer.Setup(l => l[It.IsAny<string>()]).Returns((string name) => new LocalizedString(name, name));
            localizer.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()]).Returns((string name, object[] args) => new LocalizedString(name, string.Format(name, args)));
            var localizerFactory = new Mock<IStringLocalizerFactory>();
            localizerFactory.Setup(f => f.Create(It.IsAny<Type>())).Returns(localizer.Object);

            var endpointType = typeof(UploadChatSessionDocument).GetNestedType("UploadChatSessionDocumentEndpoint", BindingFlags.NonPublic);
            var handle = endpointType.GetMethod("HandleAsync", BindingFlags.Public | BindingFlags.Static);
            var task = (Task<IResult>)handle.Invoke(null,
            [
                httpContext.Request,
                SessionManager.Object,
                ProfileManager.Object,
                Mock.Of<IAIDeploymentManager>(),
                Mock.Of<IAIClientFactory>(),
                Mock.Of<IAIDocumentStore>(),
                Mock.Of<IAIDocumentChunkStore>(),
                Mock.Of<IDocumentFileStore>(),
                Mock.Of<IAIDocumentProcessingService>(),
                Mock.Of<IImageAnalysisService>(),
                TimeProvider.System,
                Authorization.Object,
                Array.Empty<IAIChatDocumentEventHandler>(),
                Microsoft.Extensions.Options.Options.Create(Options),
                NullLoggerFactory.Instance,
                localizerFactory.Object,
                FileScanner.Object,
            ]);

            return await task;
        }
    }
}
