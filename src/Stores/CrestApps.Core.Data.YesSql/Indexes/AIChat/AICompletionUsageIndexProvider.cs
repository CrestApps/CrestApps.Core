using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

/// <summary>
/// YesSql index provider that maps <see cref="AICompletionUsageRecord"/> documents
/// to <see cref="AICompletionUsageIndex"/> entries in the AI collection.
/// </summary>
public sealed class AICompletionUsageIndexProvider : IndexProvider<AICompletionUsageRecord>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AICompletionUsageIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AICompletionUsageIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<AICompletionUsageRecord> context)
    {
        context.For<AICompletionUsageIndex>()
            .Map(record => new AICompletionUsageIndex
            {
                ContextType = record.ContextType,
                SessionId = record.SessionId,
                ProfileId = record.ProfileId,
                InteractionId = record.InteractionId,
                UserId = record.UserId,
                UserName = record.UserName,
                VisitorId = record.VisitorId,
                ClientId = record.ClientId,
                IsAuthenticated = record.IsAuthenticated,
                ClientName = record.ClientName,
                ConnectionName = record.ConnectionName,
                DeploymentName = record.DeploymentName,
                ModelName = record.ModelName,
                ResponseId = record.ResponseId,
                IsStreaming = record.IsStreaming,
                InputTokenCount = record.InputTokenCount,
                OutputTokenCount = record.OutputTokenCount,
                TotalTokenCount = record.TotalTokenCount,
                ResponseLatencyMs = record.ResponseLatencyMs,
                CreatedUtc = record.CreatedUtc,
            });
    }
}
