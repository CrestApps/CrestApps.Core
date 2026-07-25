namespace CrestApps.Core.AI.Security;

/// <summary>
/// Configures how AI chat rate-limit keys are partitioned.
/// </summary>
public sealed class AIChatRateLimitingOptions
{
    /// <summary>
    /// Gets or sets the key partitions used for authenticated chat-message throttling.
    /// </summary>
    public ChatRateLimitPartition AuthenticatedMessagePartitions { get; set; } = ChatRateLimitPartition.AuthenticatedUser;

    /// <summary>
    /// Gets or sets the key partitions used for anonymous chat-message throttling.
    /// </summary>
    public ChatRateLimitPartition AnonymousMessagePartitions { get; set; } =
        ChatRateLimitPartition.Visitor |
        ChatRateLimitPartition.NetworkAddress |
        ChatRateLimitPartition.Session |
        ChatRateLimitPartition.Connection;

    /// <summary>
    /// Gets or sets the key partitions used for anonymous session-start throttling.
    /// </summary>
    public ChatRateLimitPartition AnonymousSessionStartPartitions { get; set; } =
        ChatRateLimitPartition.Visitor |
        ChatRateLimitPartition.NetworkAddress;
}
