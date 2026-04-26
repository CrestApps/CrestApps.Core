namespace CrestApps.Core.AI.Memory;

/// <summary>
/// Represents the AI Memory Options.
/// </summary>
public sealed class AIMemoryOptions
{
    /// <summary>
    /// Gets or sets the index Profile Name.
    /// </summary>
    public string IndexProfileName { get; set; }

    /// <summary>
    /// Gets or sets the top N.
    /// </summary>
    public int TopN { get; set; } = 5;
}
