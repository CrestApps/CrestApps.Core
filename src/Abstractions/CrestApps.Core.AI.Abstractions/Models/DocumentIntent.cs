namespace CrestApps.Core.AI.Models;

/// <summary>
/// Detected intent metadata.
/// </summary>
public sealed class DocumentIntent
{
    public required string Name { get; set; }

    public float Confidence { get; set; }

    public string Reason { get; set; }
}
