namespace CrestApps.Core.AI.Security;

/// <summary>
/// Identifies which request attributes participate in AI chat rate-limit partitioning.
/// </summary>
[Flags]
public enum ChatRateLimitPartition
{
    /// <summary>
    /// No partitioning is applied.
    /// </summary>
    None = 0,

    /// <summary>
    /// Partition by authenticated user identifier.
    /// </summary>
    AuthenticatedUser = 1 << 0,

    /// <summary>
    /// Partition by the stable visitor identifier.
    /// </summary>
    Visitor = 1 << 1,

    /// <summary>
    /// Partition by the configured remote-address representation.
    /// </summary>
    NetworkAddress = 1 << 2,

    /// <summary>
    /// Partition by chat session identifier.
    /// </summary>
    Session = 1 << 3,

    /// <summary>
    /// Partition by connection identifier.
    /// </summary>
    Connection = 1 << 4,
}
