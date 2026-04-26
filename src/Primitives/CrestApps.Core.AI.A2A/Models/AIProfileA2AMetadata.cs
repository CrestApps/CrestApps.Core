namespace CrestApps.Core.AI.A2A.Models;

/// <summary>
/// Stores the selected A2A connection IDs on an AI profile, template, or chat interaction.
/// </summary>
public sealed class AIProfileA2AMetadata
{
    /// <summary>
    /// Gets or sets the connection I Ds.
    /// </summary>
    public string[] ConnectionIds { get; set; }
}
