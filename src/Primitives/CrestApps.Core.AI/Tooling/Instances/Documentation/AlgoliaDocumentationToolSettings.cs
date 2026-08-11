namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// The user-provided settings for an Algolia DocSearch documentation search tool instance. The settings
/// are persisted in the owning <see cref="CrestApps.Core.AI.Tooling.AIToolInstance.Properties"/> and bind
/// the produced function to a single Algolia index.
/// </summary>
public sealed class AlgoliaDocumentationToolSettings
{
    /// <summary>
    /// Gets or sets the Algolia application identifier.
    /// </summary>
    public string ApplicationId { get; set; }

    /// <summary>
    /// Gets or sets the protected Algolia search-only API key.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the Algolia index name to query.
    /// </summary>
    public string IndexName { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of results this instance returns for a single search. When not
    /// set, a built-in default is used.
    /// </summary>
    public int? MaxResults { get; set; }
}
