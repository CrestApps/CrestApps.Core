namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Configuration for registered orchestrators.
/// </summary>
public sealed class OrchestratorOptions
{
    /// <summary>
    /// Gets or sets the default orchestrator name.
    /// When a profile or interaction does not specify an orchestrator, this name is used.
    /// </summary>
    public string DefaultOrchestratorName { get; set; } = DefaultOrchestrator.OrchestratorName;

    internal Dictionary<string, OrchestratorEntry> Orchestrators { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the registered orchestrators as a read-only collection of name/title pairs.
    /// </summary>
    public IReadOnlyDictionary<string, OrchestratorDescriptor> GetOrchestratorDescriptors()
    {
        return Orchestrators.ToDictionary(
            kvp => kvp.Key,
            kvp => new OrchestratorDescriptor { Title = kvp.Value.Title },
            StringComparer.OrdinalIgnoreCase);
    }
}
