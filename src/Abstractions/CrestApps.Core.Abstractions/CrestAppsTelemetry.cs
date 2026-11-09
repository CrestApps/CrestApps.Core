using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CrestApps.Core;

/// <summary>
/// The OpenTelemetry contract of CrestApps.Core: the activity source and meter every package emits through, the
/// tag names they share, and the framework-level instruments. Nothing is recorded until a listener such as the
/// OpenTelemetry SDK subscribes to <see cref="SourceName"/>, so an application that does not collect telemetry
/// pays nothing for it.
/// </summary>
/// <remarks>
/// Tags are kept to bounded values. Session, user, profile and item ids, URLs, file names and prompt text are
/// never put on a metric; where they help, they go on the span only.
/// </remarks>
public static class CrestAppsTelemetry
{
    /// <summary>
    /// The name of the activity source and of the meter that every CrestApps.Core package emits through.
    /// </summary>
    public const string SourceName = "CrestApps.Core";

    /// <summary>
    /// The activity source and meter name of the Microsoft.Extensions.AI OpenTelemetry clients the framework builds
    /// for chat, embeddings, images, speech and realtime. They emit the OpenTelemetry generative AI
    /// (<c>gen_ai.*</c>) spans and metrics when the <c>EnableOpenTelemetry</c> AI option is on. The name is kept
    /// as <c>AICompletionService</c> so existing collectors keep working.
    /// </summary>
    public const string AIClientSourceName = "AICompletionService";

    /// <summary>
    /// The tag that records how an operation ended: <see cref="OutcomeOk"/>, <see cref="OutcomeError"/>,
    /// <see cref="OutcomeCancelled"/> or <see cref="OutcomeBlocked"/>.
    /// </summary>
    public const string OutcomeTag = "outcome";

    /// <summary>
    /// The tag that records the full name of the exception type that failed an operation.
    /// </summary>
    public const string ErrorTypeTag = "error.type";

    /// <summary>
    /// The tag that records the provider that served an operation, such as a search or AI provider.
    /// </summary>
    public const string ProviderTag = "provider";

    /// <summary>
    /// The tag that records which kind of operation was performed.
    /// </summary>
    public const string OperationTag = "operation";

    /// <summary>
    /// The tag that records the background service that ran a cycle or work item.
    /// </summary>
    public const string ServiceTag = "service";

    /// <summary>
    /// The tag that records the kind of work item a background service processed.
    /// </summary>
    public const string WorkItemTypeTag = "work_item_type";

    /// <summary>
    /// The tag that records the in-memory queue an item went through.
    /// </summary>
    public const string QueueTag = "queue";

    /// <summary>
    /// The tag that records the store that committed a unit of work.
    /// </summary>
    public const string StoreTag = "store";

    /// <summary>
    /// The operation completed.
    /// </summary>
    public const string OutcomeOk = "ok";

    /// <summary>
    /// The operation failed.
    /// </summary>
    public const string OutcomeError = "error";

    /// <summary>
    /// The operation was cancelled before it completed.
    /// </summary>
    public const string OutcomeCancelled = "cancelled";

    /// <summary>
    /// The operation was refused, for example by prompt security or a guardrail.
    /// </summary>
    public const string OutcomeBlocked = "blocked";

    /// <summary>
    /// The activity source every CrestApps.Core span starts from.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, CrestAppsConstants.Version);

    /// <summary>
    /// The meter every CrestApps.Core instrument is created on.
    /// </summary>
    public static readonly Meter Meter = new(SourceName, CrestAppsConstants.Version);

    /// <summary>
    /// The histogram buckets, in seconds, of operations that take from a few milliseconds to a couple of minutes.
    /// </summary>
    public static readonly IReadOnlyList<double> DurationBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120];

    /// <summary>
    /// The histogram buckets, in seconds, of long-running work such as ingestion runs, crawls and voice sessions.
    /// </summary>
    public static readonly IReadOnlyList<double> LongDurationBucketBoundaries = [1, 5, 15, 30, 60, 120, 300, 600, 1800, 3600];

    /// <summary>
    /// The duration of a search index read or write, tagged with <see cref="ProviderTag"/>, <see cref="OperationTag"/>
    /// and <see cref="OutcomeTag"/>.
    /// </summary>
    public static readonly Histogram<double> IndexOperationDuration = CreateDurationHistogram(
        "crestapps.core.index.operation.duration",
        "The duration of a search index read or write.");

    /// <summary>
    /// The duration of committing a unit of work to a store, tagged with <see cref="StoreTag"/> and <see cref="OutcomeTag"/>.
    /// </summary>
    public static readonly Histogram<double> StoreCommitDuration = CreateDurationHistogram(
        "crestapps.core.store.commit.duration",
        "The duration of committing a unit of work to a store.");

    /// <summary>
    /// The number of items waiting in an in-memory queue, tagged with <see cref="QueueTag"/>.
    /// </summary>
    public static readonly UpDownCounter<long> QueueDepth = Meter.CreateUpDownCounter<long>(
        "crestapps.core.queue.depth",
        "{item}",
        "The number of items waiting in an in-memory queue.");

    /// <summary>
    /// The time an item waited in an in-memory queue before a worker picked it up, tagged with <see cref="QueueTag"/>.
    /// </summary>
    public static readonly Histogram<double> QueueWaitDuration = CreateDurationHistogram(
        "crestapps.core.queue.wait.duration",
        "The time an item waited in an in-memory queue before a worker picked it up.",
        LongDurationBucketBoundaries);

    /// <summary>
    /// The duration of one work item processed by a background service, tagged with <see cref="ServiceTag"/>,
    /// <see cref="WorkItemTypeTag"/> and <see cref="OutcomeTag"/>.
    /// </summary>
    public static readonly Histogram<double> BackgroundItemDuration = CreateDurationHistogram(
        "crestapps.core.background.item.duration",
        "The duration of one work item processed by a background service.",
        LongDurationBucketBoundaries);

    /// <summary>
    /// The duration of one cycle of a periodic background service, tagged with <see cref="ServiceTag"/> and
    /// <see cref="OutcomeTag"/>. A service whose cycles stop reporting <see cref="OutcomeOk"/> needs attention.
    /// </summary>
    public static readonly Histogram<double> BackgroundCycleDuration = CreateDurationHistogram(
        "crestapps.core.background.cycle.duration",
        "The duration of one cycle of a periodic background service.",
        LongDurationBucketBoundaries);

    /// <summary>
    /// Creates a histogram on <see cref="Meter"/> that records a duration in seconds with bucket boundaries suited
    /// to it, so collectors do not fall back to buckets sized for milliseconds.
    /// </summary>
    /// <param name="name">The instrument name.</param>
    /// <param name="description">The instrument description.</param>
    /// <param name="bucketBoundaries">The bucket boundaries in seconds, or <c>null</c> for <see cref="DurationBucketBoundaries"/>.</param>
    /// <returns>The histogram.</returns>
    public static Histogram<double> CreateDurationHistogram(string name, string description, IReadOnlyList<double> bucketBoundaries = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return Meter.CreateHistogram<double>(
            name,
            "s",
            description,
            tags: null,
            advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries = bucketBoundaries ?? DurationBucketBoundaries,
            });
    }

    /// <summary>
    /// Starts the span of work that was queued earlier. The request that queued it has usually ended by then, so
    /// the span starts a new trace with a link back to the one that queued it rather than becoming its child.
    /// </summary>
    /// <param name="name">The span name.</param>
    /// <param name="queuedContext">The <see cref="Activity.Context"/> that was current when the work was queued.</param>
    /// <returns>The started activity, or <c>null</c> when nothing listens to <see cref="SourceName"/>.</returns>
    public static Activity StartLinkedActivity(string name, ActivityContext queuedContext)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        IEnumerable<ActivityLink> links = queuedContext == default
            ? null
            : [new ActivityLink(queuedContext)];

        return ActivitySource.StartActivity(name, ActivityKind.Internal, parentContext: default, links: links);
    }

    /// <summary>
    /// Records one search index read or write on <see cref="IndexOperationDuration"/>. The providers report a failed
    /// write by returning <c>false</c> rather than throwing, so the outcome is passed in rather than inferred.
    /// </summary>
    /// <param name="provider">The search provider, such as <c>Elasticsearch</c>.</param>
    /// <param name="operation">The operation, such as <c>search</c> or <c>write</c>.</param>
    /// <param name="startTimestamp">The <see cref="Stopwatch"/> timestamp taken when the operation started.</param>
    /// <param name="succeeded">Whether the operation succeeded.</param>
    public static void RecordIndexOperation(string provider, string operation, long startTimestamp, bool succeeded)
    {
        IndexOperationDuration.Record(
            Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
            new KeyValuePair<string, object>(ProviderTag, provider),
            new KeyValuePair<string, object>(OperationTag, operation),
            new KeyValuePair<string, object>(
                OutcomeTag,
                succeeded
                    ? OutcomeOk
                    : OutcomeError));
    }

    /// <summary>
    /// Gets the <see cref="OutcomeTag"/> value of an operation that ended with the given exception.
    /// </summary>
    /// <param name="exception">The exception the operation ended with.</param>
    /// <returns><see cref="OutcomeCancelled"/> for a cancellation, otherwise <see cref="OutcomeError"/>.</returns>
    public static string GetOutcome(Exception exception)
    {
        return exception is OperationCanceledException
            ? OutcomeCancelled
            : OutcomeError;
    }
}
