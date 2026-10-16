using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.ResponseHandling;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class AIChatResponseHandlerTests
{
    [Fact]
    public async Task HandleAsync_ChatInteraction_SetsTheInteractionOnTheScopeBeforeTheBuild()
    {
        // Profile providers and orchestration handlers that run during the build read the chat from the scope, for
        // example to offer an agent only in a given chat.
        var interaction = new ChatInteraction { ItemId = "interaction-1" };
        var builder = new RecordingContextBuilder();
        using var scope = AIInvocationScope.Begin();

        await new AIChatResponseHandler().HandleAsync(CreateContext(builder, ChatContextType.ChatInteraction, interaction: interaction), TestContext.Current.CancellationToken);

        Assert.Same(interaction, builder.ChatInteractionDuringBuild);
        Assert.Same(interaction, AIInvocationScope.Current.ChatInteraction);
        Assert.Same(builder.Context.CompletionContext, AIInvocationScope.Current.CompletionContext);
        Assert.Equal("ds-1", AIInvocationScope.Current.DataSourceId);
    }

    [Fact]
    public async Task HandleAsync_ChatSession_SetsTheSessionOnTheScopeBeforeTheBuild()
    {
        var session = new AIChatSession { SessionId = "session-1" };
        var builder = new RecordingContextBuilder();
        using var scope = AIInvocationScope.Begin();

        await new AIChatResponseHandler().HandleAsync(CreateContext(builder, ChatContextType.AIChatSession, session: session), TestContext.Current.CancellationToken);

        Assert.Same(session, builder.ChatSessionDuringBuild);
        Assert.Same(session, builder.SessionItemDuringBuild);
        Assert.Same(session, AIInvocationScope.Current.ChatSession);
        Assert.Same(builder.Context.CompletionContext, AIInvocationScope.Current.CompletionContext);
        Assert.Equal("ds-1", AIInvocationScope.Current.DataSourceId);
    }

    private static ChatResponseHandlerContext CreateContext(
        RecordingContextBuilder builder,
        ChatContextType chatType,
        ChatInteraction interaction = null,
        AIChatSession session = null)
    {
        var orchestrator = new Mock<IOrchestrator>();
        orchestrator
            .Setup(o => o.ExecuteStreamingAsync(It.IsAny<OrchestrationContext>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<ChatResponseUpdate>());

        var resolver = new Mock<IOrchestratorResolver>();
        resolver.Setup(r => r.Resolve(It.IsAny<string>())).Returns(orchestrator.Object);

        var services = new ServiceCollection()
            .AddSingleton<IOrchestrationContextBuilder>(builder)
            .AddSingleton(resolver.Object)
            .BuildServiceProvider();

        return new ChatResponseHandlerContext
        {
            Prompt = "Hello",
            ConnectionId = "connection-1",
            SessionId = "session-1",
            ChatType = chatType,
            ConversationHistory = [],
            Services = services,
            Profile = new AIProfile(),
            ChatSession = session,
            Interaction = interaction,
        };
    }

    private sealed class RecordingContextBuilder : IOrchestrationContextBuilder
    {
        public OrchestrationContext Context { get; } = new()
        {
            CompletionContext = new AICompletionContext { DataSourceId = "ds-1" },
        };

        public ChatInteraction ChatInteractionDuringBuild { get; private set; }

        public AIChatSession ChatSessionDuringBuild { get; private set; }

        public object SessionItemDuringBuild { get; private set; }

        public ValueTask<OrchestrationContext> BuildAsync(object resource, Action<OrchestrationContext> configure = null, CancellationToken cancellationToken = default)
        {
            var invocation = AIInvocationScope.Current;

            ChatInteractionDuringBuild = invocation.ChatInteraction;
            ChatSessionDuringBuild = invocation.ChatSession;
            SessionItemDuringBuild = invocation.Items.TryGetValue(nameof(AIChatSession), out var item) ? item : null;

            configure?.Invoke(Context);

            return ValueTask.FromResult(Context);
        }
    }
}
