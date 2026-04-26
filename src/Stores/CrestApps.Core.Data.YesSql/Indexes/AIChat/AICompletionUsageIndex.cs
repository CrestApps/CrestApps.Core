using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

/// <summary>
/// YesSql map index for <see cref="AICompletionUsageRecord"/>, storing per-completion
/// usage and performance data to support billing, analytics, and audit queries.
/// </summary>
public sealed class AICompletionUsageIndex : MapIndex
{
    /// <summary>
    /// Gets or sets the context type that triggered this completion (e.g., chat session, interaction).
    /// </summary>
    public string ContextType { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the chat session associated with this completion, if applicable.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the AI profile used for this completion.
    /// </summary>
    public string ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the chat interaction associated with this completion, if applicable.
    /// </summary>
    public string InteractionId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the authenticated user who triggered the completion.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the user who triggered the completion.
    /// </summary>
    public string UserName { get; set; }

    /// <summary>
    /// Gets or sets the anonymous visitor identifier for unauthenticated completions.
    /// </summary>
    public string VisitorId { get; set; }

    /// <summary>
    /// Gets or sets the client identifier from which the completion was requested.
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the completion was requested by an authenticated user.
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// Gets or sets the display name of the client application that triggered the completion.
    /// </summary>
    public string ClientName { get; set; }

    /// <summary>
    /// Gets or sets the name of the AI provider connection used for the completion.
    /// </summary>
    public string ConnectionName { get; set; }

    /// <summary>
    /// Gets or sets the name of the AI deployment used for the completion.
    /// </summary>
    public string DeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the name of the underlying model used for the completion.
    /// </summary>
    public string ModelName { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the completion response returned by the provider.
    /// </summary>
    public string ResponseId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the completion response was streamed.
    /// </summary>
    public bool IsStreaming { get; set; }

    /// <summary>
    /// Gets or sets the number of input tokens consumed by this completion.
    /// </summary>
    public int InputTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the number of output tokens produced by this completion.
    /// </summary>
    public int OutputTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the total token count (input + output) for this completion.
    /// </summary>
    public int TotalTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the end-to-end response latency in milliseconds for this completion.
    /// </summary>
    public double ResponseLatencyMs { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when this completion was recorded.
    /// </summary>
    public DateTime CreatedUtc { get; set; }
}

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
