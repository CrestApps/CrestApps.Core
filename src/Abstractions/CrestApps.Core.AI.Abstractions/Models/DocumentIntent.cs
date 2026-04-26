namespace CrestApps.Core.AI.Models;

/// <summary>
/// Detected intent metadata.
/// </summary>
public sealed class DocumentIntent
{
    /// <summary>
    /// Gets or sets the name of the detected intent.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the confidence score for this intent (0.0-1.0).
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Gets or sets a natural-language explanation for why this intent was detected.
    /// </summary>
    public string Reason { get; set; }
}
