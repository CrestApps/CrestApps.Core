namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Metadata that records which configured <see cref="AIToolInstance"/> entries are attached to a
/// tool-bearing resource (for example an AI profile or a chat interaction). Instances are referenced by
/// their unique <see cref="AIToolInstance.Name"/> so the reference stays stable and human-readable.
/// Stored in the resource's properties bag.
/// </summary>
public sealed class AIToolInstanceMetadata
{
    /// <summary>
    /// Gets or sets the unique names of the configured tool instances available to the resource.
    /// </summary>
    public string[] InstanceNames { get; set; }
}
