using System.Security.Claims;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tools;
using CrestApps.Core.Services;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Services;
using CrestApps.Core.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Memory;

public sealed class ConversationMemoryOrchestrationHandlerTests
{
    [Fact]
    public async Task BuiltAsync_WhenSessionMemoryIsOff_ChangesNothing()
    {
        var chatClient = new RecordingChatClient("unused");
        var handler = CreateHandler(chatClient);
        var (context, session) = CreateContext(CreateProfile(sessionMemory: false), messageCount: 20, window: 4);
        using var scope = AIInvocationScope.Begin();

        await handler.BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, context.OrchestrationContext.SystemMessageBuilder.ToString());
        Assert.False(session.TryGet<AIConversationMemory>(out _));
        Assert.Empty(chatClient.Calls);
    }

    [Fact]
    public async Task BuiltAsync_WhenTheConversationFitsTheWindow_ChangesNothing()
    {
        var chatClient = new RecordingChatClient("unused");
        var handler = CreateHandler(chatClient);
        var (context, _) = CreateContext(CreateProfile(), messageCount: 4, window: 10);
        using var scope = AIInvocationScope.Begin();

        await handler.BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, context.OrchestrationContext.SystemMessageBuilder.ToString());
        Assert.Empty(chatClient.Calls);
    }

    [Fact]
    public async Task BuiltAsync_WhenEnoughMessagesFallOutOfTheWindow_SummarizesThemOntoTheSession()
    {
        var chatClient = new RecordingChatClient("The user's project budget is 40000 dollars.");
        var handler = CreateHandler(chatClient);

        // 13 messages, a window of 4: the 9 oldest fall out of what the model is sent.
        var (context, session) = CreateContext(CreateProfile(), messageCount: 13, window: 4);
        using var scope = AIInvocationScope.Begin();

        await handler.BuiltAsync(context, TestContext.Current.CancellationToken);

        var call = Assert.Single(chatClient.Calls);
        var input = call.Last().Text;
        Assert.Contains("User: message-0 Our project budget is 40000 dollars.", input);
        Assert.Contains("message-8", input);
        Assert.DoesNotContain("message-9", input);

        Assert.True(session.TryGet<AIConversationMemory>(out var memory));
        Assert.Equal(9, memory.SummarizedMessageCount);
        Assert.Equal("The user's project budget is 40000 dollars.", memory.Summary);

        var systemMessage = context.OrchestrationContext.SystemMessageBuilder.ToString();
        Assert.Contains("summary=The user's project budget is 40000 dollars.", systemMessage);
        Assert.Contains("pending=0", systemMessage);
        Assert.Contains($"tool={SearchConversationTool.TheName}", systemMessage);
        Assert.Contains(SearchConversationTool.TheName, context.OrchestrationContext.MustIncludeTools);
        Assert.True((bool)context.OrchestrationContext.CompletionContext.AdditionalProperties[AICompletionContextKeys.HasConversationMemory]);

        // The search tool reads exactly the messages that fell out, from this invocation only.
        var older = Assert.IsType<List<ChatMessage>>(AIInvocationScope.Current.Items[MemoryConstants.CompletionContextKeys.OlderConversationMessages]);
        Assert.Equal(9, older.Count);
    }

    [Fact]
    public async Task BuiltAsync_WhenFewMessagesFellOut_ShowsThemVerbatimWithoutCallingTheModel()
    {
        var chatClient = new RecordingChatClient("unused");
        var handler = CreateHandler(chatClient);
        var (context, session) = CreateContext(CreateProfile(), messageCount: 14, window: 10);
        using var scope = AIInvocationScope.Begin();

        await handler.BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(chatClient.Calls);
        Assert.False(session.TryGet<AIConversationMemory>(out _));
        var systemMessage = context.OrchestrationContext.SystemMessageBuilder.ToString();
        Assert.Contains("pending=4", systemMessage);
        Assert.Contains("User:message-0 Our project budget is 40000 dollars.", systemMessage);
    }

    [Fact]
    public async Task BuiltAsync_RecallsAnOldTurnThatBearsOnTheQuestion()
    {
        var chatClient = new RecordingChatClient("A summary.");
        var handler = CreateHandler(chatClient);
        var (context, _) = CreateContext(CreateProfile(), messageCount: 21, window: 4, currentMessage: "What budget in dollars did I give you?");
        using var scope = AIInvocationScope.Begin();

        await handler.BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.Contains("recalled=1", context.OrchestrationContext.SystemMessageBuilder.ToString());
    }

    [Fact]
    public async Task BuiltAsync_WhenTheHistoryShrankBelowTheSummary_StartsTheSummaryAgain()
    {
        var chatClient = new RecordingChatClient("A fresh summary.");
        var handler = CreateHandler(chatClient);
        var (context, session) = CreateContext(CreateProfile(), messageCount: 11, window: 4);
        session.Put(new AIConversationMemory { Summary = "About a conversation that was cleared.", SummarizedMessageCount = 50 });
        using var scope = AIInvocationScope.Begin();

        await handler.BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("cleared", Assert.Single(chatClient.Calls).Last().Text);
        Assert.True(session.TryGet<AIConversationMemory>(out var memory));
        Assert.Equal(7, memory.SummarizedMessageCount);
        Assert.Equal("A fresh summary.", memory.Summary);
    }

    [Fact]
    public async Task BuiltAsync_WhenSummarizingFails_KeepsThePreviousSummaryAndShowsTheRestVerbatim()
    {
        var chatClient = new RecordingChatClient(null) { Throw = true };
        var handler = CreateHandler(chatClient);
        var (context, session) = CreateContext(CreateProfile(), messageCount: 13, window: 4);
        session.Put(new AIConversationMemory { Summary = "Older summary.", SummarizedMessageCount = 2 });
        using var scope = AIInvocationScope.Begin();

        await handler.BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.True(session.TryGet<AIConversationMemory>(out var memory));
        Assert.Equal(2, memory.SummarizedMessageCount);
        var systemMessage = context.OrchestrationContext.SystemMessageBuilder.ToString();
        Assert.Contains("summary=Older summary.", systemMessage);
        Assert.Contains("pending=7", systemMessage);
    }

    [Fact]
    public async Task BuiltAsync_ForAChatInteraction_SavesTheSummaryOnTheInteraction()
    {
        var chatClient = new RecordingChatClient("Interaction summary.");
        var interactionManager = new Mock<ICatalogManager<ChatInteraction>>();
        var handler = CreateHandler(chatClient, interactionManager: interactionManager.Object);
        var interaction = new ChatInteraction { ItemId = "interaction-1" };
        var (context, _) = CreateContext(interaction, messageCount: 13, window: 4);
        using var scope = AIInvocationScope.Begin();

        await handler.BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.True(interaction.TryGet<AIConversationMemory>(out var memory));
        Assert.Equal("Interaction summary.", memory.Summary);
        interactionManager.Verify(manager => manager.UpdateAsync(interaction, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuiltAsync_WithToolsDisabled_ShowsTheMemoryButExposesNoTool()
    {
        var handler = CreateHandler(new RecordingChatClient("A summary."));
        var (context, _) = CreateContext(CreateProfile(), messageCount: 13, window: 4);
        context.OrchestrationContext.DisableTools = true;
        using var scope = AIInvocationScope.Begin();

        await handler.BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.Contains("summary=A summary.", context.OrchestrationContext.SystemMessageBuilder.ToString());
        Assert.Empty(context.OrchestrationContext.MustIncludeTools);
        Assert.False(AIInvocationScope.Current.Items.ContainsKey(MemoryConstants.CompletionContextKeys.OlderConversationMessages));
    }

    [Fact]
    public async Task BuiltAsync_PastConversations_ScopesTheToolsToTheSignedInUserAndProfile()
    {
        var handler = CreateHandler(new RecordingChatClient("unused"), userId: "user-1");
        var (context, session) = CreateContext(CreateProfile(sessionMemory: false, pastConversations: true), messageCount: 1, window: 10);
        using var scope = AIInvocationScope.Begin();

        await handler.BuiltAsync(context, TestContext.Current.CancellationToken);

        var pastScope = Assert.IsType<PastConversationsScope>(AIInvocationScope.Current.Items[MemoryConstants.CompletionContextKeys.PastConversationsScope]);
        Assert.Equal(new PastConversationsScope("user-1", "profile-1", session.SessionId), pastScope);
        Assert.Contains(SearchPastConversationsTool.TheName, context.OrchestrationContext.MustIncludeTools);
        Assert.Contains(ListRecentConversationsTool.TheName, context.OrchestrationContext.MustIncludeTools);
        Assert.Contains("[PAST]", context.OrchestrationContext.SystemMessageBuilder.ToString());
    }

    [Fact]
    public async Task BuiltAsync_PastConversations_AreNotOfferedToAnonymousVisitors()
    {
        var handler = CreateHandler(new RecordingChatClient("unused"), userId: null);
        var (context, _) = CreateContext(CreateProfile(sessionMemory: false, pastConversations: true), messageCount: 1, window: 10);
        using var scope = AIInvocationScope.Begin();

        await handler.BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.False(AIInvocationScope.Current.Items.ContainsKey(MemoryConstants.CompletionContextKeys.PastConversationsScope));
        Assert.Empty(context.OrchestrationContext.MustIncludeTools);
        Assert.Equal(string.Empty, context.OrchestrationContext.SystemMessageBuilder.ToString());
    }

    private static AIProfile CreateProfile(bool sessionMemory = true, bool pastConversations = false)
    {
        var profile = new AIProfile { ItemId = "profile-1" };
        profile.Put(new MemoryMetadata
        {
            EnableSessionMemory = sessionMemory,
            EnablePastConversations = pastConversations,
        });

        return profile;
    }

    private static (OrchestrationContextBuiltContext Context, AIChatSession Session) CreateContext(
        object resource,
        int messageCount,
        int window,
        string currentMessage = "What should we do next?")
    {
        var history = new List<ChatMessage>();

        for (var index = 0; index < messageCount - 1; index++)
        {
            var text = index == 0 ? "message-0 Our project budget is 40000 dollars." : $"message-{index} filler text {index}";
            history.Add(new ChatMessage(index % 2 == 0 ? ChatRole.User : ChatRole.Assistant, text));
        }

        history.Add(new ChatMessage(ChatRole.User, currentMessage));

        var session = new AIChatSession { SessionId = "session-1", ProfileId = "profile-1", UserId = "user-1" };
        var completionContext = new AICompletionContext { PastMessagesCount = window };
        completionContext.AdditionalProperties[AICompletionContextKeys.Session] = session;

        var orchestration = new OrchestrationContext
        {
            UserMessage = currentMessage,
            ConversationHistory = history,
            CompletionContext = completionContext,
        };

        return (new OrchestrationContextBuiltContext(resource, orchestration), session);
    }

    private static ConversationMemoryOrchestrationHandler CreateHandler(
        RecordingChatClient chatClient,
        string userId = "user-1",
        ICatalogManager<ChatInteraction> interactionManager = null)
    {
        var deploymentManager = new Mock<IAIDeploymentManager>();
        deploymentManager
            .Setup(manager => manager.ResolveSlotAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIDeployment { ItemId = "utility", ConnectionName = "default", ModelName = "utility-model" });

        var clientFactory = new Mock<IAIClientFactory>();
        clientFactory
            .Setup(factory => factory.CreateChatClientAsync(It.IsAny<AIDeployment>(), It.IsAny<Action<ChatClientBuilder>>()))
            .Returns(new ValueTask<IChatClient>(chatClient));

        var services = new ServiceCollection();

        if (interactionManager is not null)
        {
            services.AddSingleton(interactionManager);
        }

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = string.IsNullOrEmpty(userId)
                    ? new ClaimsPrincipal(new ClaimsIdentity())
                    : new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test")),
            },
        };

        return new ConversationMemoryOrchestrationHandler(
            deploymentManager.Object,
            clientFactory.Object,
            new FakeTemplateService(),
            new LuceneTextTokenizer(),
            httpContextAccessor,
            new TestOptionsMonitor<ChatInteractionMemoryOptions> { CurrentValue = new ChatInteractionMemoryOptions() },
            services.BuildServiceProvider(),
            TimeProvider.System,
            NullLogger<ConversationMemoryOrchestrationHandler>.Instance);
    }

    private sealed class FakeTemplateService : ITemplateService
    {
        public Task<IReadOnlyList<Template>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Template>>([]);

        public Task<Template> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<Template>(null);

        public Task<string> RenderAsync(string id, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
        {
            if (id == MemoryConstants.TemplateIds.ConversationMemoryContext)
            {
                var pending = ((IEnumerable<IDictionary<string, object>>)arguments["pendingMessages"])
                    .Select(message => $"{message["Role"]}:{message["Text"]}")
                    .ToList();
                var recalled = ((IEnumerable<IDictionary<string, object>>)arguments["recalledTurns"]).Count();
                arguments.TryGetValue("searchToolName", out var tool);

                return Task.FromResult($"summary={arguments["summary"]}\npending={pending.Count}\n{string.Join('\n', pending)}\nrecalled={recalled}\ntool={tool}");
            }

            if (id == MemoryConstants.TemplateIds.PastConversationsAvailability)
            {
                return Task.FromResult("[PAST]");
            }

            return Task.FromResult("SUMMARIZE");
        }

        public Task<string> MergeAsync(IEnumerable<string> ids, IDictionary<string, object> arguments = null, string separator = "\n\n", CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }

    internal sealed class RecordingChatClient : IChatClient
    {
        private readonly string _reply;

        public RecordingChatClient(string reply)
        {
            _reply = reply;
        }

        public bool Throw { get; init; }

        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.ToList());

            if (Throw)
            {
                throw new InvalidOperationException("The utility model is unavailable.");
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _reply)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
