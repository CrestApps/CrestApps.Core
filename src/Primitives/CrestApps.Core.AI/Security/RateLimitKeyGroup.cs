namespace CrestApps.Core.AI.Security;

/// <summary>
/// A set of partition keys evaluated together against the same tiers, retaining timestamps for at
/// least <see cref="RetentionWindow"/>. Retention is separate from the tiers so a bucket shared with
/// another evaluation (for example an IP-hash key shared between authenticated and anonymous message
/// throttling) is never evicted more aggressively than the other evaluation needs.
/// </summary>
internal sealed class RateLimitKeyGroup
{
    public RateLimitKeyGroup(
        IReadOnlyList<string> keys,
        IReadOnlyList<ChatRateLimitTier> tiers,
        TimeSpan retentionWindow)
    {
        Keys = keys;
        Tiers = tiers;
        RetentionWindow = retentionWindow;
    }

    public IReadOnlyList<string> Keys { get; }

    public IReadOnlyList<ChatRateLimitTier> Tiers { get; }

    public TimeSpan RetentionWindow { get; }
}
