namespace CrestApps.Core.AI.Security;

/// <summary>
/// A single tier of a multi-tier sliding-window rate limit: at most <see cref="Limit"/> events are
/// permitted within any <see cref="Window"/>. Multiple tiers are evaluated together so a request is
/// throttled when it would exceed <em>any</em> tier — for example a short burst tier (5 / 30s)
/// alongside longer sustained tiers (150 / hour, 500 / day).
/// </summary>
public sealed class ChatRateLimitTier
{
    /// <summary>
    /// Gets or sets the maximum number of events allowed within <see cref="Window"/>.
    /// Values of zero or less disable this tier.
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// Gets or sets the sliding-window duration for this tier.
    /// Values of zero or less disable this tier.
    /// </summary>
    public TimeSpan Window { get; set; }
}
