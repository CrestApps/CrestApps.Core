using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents.Tabular;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class TabularWorkspaceCacheTests
{
    [Fact]
    public async Task GetOrCreate_SameKeyWithinSlidingExpiration_ReusesWorkspace()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 6, 26, 18, 0, 0, TimeSpan.Zero));
        using var cache = CreateCache(timeProvider);
        var key = CreateSessionKey("session-1", "doc-1");

        var first = cache.GetOrCreate(key);
        await first.EnsureReadyAsync(Documents("doc-1"), Loader("region,amount\nNorth,100"), TestContext.Current.CancellationToken);

        timeProvider.Advance(TimeSpan.FromMinutes(4));

        var second = cache.GetOrCreate(key);

        Assert.Same(first, second);
        Assert.Equal(0, cache.CompactExpired());
    }

    [Fact]
    public async Task CompactExpired_DisposesIdleWorkspace()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 6, 26, 18, 0, 0, TimeSpan.Zero));
        using var cache = CreateCache(timeProvider);
        var key = CreateSessionKey("session-1", "doc-1");
        var workspace = cache.GetOrCreate(key);
        await workspace.EnsureReadyAsync(Documents("doc-1"), Loader("region,amount\nNorth,100"), TestContext.Current.CancellationToken);

        timeProvider.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(1, cache.CompactExpired());
        await Assert.ThrowsAnyAsync<Exception>(
            () => workspace.QueryAsync("SELECT * FROM sales", 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvalidateChatSession_DisposesMatchingWorkspace()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 6, 26, 18, 0, 0, TimeSpan.Zero));
        using var cache = CreateCache(timeProvider);
        var matching = cache.GetOrCreate(CreateSessionKey("session-1", "doc-1"));
        var other = cache.GetOrCreate(CreateSessionKey("session-2", "doc-2"));
        await matching.EnsureReadyAsync(Documents("doc-1"), Loader("region,amount\nNorth,100"), TestContext.Current.CancellationToken);
        await other.EnsureReadyAsync(Documents("doc-2", "other.csv"), Loader("region,amount\nSouth,200"), TestContext.Current.CancellationToken);

        cache.InvalidateChatSession("session-1");

        await Assert.ThrowsAnyAsync<Exception>(
            () => matching.QueryAsync("SELECT * FROM sales", 10, TestContext.Current.CancellationToken));
        var result = await other.QueryAsync("SELECT COUNT(*) FROM other", 10, TestContext.Current.CancellationToken);
        Assert.Equal(1L, Assert.Single(result.Rows)[0]);
    }

    [Fact]
    public async Task InvalidateReference_DisposesMatchingWorkspace()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 6, 26, 18, 0, 0, TimeSpan.Zero));
        using var cache = CreateCache(timeProvider);
        var workspace = cache.GetOrCreate(CreateInteractionKey("interaction-1", "doc-1"));
        await workspace.EnsureReadyAsync(Documents("doc-1"), Loader("region,amount\nNorth,100"), TestContext.Current.CancellationToken);

        cache.InvalidateReference(AIReferenceTypes.Document.ChatInteraction, "interaction-1");

        await Assert.ThrowsAnyAsync<Exception>(
            () => workspace.QueryAsync("SELECT * FROM sales", 10, TestContext.Current.CancellationToken));
    }

    private static TabularWorkspaceCache CreateCache(TestTimeProvider timeProvider)
    {
        return new TabularWorkspaceCache(
            Options.Create(new TabularWorkspaceOptions
            {
                WorkspaceSlidingExpiration = TimeSpan.FromMinutes(5),
            }),
            timeProvider,
            NullLogger<TabularWorkspaceCache>.Instance);
    }

    private static TabularWorkspaceCacheKey CreateSessionKey(string sessionId, string documentId)
    {
        return new TabularWorkspaceCacheKey(
            $"chatsession:{sessionId}:documents:{documentId}",
            chatInteractionId: null,
            chatSessionId: sessionId,
            profileId: "profile-1",
            [(AIReferenceTypes.Document.ChatSession, sessionId)]);
    }

    private static TabularWorkspaceCacheKey CreateInteractionKey(string interactionId, string documentId)
    {
        return new TabularWorkspaceCacheKey(
            $"chatinteraction:{interactionId}:documents:{documentId}",
            chatInteractionId: interactionId,
            chatSessionId: null,
            profileId: null,
            [(AIReferenceTypes.Document.ChatInteraction, interactionId)]);
    }

    private static IReadOnlyList<TabularDocumentRef> Documents(string documentId, string fileName = "sales.csv")
    {
        return [new TabularDocumentRef(documentId, fileName)];
    }

    private static Func<string, CancellationToken, Task<string>> Loader(string content)
    {
        return (_, _) => Task.FromResult(content);
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan value)
        {
            _utcNow += value;
        }
    }
}
