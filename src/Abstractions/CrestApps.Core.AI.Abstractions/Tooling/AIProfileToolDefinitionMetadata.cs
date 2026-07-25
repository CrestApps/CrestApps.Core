namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Profile metadata that records which configured <see cref="AIToolDefinition"/> entries are attached to
/// an AI profile (or other tool-bearing resource). Stored in the resource's properties bag.
/// </summary>
public sealed class AIProfileToolDefinitionMetadata
{
    /// <summary>
    /// Gets or sets the identifiers of the configured tool definitions available to the resource.
    /// </summary>
    public string[] DefinitionIds { get; set; }
}
