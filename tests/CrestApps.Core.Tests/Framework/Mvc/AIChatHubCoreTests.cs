using System.Threading.Channels;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Hubs;
using CrestApps.Core.AI.Chat.Models;
using CrestApps.Core.AI.Exceptions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.ResponseHandling;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class AIChatHubCoreTests
{
    [Fact]
    public async Task SaveChatSessionAsync_SavesSessionAndCommits()
    {
        var sessionManager = new TestAIChatSessionManager();

        var committer = new TestStoreCommitter();

        var services = new ServiceCollection();
        services.AddSingleton<IStoreCommitter>(committer);
        var serviceProvider = services.BuildServiceProvider();

        var chatSession = new AIChatSession
        {
            SessionId = "session-1",
            Documents =
            [
                new ChatDocumentInfo
                {
                    DocumentId = "doc-1",
                    FileName = "brief.pdf",
                    ContentType = "application/pdf",
                    FileSize = 42,
                },
            ],
        };

        var hub = new TestAIChatHub(serviceProvider);

        await hub.SaveChatSessionForTestAsync(sessionManager, chatSession);

        Assert.Same(chatSession, sessionManager.SavedSession);
        Assert.Single(chatSession.Documents);
        Assert.Equal("doc-1", chatSession.Documents[0].DocumentId);
        Assert.True(committer.WasCommitted);
    }

    [Theory]
    [InlineData(ChatSessionStatus.Closed, true)]
    [InlineData(ChatSessionStatus.Abandoned, true)]
    [InlineData(ChatSessionStatus.Active, false)]
    public void IsEndedStatus_ReturnsExpectedValue(ChatSessionStatus status, bool expected)
    {
        Assert.Equal(expected, TestAIChatHub.IsEndedStatusForTest(status));
    }

    [Fact]
    public void GetFriendlyErrorMessage_WithInvalidChatModelSettings_ReturnsProfileGuidance()
    {
        var hub = new TestAIChatHub(new ServiceCollection().BuildServiceProvider());

        var message = hub.GetFriendlyErrorMessageForTest(new AIDeploymentNotFoundException("Unable to resolve a chat deployment for the profile."));

        Assert.Equal("The chat model settings are missing or invalid. Update the Chat model in the AI Profile or the global AI settings.", message);
    }

    /// <summary>
    /// Verifies prompt persistence, group membership, and handler dispatch retain the caller's
    /// cancellation token around conversation-history construction.
    /// </summary>
    [Fact]
    public async Task ProcessChatPromptAsync_PropagatesCancellationTokenAroundHistoryConstruction()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var profile = new AIProfile
        {
            ItemId = "profile",
        };
        var chatSession = new AIChatSession
        {
            SessionId = "session",
            ProfileId = profile.ItemId,
            Title = "Existing title",
            Status = ChatSessionStatus.Active,
        };
        var sessionManagerMock = new Mock<IAIChatSessionManager>();
        sessionManagerMock
            .Setup(manager => manager.SaveAsync(chatSession, default))
            .Returns(Task.CompletedTask);
        var promptStoreMock = new Mock<IAIChatSessionPromptStore>();
        promptStoreMock
            .Setup(store => store.CreateAsync(
                It.Is<AIChatSessionPrompt>(prompt => prompt.ItemId == "new-prompt"),
                cancellationToken))
            .Returns(ValueTask.CompletedTask);
        promptStoreMock
            .Setup(store => store.GetPromptsAsync(chatSession.SessionId))
            .ReturnsAsync([]);
        ChatResponseHandlerContext handlerContext = null;
        var handlerMock = new Mock<IChatResponseHandler>();
        handlerMock
            .Setup(handler => handler.HandleAsync(
                It.IsAny<ChatResponseHandlerContext>(),
                cancellationToken))
            .Callback<ChatResponseHandlerContext, CancellationToken>(
                (context, _) => handlerContext = context)
            .ReturnsAsync(ChatResponseHandlerResult.Deferred());
        var handlerResolverMock = new Mock<IChatResponseHandlerResolver>();
        handlerResolverMock
            .Setup(resolver => resolver.Resolve(null, ChatMode.TextInput))
            .Returns(handlerMock.Object);
        var services = new ServiceCollection()
            .AddSingleton(sessionManagerMock.Object)
            .AddSingleton(promptStoreMock.Object)
            .AddSingleton(handlerResolverMock.Object)
            .BuildServiceProvider();
        var contextMock = new Mock<HubCallerContext>();
        contextMock.SetupGet(context => context.ConnectionId).Returns("connection");
        contextMock.SetupGet(context => context.ConnectionAborted).Returns(cancellationToken);
        var groupsMock = new Mock<IGroupManager>();
        groupsMock
            .Setup(groups => groups.AddToGroupAsync(
                "connection",
                AIChatHubCore<IAIChatHubClient>.GetSessionGroupName(chatSession.SessionId),
                cancellationToken))
            .Returns(Task.CompletedTask);
        var hub = new TestAIChatHub(services, chatSession)
        {
            Context = contextMock.Object,
            Groups = groupsMock.Object,
        };
        var channel = Channel.CreateUnbounded<CompletionPartialMessage>();

        await hub.ProcessChatPromptForTestAsync(
            channel.Writer,
            services,
            profile,
            chatSession.SessionId,
            "prompt",
            cancellationToken);

        Assert.NotNull(handlerContext);
        var historyMessage = Assert.Single(handlerContext.ConversationHistory);
        Assert.Equal(ChatRole.User, historyMessage.Role);
        Assert.Equal("prompt", historyMessage.Text);
        promptStoreMock.Verify(
            store => store.CreateAsync(It.IsAny<AIChatSessionPrompt>(), cancellationToken),
            Times.Once);
        groupsMock.Verify(
            groups => groups.AddToGroupAsync(
                "connection",
                AIChatHubCore<IAIChatHubClient>.GetSessionGroupName(chatSession.SessionId),
                cancellationToken),
            Times.Exactly(2));
        handlerMock.Verify(
            handler => handler.HandleAsync(
                It.IsAny<ChatResponseHandlerContext>(),
                cancellationToken),
            Times.Once);
    }

    private sealed class TestAIChatHub : AIChatHubCore<IAIChatHubClient>
    {
        private readonly AIChatSession _chatSession;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestAIChatHub"/> class.
        /// </summary>
        /// <param name="services">The service provider.</param>
        /// <param name="chatSession">The optional chat session returned by the test override.</param>
        public TestAIChatHub(
            IServiceProvider services,
            AIChatSession chatSession = null)
            : base(services, TimeProvider.System, NullLogger.Instance)
        {
            _chatSession = chatSession;
        }

        public Task SaveChatSessionForTestAsync(IAIChatSessionManager sessionManager, AIChatSession chatSession)
        {
            return SaveChatSessionAsync(sessionManager, chatSession);
        }

        public string GetFriendlyErrorMessageForTest(Exception ex)
        {
            return GetFriendlyErrorMessage(ex);
        }

        /// <summary>
        /// Invokes chat prompt processing for tests.
        /// </summary>
        /// <param name="writer">The output channel writer.</param>
        /// <param name="services">The service provider.</param>
        /// <param name="profile">The AI profile.</param>
        /// <param name="sessionId">The session identifier.</param>
        /// <param name="prompt">The prompt text.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing prompt processing.</returns>
        public Task ProcessChatPromptForTestAsync(
            ChannelWriter<CompletionPartialMessage> writer,
            IServiceProvider services,
            AIProfile profile,
            string sessionId,
            string prompt,
            CancellationToken cancellationToken)
        {
            return ProcessChatPromptAsync(
                writer,
                services,
                profile,
                sessionId,
                prompt,
                cancellationToken);
        }

        public static bool IsEndedStatusForTest(ChatSessionStatus status)
        {
            var method = typeof(AIChatHubCore<IAIChatHubClient>).GetMethod(
                "IsEndedStatus",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.NotNull(method);

            return (bool)method.Invoke(null, [status]);
        }

        /// <inheritdoc />
        protected override string GenerateId()
        {
            return "new-prompt";
        }

        /// <inheritdoc />
        protected override Task<(AIChatSession ChatSession, bool IsNewSession)> GetOrCreateSessionAsync(
            IServiceProvider services,
            string sessionId,
            AIProfile profile,
            string userPrompt)
        {
            if (_chatSession is null)
            {
                return base.GetOrCreateSessionAsync(
                    services,
                    sessionId,
                    profile,
                    userPrompt);
            }

            return Task.FromResult((_chatSession, false));
        }
    }

    private sealed class TestStoreCommitter : IStoreCommitter
    {
        public bool WasCommitted { get; private set; }

        public ValueTask CommitAsync(CancellationToken cancellationToken = default)
        {
            WasCommitted = true;

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestAIChatSessionManager : IAIChatSessionManager
    {
        public AIChatSession SavedSession { get; private set; }

        public Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int> DeleteAllAsync(string profileId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AIChatSession> FindAsync(string id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AIChatSession> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AIChatSession> NewAsync(AIProfile profile, NewAIChatSessionContext context, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AIChatSessionResult> PageAsync(int page, int pageSize, AIChatSessionQueryContext context = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SaveAsync(AIChatSession chatSession, CancellationToken cancellationToken = default)
        {
            SavedSession = chatSession;

            return Task.CompletedTask;
        }
    }
}
