namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents the general AI Options.
/// </summary>
public sealed class GeneralAIOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether enables AI Usage Tracking.
    /// </summary>
    public bool EnableAIUsageTracking { get; set; }

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
