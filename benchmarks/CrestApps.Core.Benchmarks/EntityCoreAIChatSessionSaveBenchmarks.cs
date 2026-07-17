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
/// Measures replacement of an existing serialized chat-session document without
/// counting database creation or seed work.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(
    invocationCount: 1,
    warmupCount: 5,
    iterationCount: 15)]
public class EntityCoreAIChatSessionSaveBenchmarks
{
    private const string _sessionId = "session-benchmark";
    private static readonly DateTimeOffset _originUtc =
        new(2026, 7, 13, 12, 34, 56, TimeSpan.Zero);
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IncrementingTimeProvider _timeProvider = new(_originUtc);
    private SqliteConnection _rootConnection;
    private DbContextOptions<CrestAppsEntityDbContext> _dbContextOptions;
    private AIChatSession _session;

    /// <summary>
    /// Gets or sets the serialized chat-session payload size in bytes.
    /// </summary>
    [Params(1_024, 65_536, 1_048_576)]
    public int PayloadSize { get; set; }

    /// <summary>
    /// Creates and seeds an isolated in-memory SQLite database.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"{nameof(EntityCoreAIChatSessionSaveBenchmarks)}-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        _rootConnection = new SqliteConnection(connectionString);
        await _rootConnection.OpenAsync();
        _dbContextOptions = new DbContextOptionsBuilder<CrestAppsEntityDbContext>()
            .UseSqlite(connectionString)
            .Options;
        _session = CreateSession(PayloadSize);

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.AIChatSessionRecords.Add(CreateRecord(_session));
        await dbContext.SaveChangesAsync();

        await CurrentAsync();
        await LegacyAsync();

        await using var verificationContext = CreateDbContext();
        var storedPayload = await verificationContext.Documents
            .Select(document => document.Content)
            .SingleAsync();

        if (storedPayload.Length != PayloadSize)
        {
            throw new InvalidOperationException(
                $"Expected a {PayloadSize}-byte payload but stored {storedPayload.Length} bytes.");
        }
    }

    /// <summary>
    /// Disposes the benchmark database.
    /// </summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _rootConnection.DisposeAsync();
    }

    /// <summary>
    /// Loads the existing document payload before replacing it and saving changes.
    /// </summary>
    /// <returns>The number of affected database rows.</returns>
    [Benchmark(Baseline = true)]
    public async Task<int> LegacyAsync()
    {
        await using var dbContext = CreateDbContext();
        _session.LastActivityUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var record = await dbContext.AIChatSessionRecords
            .Include(item => item.Document)
            .FirstAsync(item => item.SessionId == _sessionId);

        UpdateRecord(record, _session);

        return await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Uses the current production manager to replace the payload without reading
    /// the previous document content.
    /// </summary>
    /// <returns>The number of affected database rows.</returns>
    [Benchmark]
    public async Task<int> CurrentAsync()
    {
        await using var dbContext = CreateDbContext();
        var manager = new EntityCoreAIChatSessionManager(
            new HttpContextAccessor(),
            dbContext,
            Array.Empty<IConversationDocumentCleanupService>(),
            _timeProvider);

        await manager.SaveAsync(_session);

        return await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Creates a context for one isolated save operation.
    /// </summary>
    /// <returns>The new database context.</returns>
    private CrestAppsEntityDbContext CreateDbContext()
    {
        return new CrestAppsEntityDbContext(
            _dbContextOptions,
            Options.Create(new EntityCoreDataStoreOptions()),
            []);
    }

    /// <summary>
    /// Creates a persisted chat-session record.
    /// </summary>
    /// <param name="session">The session to persist.</param>
    /// <returns>The Entity Framework Core record.</returns>
    private static AIChatSessionRecord CreateRecord(AIChatSession session)
    {
        return new AIChatSessionRecord
        {
            Document = new DocumentRecord
            {
                Type = typeof(AIChatSession).FullName!,
                Content = JsonSerializer.Serialize(session, _jsonSerializerOptions),
            },
            SessionId = session.SessionId,
            ProfileId = session.ProfileId,
            Title = session.Title,
            UserId = session.UserId,
            ClientId = session.ClientId,
            Status = session.Status,
            CreatedUtc = session.CreatedUtc,
            LastActivityUtc = session.LastActivityUtc,
        };
    }

    /// <summary>
    /// Reproduces the original chat-session record update.
    /// </summary>
    /// <param name="record">The destination record.</param>
    /// <param name="session">The source session.</param>
    private static void UpdateRecord(AIChatSessionRecord record, AIChatSession session)
    {
        UpdateIndex(record, session);
        record.Document.Content = JsonSerializer.Serialize(session, _jsonSerializerOptions);
    }

    /// <summary>
    /// Updates the indexed chat-session columns without touching the document navigation.
    /// </summary>
    /// <param name="record">The destination record.</param>
    /// <param name="session">The source session.</param>
    private static void UpdateIndex(AIChatSessionRecord record, AIChatSession session)
    {
        record.ProfileId = session.ProfileId;
        record.Title = session.Title;
        record.UserId = session.UserId;
        record.ClientId = session.ClientId;
        record.Status = session.Status;
        record.CreatedUtc = session.CreatedUtc;
        record.LastActivityUtc = session.LastActivityUtc;
    }

    /// <summary>
    /// Creates a chat session whose serialized representation has the requested size.
    /// </summary>
    /// <param name="payloadSize">The requested serialized payload size.</param>
    /// <returns>The chat session.</returns>
    private static AIChatSession CreateSession(int payloadSize)
    {
        var session = new AIChatSession
        {
            SessionId = _sessionId,
            ProfileId = "profile-benchmark",
            Title = "Benchmark session",
            UserId = "user-benchmark",
            ClientId = "client-benchmark",
            Status = ChatSessionStatus.Active,
            CreatedUtc = _originUtc.UtcDateTime,
            ModifiedUtc = new DateTime(2026, 7, 13, 12, 35, 56, DateTimeKind.Utc),
            LastActivityUtc = _originUtc.UtcDateTime,
            Properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Payload"] = string.Empty,
            },
        };
        var envelopeLength = JsonSerializer.Serialize(session, _jsonSerializerOptions).Length;
        var contentLength = payloadSize - envelopeLength;

        if (contentLength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payloadSize),
                payloadSize,
                "The payload size is too small for the serialized chat session.");
        }

        session.Properties["Payload"] = new string('x', contentLength);
        var serializedLength = JsonSerializer.Serialize(session, _jsonSerializerOptions).Length;

        if (serializedLength != payloadSize)
        {
            throw new InvalidOperationException(
                $"Expected a {payloadSize}-byte payload but created {serializedLength} bytes.");
        }

        return session;
    }

    private sealed class IncrementingTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _originUtc;
        private long _offsetSeconds;

        /// <summary>
        /// Initializes a new instance of the <see cref="IncrementingTimeProvider"/> class.
        /// </summary>
        /// <param name="originUtc">The initial UTC timestamp.</param>
        public IncrementingTimeProvider(DateTimeOffset originUtc)
        {
            _originUtc = originUtc;
        }

        /// <summary>
        /// Gets a distinct whole-second UTC timestamp for each save operation.
        /// </summary>
        /// <returns>The next UTC timestamp.</returns>
        public override DateTimeOffset GetUtcNow()
        {
            return _originUtc.AddSeconds(Interlocked.Increment(ref _offsetSeconds));
        }
    }
}
