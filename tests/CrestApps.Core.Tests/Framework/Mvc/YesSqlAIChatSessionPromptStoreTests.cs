using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Framework.Mvc;

/// <summary>
/// Tests the YesSql chat-session prompt store.
/// </summary>
public sealed class YesSqlAIChatSessionPromptStoreTests
{
    /// <summary>
    /// Verifies chronological stable ordering and session filtering, including equal timestamps.
    /// </summary>
    [Fact]
    public async Task GetPromptsAsync_ReturnsChronologicalStableOrderAndFiltersSession()
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
                CreatePrompt("older", "session-1", tie.AddHours(-1)),
                CreatePrompt("tie-first", "session-1", tie),
                CreatePrompt("other-session", "session-2", tie.AddHours(-2)),
                CreatePrompt("tie-second", "session-1", tie),
                CreatePrompt("newest", "session-1", tie.AddHours(1)),
            ],
            cancellationToken);

        await using var session = database.Store.CreateSession();
        var store = new YesSqlAIChatSessionPromptStore(
            session,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = collectionName,
            }));

        var prompts = await store.GetPromptsAsync("session-1");

        Assert.Equal(
            ["older", "tie-first", "tie-second", "newest"],
            prompts.Select(prompt => prompt.ItemId));
    }

    /// <summary>
    /// Verifies a missing session returns an empty prompt collection.
    /// </summary>
    [Fact]
    public async Task GetPromptsAsync_WhenNoPromptsMatch_ReturnsEmpty()
    {
        const string collectionName = "TenantOneAI";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await YesSqlAIStoreTestDatabase.CreateAsync(
            [collectionName],
            cancellationToken);
        await database.SaveAsync(
            collectionName,
            [CreatePrompt("prompt-1", "session-1", new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc))],
            cancellationToken);
        await using var session = database.Store.CreateSession();
        var store = new YesSqlAIChatSessionPromptStore(
            session,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = collectionName,
            }));

        var prompts = await store.GetPromptsAsync("missing-session");

        Assert.Empty(prompts);
    }

    /// <summary>
    /// Verifies configured tenant collections remain isolated.
    /// </summary>
    [Fact]
    public async Task GetPromptsAsync_UsesConfiguredTenantCollection()
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
            [CreatePrompt("tenant-one", "shared-session", createdUtc)],
            cancellationToken);
        await database.SaveAsync(
            secondCollection,
            [CreatePrompt("tenant-two", "shared-session", createdUtc)],
            cancellationToken);

        await using var firstSession = database.Store.CreateSession();
        await using var secondSession = database.Store.CreateSession();
        var firstStore = new YesSqlAIChatSessionPromptStore(
            firstSession,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = firstCollection,
            }));
        var secondStore = new YesSqlAIChatSessionPromptStore(
            secondSession,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = secondCollection,
            }));

        var firstPrompts = await firstStore.GetPromptsAsync("shared-session");
        var secondPrompts = await secondStore.GetPromptsAsync("shared-session");

        Assert.Equal("tenant-one", Assert.Single(firstPrompts).ItemId);
        Assert.Equal("tenant-two", Assert.Single(secondPrompts).ItemId);
    }

    /// <summary>
    /// Creates a persisted chat-session prompt.
    /// </summary>
    /// <param name="itemId">The item identifier.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="createdUtc">The creation timestamp.</param>
    /// <returns>The prompt.</returns>
    private static AIChatSessionPrompt CreatePrompt(
        string itemId,
        string sessionId,
        DateTime createdUtc)
    {
        return new AIChatSessionPrompt
        {
            ItemId = itemId,
            SessionId = sessionId,
            Role = ChatRole.User,
            Content = itemId,
            CreatedUtc = createdUtc,
        };
    }
}
