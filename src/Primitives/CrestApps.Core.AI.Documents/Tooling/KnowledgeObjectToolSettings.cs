namespace CrestApps.Core.AI.Documents.Tooling;

/// <summary>
/// The user-provided settings for a knowledge object tool instance.
/// </summary>
public sealed class KnowledgeObjectToolSettings
{
    /// <summary>
    /// Gets or sets the identifier of the AI data source this instance reads from.
    /// </summary>
    public string DataSourceId { get; set; }
}
