namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Describes a registered orchestrator including its implementation type and optional display title.
/// </summary>
public sealed class OrchestratorEntry
{
    /// <summary>
    /// Gets or sets the orchestrator implementation type.
    /// </summary>
    public Type Type { get; set; }

    /// <summary>
    /// Gets or sets the optional localized display title for this orchestrator.
    /// When <see langword="null"/> or empty, the orchestrator name is used in the UI.
    /// </summary>
    public string Title { get; set; }
}
