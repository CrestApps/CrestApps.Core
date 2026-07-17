using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.EntityCore;
using CrestApps.Core.Data.EntityCore.Models;
using CrestApps.Core.Data.EntityCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures Entity Framework Core chat-session paging across payload sizes, row counts,
/// and the repeated enumeration performed by the MVC sessions view.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 6)]
public class EntityCoreAIChatSessionPageBenchmarks
{
    private static readonly DateTime _modifiedUtc =
        new(2026, 7, 13, 12, 34, 56, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private SqliteConnection _rootConnection;
    private CrestAppsEntityDbContext _dbContext;
    private EntityCoreAIChatSessionManager _manager;

    /// <summary>
    /// Gets or sets the serialized chat-session payload size in bytes.
    /// </summary>
    [Params(1_024, 65_536, 1_048_576)]
    public int PayloadSize { get; set; }

    /// <summary>
    /// Gets or sets the number of chat-session rows returned by the page.
    /// </summary>
    [Params(1, 20, 200)]
    public int RowCount { get; set; }

    /// <summary>
    /// Gets or sets whether the result is consumed using the MVC view's two
    /// <c>Any()</c> checks followed by ordered enumeration.
    /// </summary>
    [Params(false, true)]
    public bool ViewEnumeration { get; set; }

    /// <summary>
    /// Creates and seeds an isolated in-memory SQLite database.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"{nameof(EntityCoreAIChatSessionPageBenchmarks)}-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        _rootConnection = new SqliteConnection(connectionString);
        await _rootConnection.OpenAsync();

        var options = new DbContextOptionsBuilder<CrestAppsEntityDbContext>()
            .UseSqlite(connectionString)
            .Options;
        _dbContext = new CrestAppsEntityDbContext(
            options,
            Options.Create(new EntityCoreDataStoreOptions()),
            []);
        await _dbContext.Database.EnsureCreatedAsync();

        var payload = CreatePayload(PayloadSize);
        var records = new AIChatSessionRecord[RowCount];

        for (var index = 0; index < records.Length; index++)
        {
            var createdUtc = _modifiedUtc.AddMinutes(index);
            records[index] = new AIChatSessionRecord
            {
                Document = new DocumentRecord
                {
                    Type = typeof(AIChatSession).FullName!,
                    Content = payload,
                },
                SessionId = $"session-{index:D3}",
                ProfileId = "profile-benchmark",
                Title = $"Session {index}",
                UserId = $"user-{index % 10}",
                ClientId = $"client-{index % 5}",
                Status = ChatSessionStatus.Active,
                CreatedUtc = createdUtc,
                LastActivityUtc = createdUtc,
            };
        }

        _dbContext.AIChatSessionRecords.AddRange(records);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        _manager = new EntityCoreAIChatSessionManager(
            new HttpContextAccessor(),
            _dbContext,
            Array.Empty<IConversationDocumentCleanupService>(),
            TimeProvider.System);

        var legacy = await PageLegacyAsync();
        var current = await _manager.PageAsync(
            1,
            RowCount,
            new AIChatSessionQueryContext
            {
                ProfileId = "profile-benchmark",
            });

        EnsureEquivalent(legacy, current);
    }

    /// <summary>
    /// Disposes the benchmark database.
    /// </summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _dbContext.DisposeAsync();
        await _rootConnection.DisposeAsync();
    }

    /// <summary>
    /// Executes the legacy entity-plus-document materialization and deferred projection.
    /// </summary>
    /// <returns>A checksum over the consumed page.</returns>
    [Benchmark(Baseline = true)]
    public async Task<long> LegacyAsync()
    {
        var result = await PageLegacyAsync();

        return Consume(result);
    }

    /// <summary>
    /// Executes the current production chat-session paging path.
    /// </summary>
    /// <returns>A checksum over the consumed page.</returns>
    [Benchmark]
    public async Task<long> CurrentAsync()
    {
        var result = await _manager.PageAsync(
            1,
            RowCount,
            new AIChatSessionQueryContext
            {
                ProfileId = "profile-benchmark",
            });

        return Consume(result);
    }

    /// <summary>
    /// Reproduces the original production query and deferred summary projection.
    /// </summary>
    /// <returns>The paged session summaries.</returns>
    private async Task<AIChatSessionResult> PageLegacyAsync()
    {
        var query = _dbContext.AIChatSessionRecords
            .AsNoTracking()
            .Where(record => record.ProfileId == "profile-benchmark");
        var total = await query.CountAsync();
        var records = await query
            .Include(record => record.Document)
            .OrderByDescending(record => record.CreatedUtc)
            .ThenByDescending(record => record.LastActivityUtc)
            .Take(RowCount)
            .ToListAsync();

        return new AIChatSessionResult
        {
            Count = total,
            Sessions = records.Select(record => new AIChatSessionEntry
            {
                SessionId = record.SessionId,
                ProfileId = record.ProfileId,
                Title = record.Title,
                UserId = record.UserId,
                ClientId = record.ClientId,
                Status = record.Status,
                CreatedUtc = record.CreatedUtc,
                ModifiedUtc = JsonSerializer.Deserialize<AIChatSession>(
                    record.Document.Content,
                    _jsonSerializerOptions).ModifiedUtc,
                LastActivityUtc = record.LastActivityUtc,
            }),
        };
    }

    /// <summary>
    /// Consumes one page either once or with the repeated enumeration used by the MVC view.
    /// </summary>
    /// <param name="result">The page to consume.</param>
    /// <returns>A checksum that observes every summary field used by the benchmark.</returns>
    private long Consume(AIChatSessionResult result)
    {
        var checksum = result.Count;

        if (ViewEnumeration)
        {
            checksum += result.Sessions.Any() ? 1 : 0;
            checksum += result.Sessions.Any() ? 1 : 0;

            return ConsumeEntries(result.Sessions.OrderByDescending(entry => entry.CreatedUtc), checksum);
        }

        return ConsumeEntries(result.Sessions, checksum);
    }

    /// <summary>
    /// Computes a checksum over the supplied session summaries.
    /// </summary>
    /// <param name="entries">The summaries to consume.</param>
    /// <param name="checksum">The initial checksum.</param>
    /// <returns>The completed checksum.</returns>
    private static long ConsumeEntries(IEnumerable<AIChatSessionEntry> entries, long checksum)
    {
        foreach (var entry in entries)
        {
            checksum += entry.SessionId.Length;
            checksum += entry.ProfileId.Length;
            checksum += entry.Title.Length;
            checksum += entry.UserId.Length;
            checksum += entry.ClientId.Length;
            checksum += (int)entry.Status;
            checksum += entry.CreatedUtc.Ticks;
            checksum += entry.ModifiedUtc?.Ticks ?? 0;
            checksum += entry.LastActivityUtc.Ticks;
        }

        return checksum;
    }

    /// <summary>
    /// Creates an ASCII JSON document with the exact requested UTF-8 byte length.
    /// </summary>
    /// <param name="payloadSize">The requested payload size.</param>
    /// <returns>The valid chat-session JSON payload.</returns>
    private static string CreatePayload(int payloadSize)
    {
        const string prefix = "{\"ModifiedUtc\":\"2026-07-13T12:34:56Z\",\"Properties\":{\"Payload\":\"";
        const string suffix = "\"}}";
        var contentLength = payloadSize - prefix.Length - suffix.Length;

        if (contentLength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payloadSize),
                payloadSize,
                "The payload size is too small for the JSON envelope.");
        }

        return string.Concat(prefix, new string('x', contentLength), suffix);
    }

    /// <summary>
    /// Verifies that legacy and current paths return the same ordered values.
    /// </summary>
    /// <param name="legacy">The legacy result.</param>
    /// <param name="current">The current result.</param>
    private static void EnsureEquivalent(
        AIChatSessionResult legacy,
        AIChatSessionResult current)
    {
        if (legacy.Count != current.Count)
        {
            throw new InvalidOperationException("Chat-session page counts differ.");
        }

        var legacyEntries = legacy.Sessions.ToArray();
        var currentEntries = current.Sessions.ToArray();

        if (legacyEntries.Length != currentEntries.Length)
        {
            throw new InvalidOperationException("Chat-session page lengths differ.");
        }

        for (var index = 0; index < legacyEntries.Length; index++)
        {
            var left = legacyEntries[index];
            var right = currentEntries[index];

            if (left.SessionId != right.SessionId
                || left.ProfileId != right.ProfileId
                || left.Title != right.Title
                || left.UserId != right.UserId
                || left.ClientId != right.ClientId
                || left.Status != right.Status
                || left.CreatedUtc != right.CreatedUtc
                || left.ModifiedUtc != right.ModifiedUtc
                || left.LastActivityUtc != right.LastActivityUtc)
            {
                throw new InvalidOperationException(
                    $"Chat-session page entries differ at index {index}.");
            }
        }
    }
}
