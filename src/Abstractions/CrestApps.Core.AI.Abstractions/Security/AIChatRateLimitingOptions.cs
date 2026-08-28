namespace CrestApps.Core.AI.Security;

/// <summary>
/// Configures how AI chat rate-limit keys are partitioned.
/// </summary>
public sealed class AIChatRateLimitingOptions
{
    /// <summary>
    /// Gets or sets the key partitions used for authenticated chat-message throttling.
    /// </summary>
    /// <remarks>
    /// <see cref="ChatRateLimitPartition.NetworkAddress"/> is included by default so an authenticated
    /// caller is throttled on both their user identity and their network address. The network-address
    /// bucket is shared with anonymous throttling, so a caller cannot shed their per-IP allowance by
    /// logging out. Remove the flag to key authenticated callers by user identity only.
    /// </remarks>
    public ChatRateLimitPartition AuthenticatedMessagePartitions { get; set; } =
        ChatRateLimitPartition.AuthenticatedUser |
        ChatRateLimitPartition.NetworkAddress;

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
