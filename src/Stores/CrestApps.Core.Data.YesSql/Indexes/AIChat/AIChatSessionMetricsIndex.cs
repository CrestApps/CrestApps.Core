using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

/// <summary>
/// YesSql map index for <see cref="AIChatSessionEvent"/>, storing aggregated metrics
/// for a single chat session to support analytics and reporting queries.
/// </summary>
public sealed class AIChatSessionMetricsIndex : MapIndex
{
    /// <summary>
    /// Gets or sets the unique identifier of the chat session.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the AI profile used during the session.
    /// </summary>
    public string ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the anonymous visitor identifier for unauthenticated sessions.
    /// </summary>
    public string VisitorId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the authenticated user, if available.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the session was initiated by an authenticated user.
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the chat session started.
    /// </summary>
    public DateTime SessionStartedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the chat session ended,
    /// or <see langword="null"/> if still active.
    /// </summary>
    public DateTime? SessionEndedUtc { get; set; }

    /// <summary>
    /// Gets or sets the total number of messages exchanged in the session.
    /// </summary>
    public int MessageCount { get; set; }

    /// <summary>
    /// Gets or sets the total elapsed handle time of the session in seconds.
    /// </summary>
    public double HandleTimeSeconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the session was marked as resolved.
    /// </summary>
    public bool IsResolved { get; set; }

    /// <summary>
    /// Gets or sets the hour of day (0-23) when the session started, derived from <see cref="SessionStartedUtc"/>.
    /// </summary>
    public int HourOfDay { get; set; }

    /// <summary>
    /// Gets or sets the day of week (0 = Sunday, 6 = Saturday) when the session started,
    /// derived from <see cref="SessionStartedUtc"/>.
    /// </summary>
    public int DayOfWeek { get; set; }

    /// <summary>
    /// Gets or sets the total number of input tokens consumed across all completions in the session.
    /// </summary>
    public int TotalInputTokens { get; set; }

    /// <summary>
    /// Gets or sets the total number of output tokens produced across all completions in the session.
    /// </summary>
    public int TotalOutputTokens { get; set; }

    /// <summary>
    /// Gets or sets the average response latency in milliseconds across all completions in the session.
    /// </summary>
    public double AverageResponseLatencyMs { get; set; }

    /// <summary>
    /// Gets or sets the number of AI completions performed during the session.
    /// </summary>
    public int CompletionCount { get; set; }

    /// <summary>
    /// Gets or sets the user's overall rating for the session:
    /// <see langword="true"/> for thumbs up, <see langword="false"/> for thumbs down,
    /// or <see langword="null"/> if not rated.
    /// </summary>
    public bool? UserRating { get; set; }

    /// <summary>
    /// Gets or sets the cumulative count of thumbs-up feedback received during the session.
    /// </summary>
    public int ThumbsUpCount { get; set; }

    /// <summary>
    /// Gets or sets the cumulative count of thumbs-down feedback received during the session.
    /// </summary>
    public int ThumbsDownCount { get; set; }

    /// <summary>
    /// Gets or sets the conversion score achieved during the session, if applicable.
    /// </summary>
    public int? ConversionScore { get; set; }

    /// <summary>
    /// Gets or sets the maximum possible conversion score for the session, if applicable.
    /// </summary>
    public int? ConversionMaxScore { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when this metrics record was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="AIChatSessionEvent"/> documents
/// to <see cref="AIChatSessionMetricsIndex"/> entries in the AI collection.
/// </summary>
public sealed class AIChatSessionMetricsIndexProvider : IndexProvider<AIChatSessionEvent>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIChatSessionMetricsIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AIChatSessionMetricsIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<AIChatSessionEvent> context)
    {
        context.For<AIChatSessionMetricsIndex>()
            .Map(evt => new AIChatSessionMetricsIndex
            {
                SessionId = evt.SessionId,
                ProfileId = evt.ProfileId,
                VisitorId = evt.VisitorId,
                UserId = evt.UserId,
                IsAuthenticated = evt.IsAuthenticated,
                SessionStartedUtc = evt.SessionStartedUtc,
                SessionEndedUtc = evt.SessionEndedUtc,
                MessageCount = evt.MessageCount,
                HandleTimeSeconds = evt.HandleTimeSeconds,
                IsResolved = evt.IsResolved,
                HourOfDay = evt.SessionStartedUtc.Hour,
                DayOfWeek = (int)evt.SessionStartedUtc.DayOfWeek,
                TotalInputTokens = evt.TotalInputTokens,
                TotalOutputTokens = evt.TotalOutputTokens,
                AverageResponseLatencyMs = evt.AverageResponseLatencyMs,
                CompletionCount = evt.CompletionCount,
                UserRating = evt.UserRating,
                ThumbsUpCount = evt.ThumbsUpCount,
                ThumbsDownCount = evt.ThumbsDownCount,
                ConversionScore = evt.ConversionScore,
                ConversionMaxScore = evt.ConversionMaxScore,
                CreatedUtc = evt.CreatedUtc,
            });
    }
}
