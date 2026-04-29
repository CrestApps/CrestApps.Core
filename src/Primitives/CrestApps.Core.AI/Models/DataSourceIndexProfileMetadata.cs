namespace CrestApps.Core.AI.Models;

/// <summary>
/// Metadata for data source embedding index profiles that stores embedding configuration.
/// </summary>
public sealed class DataSourceIndexProfileMetadata
{
    /// <summary>
    /// Gets or sets the unique deployment name for the embedding service.
    /// </summary>
    public string EmbeddingDeploymentName { get; set; }
}
