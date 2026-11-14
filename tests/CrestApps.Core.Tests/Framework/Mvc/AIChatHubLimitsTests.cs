using System.Security.Claims;
using System.Text.Json;
using AGUI.Abstractions;
using CrestApps.Core.AI;
using CrestApps.Core.AI.AGUI;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.AGUI;
using CrestApps.Core.AI.Chat.Hubs;
using CrestApps.Core.AI.Chat.Security;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.AI.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.Mvc;

/// <summary>
/// The hub knows who is calling from the connection's own request, because that is the one thing every transport hands a
/// hub invocation, and a message of any kind that reaches a provider counts against the caller's message limit.
/// </summary>
public sealed class AIChatHubLimitsTests
{
    private static readonly AIProfile ChatProfile = new() { ItemId = "profile-1", Type = AIProfileType.Chat };

    [Fact]
    public async Task StartSession_WhenTheTransportGivesTheInvocationNoHttpContext_StillKeysTheLimiterOnTheVisitor()
    {
        // Long polling answers each invocation on its own request, so the accessor has no HttpContext when the hub method
        // runs. The connection's HttpContext is still there, and the visitor and the network address come from it.
        var accessor = new HttpContextAccessor();
        var limiter = CapturingSessionStartLimiter(out var seen);
        var services = Services(accessor, sessionStartLimiter: limiter);
        var (hub, _) = CreateHub(services, connectionContext: ConnectionContext("visitor-abc", "203.0.113.9"));

        await hub.StartSession(ChatProfile.ItemId);

        var context = Assert.Single(seen);
        Assert.Equal("visitor-abc", context.VisitorId);

        // The default mode keeps only a hash of the address, which is what the limiters key the network address on.
        Assert.False(string.IsNullOrEmpty(context.RemoteAddressHash));
    }

    [Fact]
    public async Task StartSession_LeavesNoHttpContextBehindOnTheAccessor()
    {
        var accessor = new HttpContextAccessor();
        var services = Services(accessor, sessionStartLimiter: CapturingSessionStartLimiter(out _));
        var (hub, _) = CreateHub(services, connectionContext: ConnectionContext("visitor-abc", "203.0.113.9"));

        await hub.StartSession(ChatProfile.ItemId);

        // The accessor is shared by every connection, so what one invocation set must not be seen by the next.
        Assert.Null(accessor.HttpContext);
    }

    [Fact]
    public async Task StartSession_WhenTheTransportDoesGiveTheInvocationAnHttpContext_UsesThatOne()
    {
        // WebSockets keep the connection's request as the ambient one, and nothing is replaced.
        var ambient = ConnectionContext("visitor-ambient", "198.51.100.7");
        var accessor = new HttpContextAccessor { HttpContext = ambient };
        var limiter = CapturingSessionStartLimiter(out var seen);
        var services = Services(accessor, sessionStartLimiter: limiter);
        var (hub, _) = CreateHub(services, connectionContext: ConnectionContext("visitor-other", "203.0.113.9"));

        await hub.StartSession(ChatProfile.ItemId);

        Assert.Equal("visitor-ambient", Assert.Single(seen).VisitorId);
        Assert.Same(ambient, accessor.HttpContext);
    }

    public static TheoryData<string> SpeechMethods =>
    [
        nameof(AIChatHubCore<IAIChatHubClient>.SendAudioStream),
        nameof(AIChatHubCore<IAIChatHubClient>.SynthesizeSpeech),
        nameof(AIChatHubCore<IAIChatHubClient>.StartConversation),
        nameof(AIChatHubCore<IAIChatHubClient>.StartRealtimeConversation),
        nameof(AIChatHubCore<IAIChatHubClient>.StartRealtimeWebRtc),
    ];

    [Theory]
    [MemberData(nameof(SpeechMethods))]
    public async Task SpeechMethods_WhenTheMessageLimitIsExceeded_StopBeforeAnyProviderIsCalled(string method)
    {
        var clientFactory = new Mock<IAIClientFactory>();
        var deployments = new Mock<IAIDeploymentManager>();
        var services = Services(new HttpContextAccessor(), messageLimiter: ThrottledMessageLimiter(), clientFactory: clientFactory.Object, deployments: deployments.Object);
        var (hub, caller) = CreateHub(services);

        await InvokeAsync(hub, method);

        caller.Verify(c => c.ReceiveError(It.Is<string>(message => message.Contains("Rate limit exceeded", StringComparison.Ordinal))), Times.Once);
        clientFactory.VerifyNoOtherCalls();
        deployments.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(SpeechMethods))]
    public async Task SpeechMethods_CountOnMessageLimitOnce(string method)
    {
        var limiter = new Mock<IChatRateLimiter>();
        limiter
            .Setup(l => l.EvaluateAsync(It.IsAny<PromptSecurityContext>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<RateLimitResult>(RateLimitResult.Throttled(7, 5, 5)));
        var services = Services(new HttpContextAccessor(), messageLimiter: limiter.Object);
        var (hub, _) = CreateHub(services);

        await InvokeAsync(hub, method);

        limiter.Verify(l => l.EvaluateAsync(It.Is<PromptSecurityContext>(c => c.ProfileId == ChatProfile.ItemId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResumingAnAGUIRun_WhenTheMessageLimitIsExceeded_StopsBeforeTheModelIsCalled()
    {
        var session = new AIChatSession { SessionId = "session-1", ProfileId = ChatProfile.ItemId, UserId = "user-1" };
        var paused = new AIChatSessionPrompt { ItemId = "prompt-1", SessionId = "session-1", Role = ChatRole.Assistant, Content = "needs approval" };
        paused.Put(new ChatTurnToolActivity
        {
            ToolCalls = [new ChatTurnToolCall { CallId = "call-1", Name = "tool", Status = ChatTurnToolCallStatus.AwaitingApproval, ApprovalRequestId = "approval-1" }],
        });
        var promptStore = PromptStore([paused]);
        var services = Services(new HttpContextAccessor(), messageLimiter: ThrottledMessageLimiter(), session: session, promptStore: promptStore.Object);
        var (hub, caller) = CreateHub(services);
        var events = new List<BaseEvent>();

        await hub.ResumeForTestAsync(CreateTurn("session-1", events), services);

        caller.Verify(c => c.ReceiveError(It.Is<string>(message => message.Contains("Rate limit exceeded", StringComparison.Ordinal))), Times.Once);
        Assert.DoesNotContain(events, e => e is RunStartedEvent);
        promptStore.Verify(s => s.UpdateAsync(It.IsAny<AIChatSessionPrompt>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ContinuingAnAGUIRun_WhenTheMessageLimitIsExceeded_StopsAndLeavesTheTurnContinuable()
    {
        var session = new AIChatSession { SessionId = "session-1", ProfileId = ChatProfile.ItemId, UserId = "user-1" };
        var last = new AIChatSessionPrompt { ItemId = "prompt-1", SessionId = "session-1", Role = ChatRole.Assistant, Content = "opened a page" };
        var activity = new ChatTurnToolActivity { ContinuesOnNextPage = true };
        last.Put(activity);
        var promptStore = PromptStore([last]);
        var services = Services(new HttpContextAccessor(), messageLimiter: ThrottledMessageLimiter(), session: session, promptStore: promptStore.Object);
        var (hub, caller) = CreateHub(services);

        await hub.ContinueForTestAsync(CreateTurn("session-1", []), services);

        caller.Verify(c => c.ReceiveError(It.Is<string>(message => message.Contains("Rate limit exceeded", StringComparison.Ordinal))), Times.Once);

        // The turn was not consumed, so the user can continue once the limit has passed.
        Assert.True(activity.ContinuesOnNextPage);
        promptStore.Verify(s => s.UpdateAsync(It.IsAny<AIChatSessionPrompt>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task InvokeAsync(TestHub hub, string method)
    {
        IAsyncEnumerable<string> audio = EmptyAudio();

        switch (method)
        {
            case nameof(AIChatHubCore<IAIChatHubClient>.SendAudioStream):
                await hub.SendAudioStream(ChatProfile.ItemId, "session-1", audio);
                break;
            case nameof(AIChatHubCore<IAIChatHubClient>.SynthesizeSpeech):
                await hub.SynthesizeSpeech(ChatProfile.ItemId, "session-1", "hello");
                break;
            case nameof(AIChatHubCore<IAIChatHubClient>.StartConversation):
                await hub.StartConversation(ChatProfile.ItemId, "session-1", audio);
                break;
            case nameof(AIChatHubCore<IAIChatHubClient>.StartRealtimeConversation):
                await hub.StartRealtimeConversation(ChatProfile.ItemId, "session-1", audio);
                break;
            case nameof(AIChatHubCore<IAIChatHubClient>.StartRealtimeWebRtc):
                await hub.StartRealtimeWebRtc(ChatProfile.ItemId, "session-1", "v=0");
                break;
            default:
                throw new InvalidOperationException(method);
        }
    }

    private static async IAsyncEnumerable<string> EmptyAudio()
    {
        await Task.CompletedTask;

        yield break;
    }

    private static IChatRateLimiter ThrottledMessageLimiter()
    {
        var limiter = new Mock<IChatRateLimiter>();
        limiter
            .Setup(l => l.EvaluateAsync(It.IsAny<PromptSecurityContext>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<RateLimitResult>(RateLimitResult.Throttled(7, 5, 5)));

        return limiter.Object;
    }

    private static IChatSessionStartRateLimiter CapturingSessionStartLimiter(out List<PromptSecurityContext> seen)
    {
        var captured = new List<PromptSecurityContext>();
        var limiter = new Mock<IChatSessionStartRateLimiter>();
        limiter
            .Setup(l => l.EvaluateAsync(It.IsAny<PromptSecurityContext>(), It.IsAny<CancellationToken>()))
            .Returns((PromptSecurityContext context, CancellationToken _) =>
            {
                captured.Add(context);

                return new ValueTask<RateLimitResult>(RateLimitResult.Throttled(94, 10, 10));
            });
        seen = captured;

        return limiter.Object;
    }

    private static Mock<IAIChatSessionPromptStore> PromptStore(IReadOnlyList<AIChatSessionPrompt> prompts)
    {
        var store = new Mock<IAIChatSessionPromptStore>();
        store.Setup(s => s.GetPromptsAsync(It.IsAny<string>())).ReturnsAsync(prompts);

        return store;
    }

    private static ServiceProvider Services(
        HttpContextAccessor accessor,
        IChatRateLimiter messageLimiter = null,
        IChatSessionStartRateLimiter sessionStartLimiter = null,
        IAIClientFactory clientFactory = null,
        IAIDeploymentManager deployments = null,
        AIChatSession session = null,
        IAIChatSessionPromptStore promptStore = null)
    {
        var profileManager = new Mock<IAIProfileManager>();
        profileManager
            .Setup(m => m.FindByIdAsync(ChatProfile.ItemId, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<AIProfile>(ChatProfile));
        var sessionManager = new Mock<IAIChatSessionManager>();
        sessionManager
            .Setup(m => m.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var hostEnvironment = new Mock<IHostEnvironment>();
        hostEnvironment.SetupGet(e => e.ApplicationName).Returns("test");

        var collection = new ServiceCollection()
            .AddSingleton<IHttpContextAccessor>(accessor)
            .AddSingleton(Options.Create(new AIVisitorIdentityOptions()))
            .AddSingleton(hostEnvironment.Object)
            .AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider())
            .AddSingleton<IAIVisitorIdentityResolver, DefaultAIVisitorIdentityResolver>()
            .AddSingleton(profileManager.Object)
            .AddSingleton(sessionManager.Object)
            .AddSingleton(promptStore ?? Mock.Of<IAIChatSessionPromptStore>())
            .AddSingleton(clientFactory ?? Mock.Of<IAIClientFactory>())
            .AddSingleton(deployments ?? Mock.Of<IAIDeploymentManager>())
            .AddSingleton(Mock.Of<IWebRtcRealtimePeerFactory>());

        if (messageLimiter is not null)
        {
            collection.AddSingleton(messageLimiter);
        }

        if (sessionStartLimiter is not null)
        {
            collection.AddSingleton(sessionStartLimiter);
        }

        return collection.BuildServiceProvider();
    }

    private static DefaultHttpContext ConnectionContext(string visitorId, string remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"crestapps-ai-visitor={visitorId}";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteAddress);

        return context;
    }

    private static (TestHub Hub, Mock<IAIChatHubClient> Caller) CreateHub(IServiceProvider services, HttpContext connectionContext = null)
    {
        var caller = new Mock<IAIChatHubClient>();
        var clients = new Mock<IHubCallerClients<IAIChatHubClient>>();
        clients.SetupGet(c => c.Caller).Returns(caller.Object);

        var features = new FeatureCollection();

        if (connectionContext is not null)
        {
            features.Set<IHttpContextFeature>(new ConnectionHttpContextFeature { HttpContext = connectionContext });
        }

        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns("connection");
        context.SetupGet(c => c.ConnectionAborted).Returns(CancellationToken.None);
        context.SetupGet(c => c.Features).Returns(features);
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "Test")));

        return (new TestHub(services) { Clients = clients.Object, Context = context.Object, Groups = Mock.Of<IGroupManager>() }, caller);
    }

    private static AGUIChatTurn CreateTurn(string threadId, List<BaseEvent> events)
    {
        var request = AGUIRunRequest.From(AGUIJson.DeserializeInput(JsonElement.Parse(
            $"{{ \"threadId\": \"{threadId}\", \"runId\": \"r\", \"messages\": [], \"tools\": [], \"context\": [], \"forwardedProps\": {{}} }}")));
        var writer = new AGUIRunEventWriter(request.ThreadId, request.RunId, (evt, _) =>
        {
            events.Add(evt);

            return ValueTask.CompletedTask;
        });

        return new AGUIChatTurn(request, writer);
    }

    private sealed class ConnectionHttpContextFeature : IHttpContextFeature
    {
        public HttpContext HttpContext { get; set; }
    }

    private sealed class TestHub : AIChatHubCore<IAIChatHubClient>
    {
        public TestHub(IServiceProvider services)
            : base(services, TimeProvider.System, NullLogger.Instance)
        {
        }

        public Task ResumeForTestAsync(AGUIChatTurn turn, IServiceProvider services)
        {
            return ResumeAGUIRunAsync(turn, services, CancellationToken.None);
        }

        public Task ContinueForTestAsync(AGUIChatTurn turn, IServiceProvider services)
        {
            return ContinueAGUIRunAsync(turn, services, CancellationToken.None);
        }

        protected override Task<bool> AuthorizeProfileAsync(IServiceProvider services, AIProfile profile)
        {
            return Task.FromResult(true);
        }

        protected override Task<bool> AuthorizeSessionAsync(IServiceProvider services, AIChatSession chatSession)
        {
            return Task.FromResult(true);
        }
    }
}
