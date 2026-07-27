using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Defines extracted-data map densities used by the recorder benchmarks.
/// </summary>
public enum ExtractedDataMapDensity
{
    /// <summary>
    /// Every source field has retained values.
    /// </summary>
    Dense,

    /// <summary>
    /// One percent of source fields have retained values.
    /// </summary>
    MostlyEmpty,

    /// <summary>
    /// No source fields have retained values.
    /// </summary>
    AllEmpty,
}

/// <summary>
/// Measures the captured LINQ snapshot construction against the current extracted-data recorder.
/// The in-memory store excludes persistence so the benchmark isolates snapshot construction.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class DefaultAIChatSessionExtractedDataRecorderBenchmarks
{
    private AIProfile _profile;
    private AIChatSession _session;
    private LegacyAIChatSessionExtractedDataRecorder _legacyRecorder;
    private DefaultAIChatSessionExtractedDataRecorder _currentRecorder;
    private NoOpExtractedDataStore _legacyStore;
    private NoOpExtractedDataStore _currentStore;

    /// <summary>
    /// Gets or sets the number of source extracted-data fields.
    /// </summary>
    [Params(1000, 10000)]
    public int FieldCount { get; set; }

    /// <summary>
    /// Gets or sets the density of retained extracted-data fields.
    /// </summary>
    [Params(
        ExtractedDataMapDensity.Dense,
        ExtractedDataMapDensity.MostlyEmpty,
        ExtractedDataMapDensity.AllEmpty)]
    public ExtractedDataMapDensity Density { get; set; }

    /// <summary>
    /// Creates descending mixed-case keys and retained value lists for the selected density.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _profile = new AIProfile
        {
            ItemId = "profile-1",
        };
        _session = new AIChatSession
        {
            SessionId = "session-1",
            CreatedUtc = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            ClosedAtUtc = new DateTime(2026, 5, 1, 12, 5, 0, DateTimeKind.Utc),
            ExtractedData = new Dictionary<string, ExtractedFieldState>(FieldCount, StringComparer.Ordinal),
        };

        for (var index = FieldCount - 1; index >= 0; index--)
        {
            var key = (index % 3) switch
            {
                0 => $"FIELD-{index:D5}",
                1 => $"Field-{index:D5}",
                _ => $"field-{index:D5}",
            };
            var values = new List<string>();

            if (ShouldRetain(index))
            {
                values.Add(index % 7 == 3
                    ? null
                    : $"value-{index:D5}-0");
                values.Add($"value-{index:D5}-1");
                values.Add($"value-{index:D5}-2");
            }

            _session.ExtractedData.Add(
                key,
                new ExtractedFieldState
                {
                    Values = values,
                });
        }

        var timeProvider = new FixedTimeProvider(
            new DateTimeOffset(2026, 5, 1, 12, 6, 0, TimeSpan.Zero));
        _legacyStore = new NoOpExtractedDataStore();
        _currentStore = new NoOpExtractedDataStore();
        _legacyRecorder = new LegacyAIChatSessionExtractedDataRecorder(_legacyStore, timeProvider);
        _currentRecorder = new DefaultAIChatSessionExtractedDataRecorder(_currentStore, timeProvider);

        _legacyRecorder.RecordExtractedDataAsync(_profile, _session).GetAwaiter().GetResult();
        _currentRecorder.RecordExtractedDataAsync(_profile, _session).GetAwaiter().GetResult();
        EnsureEquivalent(_legacyStore, _currentStore, _session);
        _legacyStore.Reset();
        _currentStore.Reset();
    }

    /// <summary>
    /// Records a snapshot with the production LINQ implementation captured before optimization.
    /// </summary>
    /// <returns>A task representing the recording operation.</returns>
    [Benchmark(Baseline = true)]
    public Task RecordWithLegacyLinq()
    {
        return _legacyRecorder.RecordExtractedDataAsync(_profile, _session);
    }

    /// <summary>
    /// Records a snapshot with the current production implementation.
    /// </summary>
    /// <returns>A task representing the recording operation.</returns>
    [Benchmark]
    public Task RecordWithCurrentImplementation()
    {
        return _currentRecorder.RecordExtractedDataAsync(_profile, _session);
    }

    /// <summary>
    /// Determines whether the field at the specified index should contain retained values.
    /// </summary>
    /// <param name="index">The source field index.</param>
    /// <returns><see langword="true"/> when the field should be retained; otherwise, <see langword="false"/>.</returns>
    private bool ShouldRetain(int index)
    {
        return Density switch
        {
            ExtractedDataMapDensity.Dense => true,
            ExtractedDataMapDensity.MostlyEmpty => index % 100 == 0,
            _ => false,
        };
    }

    /// <summary>
    /// Verifies both implementations produce equivalent calls, metadata, ordering, comparer, and values.
    /// </summary>
    /// <param name="legacyStore">The store used by the captured implementation.</param>
    /// <param name="currentStore">The store used by the current implementation.</param>
    /// <param name="session">The source session used to verify detached lists.</param>
    private static void EnsureEquivalent(
        NoOpExtractedDataStore legacyStore,
        NoOpExtractedDataStore currentStore,
        AIChatSession session)
    {
        if (legacyStore.SaveCount != currentStore.SaveCount ||
            legacyStore.DeleteCount != currentStore.DeleteCount)
        {
            throw new InvalidOperationException("Recorder implementations invoked different store operations.");
        }

        var legacy = legacyStore.LastSavedRecord;
        var current = currentStore.LastSavedRecord;

        if (legacy is null || current is null)
        {
            if (legacy is not null ||
                current is not null ||
                legacyStore.SaveCount != 0 ||
                legacyStore.DeleteCount != 1)
            {
                throw new InvalidOperationException("Recorder implementations returned different delete results.");
            }

            return;
        }

        if (legacy.ItemId != current.ItemId ||
            legacy.SessionId != current.SessionId ||
            legacy.ProfileId != current.ProfileId ||
            legacy.SessionStartedUtc != current.SessionStartedUtc ||
            legacy.SessionEndedUtc != current.SessionEndedUtc ||
            legacy.UpdatedUtc != current.UpdatedUtc ||
            !ReferenceEquals(legacy.Values.Comparer, current.Values.Comparer) ||
            !legacy.Values.Keys.SequenceEqual(current.Values.Keys))
        {
            throw new InvalidOperationException("Recorder implementations returned different snapshots.");
        }

        foreach (var pair in legacy.Values)
        {
            if (!current.Values.TryGetValue(pair.Key, out var currentValues) ||
                !pair.Value.SequenceEqual(currentValues) ||
                ReferenceEquals(pair.Value, session.ExtractedData[pair.Key].Values) ||
                ReferenceEquals(currentValues, session.ExtractedData[pair.Key].Values))
            {
                throw new InvalidOperationException("Recorder implementations returned different field values.");
            }
        }
    }

    /// <summary>
    /// Captures the pre-optimization production recorder implementation.
    /// </summary>
    private sealed class LegacyAIChatSessionExtractedDataRecorder
    {
        private readonly IAIChatSessionExtractedDataStore _store;
        private readonly TimeProvider _timeProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="LegacyAIChatSessionExtractedDataRecorder"/> class.
        /// </summary>
        /// <param name="store">The in-memory store.</param>
        /// <param name="timeProvider">The fixed time provider.</param>
        public LegacyAIChatSessionExtractedDataRecorder(
            IAIChatSessionExtractedDataStore store,
            TimeProvider timeProvider)
        {
            _store = store;
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// Records the extracted-data snapshot using the captured LINQ implementation.
        /// </summary>
        /// <param name="profile">The AI profile.</param>
        /// <param name="session">The chat session.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the recording operation.</returns>
        public async Task RecordExtractedDataAsync(
            AIProfile profile,
            AIChatSession session,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(session);
            ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);

            var values = session.ExtractedData
                .Where(pair => pair.Value.Values.Count > 0)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Values.ToList(),
                    StringComparer.OrdinalIgnoreCase);

            if (values.Count == 0)
            {
                await _store.DeleteAsync(session.SessionId, cancellationToken);

                return;
            }

            await _store.SaveAsync(
                new AIChatSessionExtractedDataRecord
                {
                    ItemId = session.SessionId,
                    SessionId = session.SessionId,
                    ProfileId = profile.ItemId,
                    SessionStartedUtc = session.CreatedUtc,
                    SessionEndedUtc = session.ClosedAtUtc,
                    UpdatedUtc = _timeProvider.GetUtcNow().UtcDateTime,
                    Values = values,
                },
                cancellationToken);
        }
    }

    /// <summary>
    /// Provides a fixed UTC timestamp without adding clock variability to the benchmark.
    /// </summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="FixedTimeProvider"/> class.
        /// </summary>
        /// <param name="utcNow">The fixed current time.</param>
        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        /// <summary>
        /// Gets the fixed UTC timestamp.
        /// </summary>
        /// <returns>The fixed timestamp.</returns>
        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    /// <summary>
    /// Captures store calls and the last saved record without persistence or I/O.
    /// </summary>
    private sealed class NoOpExtractedDataStore : IAIChatSessionExtractedDataStore
    {
        /// <summary>
        /// Gets the last snapshot passed to <see cref="SaveAsync"/>.
        /// </summary>
        public AIChatSessionExtractedDataRecord LastSavedRecord { get; private set; }

        /// <summary>
        /// Gets the number of save calls.
        /// </summary>
        public int SaveCount { get; private set; }

        /// <summary>
        /// Gets the number of delete calls.
        /// </summary>
        public int DeleteCount { get; private set; }

        /// <summary>
        /// Captures the supplied snapshot without persistence.
        /// </summary>
        /// <param name="record">The snapshot record.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A completed task.</returns>
        public Task SaveAsync(
            AIChatSessionExtractedDataRecord record,
            CancellationToken cancellationToken = default)
        {
            LastSavedRecord = record;
            SaveCount++;

            return Task.CompletedTask;
        }

        /// <summary>
        /// Captures a delete call without persistence.
        /// </summary>
        /// <param name="sessionId">The session identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A completed task whose result is <see langword="true"/>.</returns>
        public Task<bool> DeleteAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            DeleteCount++;

            return Task.FromResult(true);
        }

        /// <summary>
        /// Returns no records.
        /// </summary>
        /// <param name="profileId">The profile identifier.</param>
        /// <param name="startDateUtc">The inclusive start date.</param>
        /// <param name="endDateUtc">The inclusive end date.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>An empty record list.</returns>
        public Task<IReadOnlyList<AIChatSessionExtractedDataRecord>> GetAsync(
            string profileId,
            DateTime? startDateUtc,
            DateTime? endDateUtc,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AIChatSessionExtractedDataRecord>>([]);
        }

        /// <summary>
        /// Clears captured calls before benchmark measurements begin.
        /// </summary>
        public void Reset()
        {
            LastSavedRecord = null;
            SaveCount = 0;
            DeleteCount = 0;
        }
    }
}
