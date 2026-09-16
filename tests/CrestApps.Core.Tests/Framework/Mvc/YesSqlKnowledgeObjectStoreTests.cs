using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Services;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Framework.Mvc;

/// <summary>
/// Tests the YesSql knowledge-object store's figure lookup by content hash.
/// </summary>
/// <remarks>
/// The lookup is what stops the same artwork being sent to a vision model once per document it appears in, so
/// it is worth knowing it works on both stores rather than on the one that was easier to test. The
/// EntityCore side was covered from the start; this side was not, because the suite's YesSql harness
/// registered only the chat indexes and a query against an unregistered index returns nothing at all — which
/// reads as "no match" and would have made these tests pass while proving nothing.
/// </remarks>
public sealed class YesSqlKnowledgeObjectStoreTests
{
    private const string CollectionName = "TenantOneAI";
    private const string FirstDataSourceId = "data-source-1";
    private const string SecondDataSourceId = "data-source-2";
    private const string PromptVersion = "v1";

    /// <summary>
    /// Verifies that the same bytes are found wherever they were ingested, and that nothing which is not a
    /// described figure is returned.
    /// </summary>
    [Fact]
    public async Task FindFigureByContentHash_MatchingBytes_FindsTheFigureInAnotherDataSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var database = await YesSqlAIStoreTestDatabase.CreateAsync([CollectionName], cancellationToken);

        var chart = CreateFigure(SecondDataSourceId, "chart:shared", KnowledgeObjectTypes.Chart, "hash-shared", "A chart of the measured values.");

        await database.SaveAsync(
            CollectionName,
            [
                // Same bytes, but text rather than a figure: the hash matches and the kind does not.
                CreateText(FirstDataSourceId, "text:shared", "hash-shared"),
                CreateFigure(FirstDataSourceId, "figure:other", KnowledgeObjectTypes.Figure, "hash-other", "A photograph."),
                chart,
            ],
            cancellationToken);

        await using var session = database.Store.CreateSession();
        var store = CreateStore(session);

        var match = await store.FindFigureByContentHashAsync("hash-shared", PromptVersion, cancellationToken);

        Assert.NotNull(match);
        Assert.Equal(chart.CanonicalId, match.CanonicalId);

        // Crossing data sources is the point: the same artwork is the same artwork wherever it was ingested.
        Assert.Equal(SecondDataSourceId, match.Source);
    }

    /// <summary>
    /// Verifies that a figure still waiting for its description is a miss rather than a hit with nothing in
    /// it.
    /// </summary>
    [Fact]
    public async Task FindFigureByContentHash_WhenTheFigureHasNoDescriptionYet_IsAMiss()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var database = await YesSqlAIStoreTestDatabase.CreateAsync([CollectionName], cancellationToken);

        await database.SaveAsync(
            CollectionName,
            [CreateFigure(FirstDataSourceId, "figure:pending", KnowledgeObjectTypes.Figure, "hash-pending", description: null)],
            cancellationToken);

        await using var session = database.Store.CreateSession();
        var store = CreateStore(session);

        Assert.Null(await store.FindFigureByContentHashAsync("hash-pending", PromptVersion, cancellationToken));
    }

    /// <summary>
    /// Verifies that a description written by a prompt that has since changed is not reused.
    /// </summary>
    /// <remarks>
    /// Reusing it would hide a prompt change behind a description nobody would think to look at again.
    /// </remarks>
    [Fact]
    public async Task FindFigureByContentHash_WhenThePromptHasChanged_IsAMiss()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var database = await YesSqlAIStoreTestDatabase.CreateAsync([CollectionName], cancellationToken);

        await database.SaveAsync(
            CollectionName,
            [CreateFigure(FirstDataSourceId, "figure:stale", KnowledgeObjectTypes.Figure, "hash-stale", "Described under an older prompt.")],
            cancellationToken);

        await using var session = database.Store.CreateSession();
        var store = CreateStore(session);

        Assert.Null(await store.FindFigureByContentHashAsync("hash-stale", "v2", cancellationToken));
        Assert.NotNull(await store.FindFigureByContentHashAsync("hash-stale", PromptVersion, cancellationToken));
    }

    /// <summary>
    /// Verifies that bytes nothing was made from are a miss rather than an arbitrary figure.
    /// </summary>
    [Fact]
    public async Task FindFigureByContentHash_WhenNothingMatches_IsAMiss()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var database = await YesSqlAIStoreTestDatabase.CreateAsync([CollectionName], cancellationToken);

        await database.SaveAsync(
            CollectionName,
            [CreateFigure(FirstDataSourceId, "figure:one", KnowledgeObjectTypes.Figure, "hash-one", "A photograph.")],
            cancellationToken);

        await using var session = database.Store.CreateSession();
        var store = CreateStore(session);

        Assert.Null(await store.FindFigureByContentHashAsync("hash-absent", PromptVersion, cancellationToken));
    }

    private static YesSqlKnowledgeObjectStore CreateStore(global::YesSql.ISession session)
    {
        return new YesSqlKnowledgeObjectStore(
            session,
            Options.Create(new YesSqlStoreOptions
            {
                AICollectionName = CollectionName,
            }));
    }

    private static KnowledgeObject CreateText(string dataSourceId, string canonicalId, string contentHash)
    {
        return new KnowledgeObject
        {
            Source = dataSourceId,
            CanonicalId = canonicalId,
            ObjectType = KnowledgeObjectTypes.Text,
            RootId = "document:shared",
            ParentId = "article:shared",
            Title = "The measurements, written up.",
            Content = "The measurements are discussed at length.",
            ContentHash = contentHash,
            CreatedUtc = DateTime.UtcNow,
        };
    }

    private static KnowledgeObject CreateFigure(
        string dataSourceId,
        string canonicalId,
        string objectType,
        string contentHash,
        string description)
    {
        var entry = new KnowledgeObject
        {
            Source = dataSourceId,
            CanonicalId = canonicalId,
            ObjectType = objectType,
            RootId = "document:shared",
            ParentId = "article:shared",
            Title = "Figure 1. The measurements.",
            Content = "Figure 1. The measurements.",
            ContentHash = contentHash,
            MediaType = "image/png",
            StoragePath = $"figures/{canonicalId}.png",
            PageStart = 8,
            PageEnd = 8,
            CreatedUtc = DateTime.UtcNow,
        };

        entry.Put(new FigureDetails
        {
            Caption = "Figure 1. The measurements.",
            Tier = "describe",
            Description = description,
            DescriptionPromptVersion = description is null ? null : PromptVersion,
        });

        return entry;
    }
}
