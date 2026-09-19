namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Public descriptor for a registered orchestrator, exposing only metadata.
/// </summary>
public sealed class OrchestratorDescriptor
{
    /// <summary>
    /// Gets or sets the display title. When <see langword="null"/>, the name should be used.
    /// </summary>
    public string Title { get; set; }
}
