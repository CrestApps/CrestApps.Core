using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Hubs;
using CrestApps.Core.AI.Exceptions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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

    private sealed class TestAIChatHub : AIChatHubCore<IAIChatHubClient>
    {
        public TestAIChatHub(IServiceProvider services)
            : base(services, TimeProvider.System, NullLogger.Instance)
        {
        }

        public Task SaveChatSessionForTestAsync(IAIChatSessionManager sessionManager, AIChatSession chatSession)
        {
            return SaveChatSessionAsync(sessionManager, chatSession);
        }

        public string GetFriendlyErrorMessageForTest(Exception ex)
        {
            return GetFriendlyErrorMessage(ex);
        }

        public static bool IsEndedStatusForTest(ChatSessionStatus status)
        {
            var method = typeof(AIChatHubCore<IAIChatHubClient>).GetMethod(
                "IsEndedStatus",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.NotNull(method);

return (bool)method.Invoke(null, new object[] { status });
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
