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
