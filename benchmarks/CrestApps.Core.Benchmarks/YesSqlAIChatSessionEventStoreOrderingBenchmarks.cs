using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Provider.Sqlite;
using YesSql.Sql;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures chat-session event ordering after materialization against translated YesSql ordering.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class YesSqlAIChatSessionEventStoreOrderingBenchmarks
{
    private const string _collectionName = "AI";
    private const string _profileId = "profile-target";
    private static readonly DateTime _originUtc =
        new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _startDateUtc = _originUtc.AddDays(2);
    private static readonly DateTime _endDateUtc = _originUtc.AddDays(7);

    private SqliteConnection _rootConnection;
    private IStore _store;

    /// <summary>
    /// Gets or sets the total number of persisted chat-session event records.
    /// </summary>
    [Params(100, 1000, 10000)]
    public int RecordCount { get; set; }

    /// <summary>
    /// Creates an isolated in-memory YesSql SQLite store and realistic filtered records.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"{nameof(YesSqlAIChatSessionEventStoreOrderingBenchmarks)}-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        _rootConnection = new SqliteConnection(connectionString);
        await _rootConnection.OpenAsync();
        _store = await StoreFactory.CreateAndInitializeAsync(
            new Configuration().UseSqLite(connectionString));
        await _store.InitializeCollectionAsync(_collectionName);
        var options = new YesSqlStoreOptions
        {
            AICollectionName = _collectionName,
        };

        _store.RegisterIndexes(
            [new AIChatSessionMetricsIndexProvider(Options.Create(options))],
            _collectionName);
        await InitializeSchemaAsync(options);
        await SeedAsync();

        var legacy = await MaterializeThenOrderAsync();
        var candidate = await OrderInQueryAsync();
        EnsureEquivalent(legacy, candidate);
    }

    /// <summary>
    /// Disposes the isolated benchmark database.
    /// </summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        _store.Dispose();
        await _rootConnection.DisposeAsync();
    }

    /// <summary>
    /// Loads the filtered events before applying stable descending LINQ ordering.
    /// </summary>
    /// <returns>The ordered events.</returns>
    [Benchmark(Baseline = true)]
    public async Task<List<AIChatSessionEvent>> MaterializeThenOrderAsync()
    {
        await using var session = _store.CreateSession();
        var events = await CreateFilteredQuery(session).ListAsync();

        return events.OrderByDescending(chatEvent => chatEvent.SessionStartedUtc).ToList();
    }

    /// <summary>
    /// Applies descending start timestamp and ascending map-index identifier ordering in YesSql.
    /// </summary>
    /// <returns>The ordered events.</returns>
    [Benchmark]
    public async Task<List<AIChatSessionEvent>> OrderInQueryAsync()
    {
        await using var session = _store.CreateSession();
        var events = await CreateFilteredQuery(session)
            .OrderByDescending(index => index.SessionStartedUtc)
            .ThenBy(index => index.Id)
            .ListAsync();

        return (List<AIChatSessionEvent>)events;
    }

    /// <summary>
    /// Creates the production-equivalent profile and inclusive date-range query.
    /// </summary>
    /// <param name="session">The YesSql session.</param>
    /// <returns>The filtered query.</returns>
    private static IQuery<AIChatSessionEvent, AIChatSessionMetricsIndex> CreateFilteredQuery(
        ISession session)
    {
        var endExclusive = _endDateUtc.Date.AddDays(1);

        return session
            .Query<AIChatSessionEvent, AIChatSessionMetricsIndex>(collection: _collectionName)
            .Where(index => index.ProfileId == _profileId)
            .Where(index => index.SessionStartedUtc >= _startDateUtc.Date)
            .Where(index => index.SessionStartedUtc < endExclusive);
    }

    /// <summary>
    /// Creates the chat-session metrics map-index schema.
    /// </summary>
    /// <param name="options">The collection options.</param>
    private async Task InitializeSchemaAsync(YesSqlStoreOptions options)
    {
        await using var connection = _store.Configuration.ConnectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var schemaBuilder = new SchemaBuilder(_store.Configuration, transaction);

        await schemaBuilder.CreateAIChatSessionMetricsSchemaAsync(
            options,
            new AIChatSessionMetricsIndexSchemaOptions());
        await transaction.CommitAsync();
    }

    /// <summary>
    /// Persists timestamp groups and nonmatching profiles so ties and filters remain observable.
    /// </summary>
    private async Task SeedAsync()
    {
        await using var session = _store.CreateSession();
        var bucketCount = (RecordCount + 3) / 4;
        var rangeTicks = TimeSpan.FromDays(10).Ticks;

        for (var index = 0; index < RecordCount; index++)
        {
            var bucket = index / 4;
            var startedUtc = _originUtc.AddTicks((rangeTicks * bucket) / bucketCount);
            await session.SaveAsync(
                new AIChatSessionEvent
                {
                    SessionId = $"session-{index:D5}",
                    ProfileId = index % 4 == 3 ? "profile-other" : _profileId,
                    VisitorId = $"visitor-{index % 1000:D4}",
                    UserId = $"user-{index % 250:D3}",
                    IsAuthenticated = index % 3 != 0,
                    SessionStartedUtc = startedUtc,
                    SessionEndedUtc = startedUtc.AddMinutes(5 + index % 30),
                    MessageCount = 2 + index % 20,
                    HandleTimeSeconds = 30 + index % 600,
                    IsResolved = index % 5 != 0,
                    TotalInputTokens = 500 + index % 500,
                    TotalOutputTokens = 100 + index % 100,
                    AverageResponseLatencyMs = 100 + index % 900,
                    CompletionCount = 1 + index % 10,
                    CreatedUtc = startedUtc,
                },
                false,
                _collectionName);
        }

        await session.SaveChangesAsync();
    }

    /// <summary>
    /// Verifies exact session-identifier sequence equivalence, including equal timestamps.
    /// </summary>
    /// <param name="legacy">The materialize-then-sort result.</param>
    /// <param name="candidate">The query-ordered result.</param>
    private static void EnsureEquivalent(
        List<AIChatSessionEvent> legacy,
        List<AIChatSessionEvent> candidate)
    {
        if (legacy.Count != candidate.Count)
        {
            throw new InvalidOperationException("Chat-session event queries returned different counts.");
        }

        for (var index = 0; index < legacy.Count; index++)
        {
            if (legacy[index].SessionId != candidate[index].SessionId)
            {
                throw new InvalidOperationException(
                    $"Chat-session event queries differed at index {index}.");
            }
        }
    }
}
