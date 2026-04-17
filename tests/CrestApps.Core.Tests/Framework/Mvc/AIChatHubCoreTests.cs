using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Hubs;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class AIChatHubCoreTests
{
    [Fact]
    public async Task SaveChatSessionAsync_PreservesPersistedDocumentsAndCommits()
    {
        var sessionManager = new TestAIChatSessionManager
        {
            PersistedSession = new AIChatSession
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
            },
        };

        var committer = new TestStoreCommitter();

        var services = new ServiceCollection();
        services.AddSingleton<IStoreCommitter>(committer);
        var serviceProvider = services.BuildServiceProvider();

        var chatSession = new AIChatSession
        {
            SessionId = "session-1",
            Documents = [],
        };

        var hub = new TestAIChatHub(serviceProvider);

        await hub.SaveChatSessionForTestAsync(sessionManager, chatSession);

        Assert.Single(chatSession.Documents);
        Assert.Equal("doc-1", chatSession.Documents[0].DocumentId);
        Assert.Same(chatSession, sessionManager.SavedSession);
        Assert.Equal(["session-1"], sessionManager.FindByIdRequests);
        Assert.True(committer.WasCommitted);
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
        public AIChatSession PersistedSession { get; set; }

        public AIChatSession SavedSession { get; private set; }

        public List<string> FindByIdRequests { get; } = [];

        public Task<bool> DeleteAsync(string sessionId)
        {
            throw new NotSupportedException();
        }

        public Task<int> DeleteAllAsync(string profileId)
        {
            throw new NotSupportedException();
        }

        public Task<AIChatSession> FindAsync(string id)
        {
            throw new NotSupportedException();
        }

        public Task<AIChatSession> FindByIdAsync(string id)
        {
            FindByIdRequests.Add(id);

            return Task.FromResult(PersistedSession);
        }

        public Task<AIChatSession> NewAsync(AIProfile profile, NewAIChatSessionContext context)
        {
            throw new NotSupportedException();
        }

        public Task<AIChatSessionResult> PageAsync(int page, int pageSize, AIChatSessionQueryContext context = null)
        {
            throw new NotSupportedException();
        }

        public Task SaveAsync(AIChatSession chatSession)
        {
            SavedSession = chatSession;

            return Task.CompletedTask;
        }
    }
}
