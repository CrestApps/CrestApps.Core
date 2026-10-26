namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents the general AI Settings.
/// </summary>
public sealed class GeneralAISettings
{
    /// <summary>
    /// Gets or sets a value indicating whether enables AI Usage Tracking.
    /// </summary>
    [Obsolete("AI usage tracking is no longer a setting and this value is ignored. It is kept so a stored value can be read by a migration. Register tracking with AddCoreAIUsageTracking() instead.")]
    public bool EnableAIUsageTracking { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether enables Analytics.
    /// </summary>
#pragma warning disable CS0618 // Forwards to the obsolete stored value.
    [Obsolete("Use AddCoreAIUsageTracking() instead.")]
    public bool EnableAnalytics
    {
        get => EnableAIUsageTracking;
        set => EnableAIUsageTracking = value;
    }
#pragma warning restore CS0618

    /// <summary>
    /// Gets or sets a value indicating whether enables Preemptive Memory Retrieval.
    /// </summary>
    public bool EnablePreemptiveMemoryRetrieval { get; set; } = true;

    /// <summary>
    /// Gets or sets the override Maximum Iterations Per Request.
    /// </summary>
    public bool OverrideMaximumIterationsPerRequest { get; set; }

    /// <summary>
    /// Gets or sets the maximum Iterations Per Request.
    /// </summary>
    public int MaximumIterationsPerRequest { get; set; } = 10;

    /// <summary>
    /// Gets or sets the override Enable Distributed Caching.
    /// </summary>
    public bool OverrideEnableDistributedCaching { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether enables Distributed Caching.
    /// </summary>
    public bool EnableDistributedCaching { get; set; } = true;

    /// <summary>
    /// Gets or sets the override Enable Open Telemetry.
    /// </summary>
    public bool OverrideEnableOpenTelemetry { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether enables Open Telemetry.
    /// </summary>
    public bool EnableOpenTelemetry { get; set; }
}
