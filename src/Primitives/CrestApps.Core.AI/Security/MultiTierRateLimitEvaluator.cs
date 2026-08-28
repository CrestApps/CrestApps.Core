using System.Collections.Concurrent;

namespace CrestApps.Core.AI.Security;

/// <summary>
/// A per-key sliding-window bucket. Timestamps are stored oldest-first and shared across all tiers
/// evaluated for the key.
/// </summary>
internal sealed class SlidingWindowEntry
{
    public object Lock { get; } = new();

    public Queue<DateTimeOffset> Timestamps { get; } = new();
}

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

/// <summary>
/// Evaluates a request against one or more <see cref="ChatRateLimitTier"/> windows using in-memory
/// per-key sliding windows. A request is throttled when it would exceed any tier on any key; when
/// allowed, the request timestamp is recorded against every key.
/// </summary>
/// <remarks>
/// Designed for single-instance deployments, matching the existing default limiters. For distributed
/// deployments, replace the owning limiter with a Redis-backed or shared-state implementation.
/// </remarks>
internal static class MultiTierRateLimitEvaluator
{
    /// <summary>
    /// Normalizes a set of tiers by dropping entries with a non-positive limit or window.
    /// Returns an empty list when no tier is enforceable (rate limiting disabled).
    /// </summary>
    public static List<ChatRateLimitTier> Normalize(IEnumerable<ChatRateLimitTier> tiers)
    {
        var result = new List<ChatRateLimitTier>();

        if (tiers is null)
        {
            return result;
        }

        foreach (var tier in tiers)
        {
            if (tier is not null && tier.Limit > 0 && tier.Window > TimeSpan.Zero)
            {
                result.Add(tier);
            }
        }

        return result;
    }

    /// <summary>
    /// Checks the provided keys against every tier and, when none is exceeded, records the request.
    /// </summary>
    /// <param name="windows">The per-key sliding-window store.</param>
    /// <param name="keys">The partition keys to evaluate (for example visitor and IP-hash keys).</param>
    /// <param name="tiers">The normalized, enforceable tiers. Must not be empty.</param>
    /// <param name="now">The current timestamp.</param>
    /// <returns>
    /// A throttled <see cref="RateLimitResult"/> reporting the most constraining exceeded tier, or
    /// <see cref="RateLimitResult.Allowed"/> when the request was permitted and recorded.
    /// </returns>
    public static RateLimitResult Evaluate(
        ConcurrentDictionary<string, SlidingWindowEntry> windows,
        IReadOnlyList<string> keys,
        IReadOnlyList<ChatRateLimitTier> tiers,
        DateTimeOffset now)
    {
        var maxWindow = MaxWindow(tiers);

        return Evaluate(windows, [new RateLimitKeyGroup(keys, tiers, maxWindow)], now);
    }

    /// <summary>
    /// Checks each group's keys against that group's tiers and, when none is exceeded, records the
    /// request against every key in every group. Groups are evaluated together so the request is
    /// permitted only if it passes all of them, and recorded only once per key.
    /// </summary>
    /// <param name="windows">The per-key sliding-window store.</param>
    /// <param name="groups">The key groups to evaluate.</param>
    /// <param name="now">The current timestamp.</param>
    public static RateLimitResult Evaluate(
        ConcurrentDictionary<string, SlidingWindowEntry> windows,
        IReadOnlyList<RateLimitKeyGroup> groups,
        DateTimeOffset now)
    {
        // Phase 1: evaluate every key in every group before recording anything.
        foreach (var group in groups)
        {
            if (group.Tiers.Count == 0)
            {
                continue;
            }

            var oldestRetained = now - group.RetentionWindow;

            foreach (var key in group.Keys)
            {
                var entry = windows.GetOrAdd(key, static _ => new SlidingWindowEntry());

                lock (entry.Lock)
                {
                    // Evict only timestamps older than this group's retention window. A bucket shared
                    // with a wider-retention group keeps that group's history intact.
                    while (entry.Timestamps.Count > 0 && entry.Timestamps.Peek() <= oldestRetained)
                    {
                        entry.Timestamps.Dequeue();
                    }

                    var throttled = EvaluateTiers(entry, group.Tiers, now);

                    if (throttled is not null)
                    {
                        return throttled;
                    }
                }
            }
        }

        // Phase 2: the request is permitted; record it against each distinct key.
        var recorded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            if (group.Tiers.Count == 0)
            {
                continue;
            }

            foreach (var key in group.Keys)
            {
                if (!recorded.Add(key))
                {
                    continue;
                }

                var entry = windows.GetOrAdd(key, static _ => new SlidingWindowEntry());

                lock (entry.Lock)
                {
                    entry.Timestamps.Enqueue(now);
                }
            }
        }

        return RateLimitResult.Allowed;
    }

    /// <summary>
    /// Returns the widest window across the provided tiers.
    /// </summary>
    public static TimeSpan MaxWindow(IEnumerable<ChatRateLimitTier> tiers)
    {
        var maxWindow = TimeSpan.Zero;

        if (tiers is not null)
        {
            foreach (var tier in tiers)
            {
                if (tier is not null && tier.Window > maxWindow)
                {
                    maxWindow = tier.Window;
                }
            }
        }

        return maxWindow;
    }

    private static RateLimitResult EvaluateTiers(
        SlidingWindowEntry entry,
        IReadOnlyList<ChatRateLimitTier> tiers,
        DateTimeOffset now)
    {
        RateLimitResult mostConstraining = null;

        foreach (var tier in tiers)
        {
            var windowStart = now - tier.Window;
            var count = 0;
            var oldestInWindow = default(DateTimeOffset);
            var haveOldest = false;

            // Timestamps are oldest-first, so the first in-window entry is the oldest in this tier.
            foreach (var timestamp in entry.Timestamps)
            {
                if (timestamp > windowStart)
                {
                    if (!haveOldest)
                    {
                        oldestInWindow = timestamp;
                        haveOldest = true;
                    }

                    count++;
                }
            }

            if (count < tier.Limit)
            {
                continue;
            }

            var retryAfter = (int)Math.Ceiling((oldestInWindow + tier.Window - now).TotalSeconds);

            if (retryAfter < 1)
            {
                retryAfter = 1;
            }

            // Report the tier that keeps the caller waiting longest.
            if (mostConstraining is null || retryAfter > mostConstraining.RetryAfterSeconds)
            {
                mostConstraining = RateLimitResult.Throttled(retryAfter, count, tier.Limit);
            }
        }

        return mostConstraining;
    }
}
