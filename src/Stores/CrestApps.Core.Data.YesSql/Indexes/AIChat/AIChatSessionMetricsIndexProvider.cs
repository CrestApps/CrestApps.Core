using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

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
