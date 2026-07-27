using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class YesSqlAIChatSessionEventStoreTests
{
    [Fact]
    public async Task SaveAsync_WritesAnalyticsRecordToAiCollection()
    {
        var session = new Mock<global::YesSql.ISession>(MockBehavior.Strict);
        session
            .Setup(store => store.SaveAsync(It.IsAny<AIChatSessionEvent>(), false, "AI", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var store = new YesSqlAIChatSessionEventStore(
            session.Object,
            Options.Create(new YesSqlStoreOptions()));

        await store.SaveAsync(new AIChatSessionEvent
        {
            SessionId = "session-1",
        }, TestContext.Current.CancellationToken);

        session.VerifyAll();
    }

    /// <summary>
    /// Verifies descending stable ordering and profile/date filtering, including equal timestamps.
    /// </summary>
    [Fact]
    public async Task GetAsync_ReturnsNewestFirstStableTiesAndAppliesFilters()
    {
        const string collectionName = "TenantOneAI";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await YesSqlAIStoreTestDatabase.CreateAsync(
            [collectionName],
            cancellationToken);
        var tie = new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);

        await database.SaveAsync(
            collectionName,
            [
                CreateEvent("before", "profile-1", new DateTime(2026, 5, 1, 23, 59, 0, DateTimeKind.Utc)),
                CreateEvent("start", "profile-1", new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc)),
                CreateEvent("tie-first", "profile-1", tie),
                CreateEvent("other-profile", "profile-2", tie.AddHours(2)),
                CreateEvent("tie-second", "profile-1", tie),
                CreateEvent("end", "profile-1", new DateTime(2026, 5, 3, 23, 59, 0, DateTimeKind.Utc)),
                CreateEvent("after", "profile-1", new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc)),
            ],
            cancellationToken);

        await using var session = database.Store.CreateSession();
        var store = new YesSqlAIChatSessionEventStore(
            session,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = collectionName,
            }));

        var events = await store.GetAsync(
            "profile-1",
            new DateTime(2026, 5, 2, 18, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 3, 6, 0, 0, DateTimeKind.Utc),
            cancellationToken);

        Assert.Equal(
            ["end", "tie-first", "tie-second", "start"],
            events.Select(chatEvent => chatEvent.SessionId));
    }

    /// <summary>
    /// Verifies filters without matching events return an empty collection.
    /// </summary>
    [Fact]
    public async Task GetAsync_WhenNoEventsMatch_ReturnsEmpty()
    {
        const string collectionName = "TenantOneAI";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await YesSqlAIStoreTestDatabase.CreateAsync(
            [collectionName],
            cancellationToken);
        await database.SaveAsync(
            collectionName,
            [
                CreateEvent(
                    "session-1",
                    "profile-1",
                    new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc)),
            ],
            cancellationToken);
        await using var session = database.Store.CreateSession();
        var store = new YesSqlAIChatSessionEventStore(
            session,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = collectionName,
            }));

        var events = await store.GetAsync(
            "missing-profile",
            null,
            null,
            cancellationToken);

        Assert.Empty(events);
    }

    /// <summary>
    /// Verifies caller cancellation propagates through the YesSql query.
    /// </summary>
    [Fact]
    public async Task GetAsync_WhenCanceled_PropagatesCancellation()
    {
        const string collectionName = "TenantOneAI";
        await using var database = await YesSqlAIStoreTestDatabase.CreateAsync(
            [collectionName],
            TestContext.Current.CancellationToken);
        await using var session = database.Store.CreateSession();
        var store = new YesSqlAIChatSessionEventStore(
            session,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = collectionName,
            }));
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.GetAsync(null, null, null, cancellationSource.Token));
    }

    /// <summary>
    /// Verifies configured tenant collections remain isolated.
    /// </summary>
    [Fact]
    public async Task GetAsync_UsesConfiguredTenantCollection()
    {
        const string firstCollection = "TenantOneAI";
        const string secondCollection = "TenantTwoAI";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await YesSqlAIStoreTestDatabase.CreateAsync(
            [firstCollection, secondCollection],
            cancellationToken);
        var startedUtc = new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);

        await database.SaveAsync(
            firstCollection,
            [CreateEvent("tenant-one", "profile-1", startedUtc)],
            cancellationToken);
        await database.SaveAsync(
            secondCollection,
            [CreateEvent("tenant-two", "profile-1", startedUtc)],
            cancellationToken);

        await using var firstSession = database.Store.CreateSession();
        await using var secondSession = database.Store.CreateSession();
        var firstStore = new YesSqlAIChatSessionEventStore(
            firstSession,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = firstCollection,
            }));
        var secondStore = new YesSqlAIChatSessionEventStore(
            secondSession,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = secondCollection,
            }));

        var firstEvents = await firstStore.GetAsync(null, null, null, cancellationToken);
        var secondEvents = await secondStore.GetAsync(null, null, null, cancellationToken);

        Assert.Equal("tenant-one", Assert.Single(firstEvents).SessionId);
        Assert.Equal("tenant-two", Assert.Single(secondEvents).SessionId);
    }

    /// <summary>
    /// Creates a persisted chat-session event.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="profileId">The profile identifier.</param>
    /// <param name="sessionStartedUtc">The session start timestamp.</param>
    /// <returns>The chat-session event.</returns>
    private static AIChatSessionEvent CreateEvent(
        string sessionId,
        string profileId,
        DateTime sessionStartedUtc)
    {
        return new AIChatSessionEvent
        {
            SessionId = sessionId,
            ProfileId = profileId,
            SessionStartedUtc = sessionStartedUtc,
            CreatedUtc = sessionStartedUtc,
        };
    }
}
