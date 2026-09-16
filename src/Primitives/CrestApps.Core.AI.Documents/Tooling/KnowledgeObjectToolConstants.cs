namespace CrestApps.Core.AI.Documents.Tooling;

/// <summary>
/// Well-known identifiers for the built-in knowledge object tool instance source.
/// </summary>
public static class KnowledgeObjectToolConstants
{
    /// <summary>
    /// The registered source name.
    /// </summary>
    public const string SourceName = "knowledge-object";

    /// <summary>
    /// The category the source is grouped under.
    /// </summary>
    public const string Category = "Knowledgebase";
}

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
