using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class YesSqlAICompletionUsageStoreTests
{
    [Fact]
    public async Task SaveAsync_WritesUsageRecordToAiCollection()
    {
        var session = new Mock<global::YesSql.ISession>(MockBehavior.Strict);
        session
            .Setup(store => store.SaveAsync(It.IsAny<AICompletionUsageRecord>(), false, "AI", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var store = new YesSqlAICompletionUsageStore(
            session.Object,
            Options.Create(new YesSqlStoreOptions()));

        await store.SaveAsync(new AICompletionUsageRecord(), TestContext.Current.CancellationToken);

        session.VerifyAll();
    }

    /// <summary>
    /// Verifies descending stable ordering and inclusive date filtering, including equal timestamps.
    /// </summary>
    [Fact]
    public async Task GetAsync_ReturnsNewestFirstStableTiesAndAppliesDateFilters()
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
                CreateRecord("before", new DateTime(2026, 5, 1, 23, 59, 0, DateTimeKind.Utc)),
                CreateRecord("start", new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc)),
                CreateRecord("tie-first", tie),
                CreateRecord("tie-second", tie),
                CreateRecord("end", new DateTime(2026, 5, 3, 23, 59, 0, DateTimeKind.Utc)),
                CreateRecord("after", new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc)),
            ],
            cancellationToken);

        await using var session = database.Store.CreateSession();
        var store = new YesSqlAICompletionUsageStore(
            session,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = collectionName,
            }));

        var records = await store.GetAsync(
            new DateTime(2026, 5, 2, 18, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 3, 6, 0, 0, DateTimeKind.Utc),
            cancellationToken);

        Assert.Equal(
            ["end", "tie-first", "tie-second", "start"],
            records.Select(record => record.ResponseId));
    }

    /// <summary>
    /// Verifies a date range without matching records returns an empty collection.
    /// </summary>
    [Fact]
    public async Task GetAsync_WhenNoRecordsMatch_ReturnsEmpty()
    {
        const string collectionName = "TenantOneAI";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await YesSqlAIStoreTestDatabase.CreateAsync(
            [collectionName],
            cancellationToken);
        await database.SaveAsync(
            collectionName,
            [CreateRecord("record-1", new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc))],
            cancellationToken);
        await using var session = database.Store.CreateSession();
        var store = new YesSqlAICompletionUsageStore(
            session,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = collectionName,
            }));

        var records = await store.GetAsync(
            new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
            null,
            cancellationToken);

        Assert.Empty(records);
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
        var store = new YesSqlAICompletionUsageStore(
            session,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = collectionName,
            }));
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.GetAsync(null, null, cancellationSource.Token));
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
        var createdUtc = new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);

        await database.SaveAsync(
            firstCollection,
            [CreateRecord("tenant-one", createdUtc)],
            cancellationToken);
        await database.SaveAsync(
            secondCollection,
            [CreateRecord("tenant-two", createdUtc)],
            cancellationToken);

        await using var firstSession = database.Store.CreateSession();
        await using var secondSession = database.Store.CreateSession();
        var firstStore = new YesSqlAICompletionUsageStore(
            firstSession,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = firstCollection,
            }));
        var secondStore = new YesSqlAICompletionUsageStore(
            secondSession,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = secondCollection,
            }));

        var firstRecords = await firstStore.GetAsync(null, null, cancellationToken);
        var secondRecords = await secondStore.GetAsync(null, null, cancellationToken);

        Assert.Equal("tenant-one", Assert.Single(firstRecords).ResponseId);
        Assert.Equal("tenant-two", Assert.Single(secondRecords).ResponseId);
    }

    /// <summary>
    /// Creates a persisted completion-usage record.
    /// </summary>
    /// <param name="responseId">The response identifier.</param>
    /// <param name="createdUtc">The creation timestamp.</param>
    /// <returns>The completion-usage record.</returns>
    private static AICompletionUsageRecord CreateRecord(
        string responseId,
        DateTime createdUtc)
    {
        return new AICompletionUsageRecord
        {
            ResponseId = responseId,
            CreatedUtc = createdUtc,
        };
    }
}
