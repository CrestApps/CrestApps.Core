namespace CrestApps.Core.AI.Documents.Tooling;

/// <summary>
/// The user-provided settings for a knowledge object listing tool instance.
/// </summary>
public sealed class KnowledgeObjectListToolSettings
{
    /// <summary>
    /// Gets or sets the identifier of the AI data source this instance lists from.
    /// </summary>
    public string DataSourceId { get; set; }

    /// <summary>
    /// Gets or sets the most objects one listing returns. Values below one fall back to
    /// <see cref="KnowledgeObjectListToolConstants.DefaultMaxResults"/>, and anything above
    /// <see cref="KnowledgeObjectListToolConstants.MaxAllowedResults"/> is clamped to it.
    /// </summary>
    public int MaxResults { get; set; } = KnowledgeObjectListToolConstants.DefaultMaxResults;
}
