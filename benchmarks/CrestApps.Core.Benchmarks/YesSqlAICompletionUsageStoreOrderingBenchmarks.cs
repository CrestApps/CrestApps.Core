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
/// Measures completion-usage ordering after materialization against translated YesSql ordering.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class YesSqlAICompletionUsageStoreOrderingBenchmarks
{
    private const string _collectionName = "AI";
    private static readonly DateTime _originUtc =
        new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _startDateUtc = _originUtc.AddDays(2);
    private static readonly DateTime _endDateUtc = _originUtc.AddDays(7);

    private SqliteConnection _rootConnection;
    private IStore _store;

    /// <summary>
    /// Gets or sets the total number of persisted completion-usage records.
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
            DataSource = $"{nameof(YesSqlAICompletionUsageStoreOrderingBenchmarks)}-{Guid.NewGuid():N}",
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
            [new AICompletionUsageIndexProvider(Options.Create(options))],
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
    /// Loads the filtered records before applying stable descending LINQ ordering.
    /// </summary>
    /// <returns>The ordered records.</returns>
    [Benchmark(Baseline = true)]
    public async Task<List<AICompletionUsageRecord>> MaterializeThenOrderAsync()
    {
        await using var session = _store.CreateSession();
        var records = await CreateFilteredQuery(session).ListAsync();

        return records.OrderByDescending(record => record.CreatedUtc).ToList();
    }

    /// <summary>
    /// Applies descending timestamp and ascending map-index identifier ordering in YesSql.
    /// </summary>
    /// <returns>The ordered records.</returns>
    [Benchmark]
    public async Task<List<AICompletionUsageRecord>> OrderInQueryAsync()
    {
        await using var session = _store.CreateSession();
        var records = await CreateFilteredQuery(session)
            .OrderByDescending(index => index.CreatedUtc)
            .ThenBy(index => index.Id)
            .ListAsync();

        return (List<AICompletionUsageRecord>)records;
    }

    /// <summary>
    /// Creates the production-equivalent inclusive date-range query.
    /// </summary>
    /// <param name="session">The YesSql session.</param>
    /// <returns>The filtered query.</returns>
    private static IQuery<AICompletionUsageRecord, AICompletionUsageIndex> CreateFilteredQuery(
        ISession session)
    {
        var endExclusive = _endDateUtc.Date.AddDays(1);

        return session
            .Query<AICompletionUsageRecord, AICompletionUsageIndex>(collection: _collectionName)
            .Where(index => index.CreatedUtc >= _startDateUtc.Date)
            .Where(index => index.CreatedUtc < endExclusive);
    }

    /// <summary>
    /// Creates the completion-usage map-index schema.
    /// </summary>
    /// <param name="options">The collection options.</param>
    private async Task InitializeSchemaAsync(YesSqlStoreOptions options)
    {
        await using var connection = _store.Configuration.ConnectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var schemaBuilder = new SchemaBuilder(_store.Configuration, transaction);

        await schemaBuilder.CreateAICompletionUsageIndexSchemaAsync(options);
        await transaction.CommitAsync();
    }

    /// <summary>
    /// Persists timestamp groups in insertion order so equal-key stability is observable.
    /// </summary>
    private async Task SeedAsync()
    {
        await using var session = _store.CreateSession();
        var bucketCount = (RecordCount + 3) / 4;
        var rangeTicks = TimeSpan.FromDays(10).Ticks;

        for (var index = 0; index < RecordCount; index++)
        {
            var bucket = index / 4;
            var createdUtc = _originUtc.AddTicks((rangeTicks * bucket) / bucketCount);
            await session.SaveAsync(
                new AICompletionUsageRecord
                {
                    ContextType = "Chat",
                    SessionId = $"session-{index % 100:D3}",
                    ProfileId = $"profile-{index % 12:D2}",
                    InteractionId = $"interaction-{index:D5}",
                    UserId = $"user-{index % 250:D3}",
                    ClientName = "Benchmark",
                    ConnectionName = "benchmark-connection",
                    DeploymentName = "benchmark-deployment",
                    ModelName = "benchmark-model",
                    ResponseId = $"response-{index:D5}",
                    InputTokenCount = 500 + index % 500,
                    OutputTokenCount = 100 + index % 100,
                    TotalTokenCount = 600 + index % 600,
                    ResponseLatencyMs = 100 + index % 900,
                    CreatedUtc = createdUtc,
                },
                false,
                _collectionName);
        }

        await session.SaveChangesAsync();
    }

    /// <summary>
    /// Verifies exact response-identifier sequence equivalence, including equal timestamps.
    /// </summary>
    /// <param name="legacy">The materialize-then-sort result.</param>
    /// <param name="candidate">The query-ordered result.</param>
    private static void EnsureEquivalent(
        List<AICompletionUsageRecord> legacy,
        List<AICompletionUsageRecord> candidate)
    {
        if (legacy.Count != candidate.Count)
        {
            throw new InvalidOperationException("Completion-usage queries returned different counts.");
        }

        for (var index = 0; index < legacy.Count; index++)
        {
            if (legacy[index].ResponseId != candidate[index].ResponseId)
            {
                throw new InvalidOperationException(
                    $"Completion-usage queries differed at index {index}.");
            }
        }
    }
}
