namespace CrestApps.Core.Infrastructure.Indexing;

/// <summary>
/// Describes a single index profile source entry, combining provider identity,
/// profile type, and human-readable display metadata.
/// </summary>
public sealed class IndexProfileSourceDescriptor
{
    /// <summary>
    /// Gets or sets the unique technical name of the search provider (e.g., "Elasticsearch", "AzureAISearch").
    /// </summary>
    public string ProviderName { get; set; }

    /// <summary>
    /// Gets or sets the human-readable display name of the search provider.
    /// </summary>
    public string ProviderDisplayName { get; set; }

    /// <summary>
    /// Gets or sets the index profile type this descriptor applies to
    /// (e.g., <see cref="IndexProfileTypes.AIDocuments"/>, <see cref="IndexProfileTypes.DataSource"/>).
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the human-readable display name shown in the UI for this source descriptor.
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets a short description of this source descriptor shown in the UI.
    /// </summary>
    public string Description { get; set; }
}
