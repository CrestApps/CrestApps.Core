namespace CrestApps.Core.AI.Documents.Tooling;

/// <summary>
/// Well-known identifiers for the built-in knowledge object listing tool instance source.
/// </summary>
public static class KnowledgeObjectListToolConstants
{
    /// <summary>
    /// The registered source name.
    /// </summary>
    public const string SourceName = "knowledge-object-list";

    /// <summary>
    /// The number of objects one listing returns when no instance setting says otherwise.
    /// </summary>
    public const int DefaultMaxResults = 50;

    /// <summary>
    /// The most objects one listing can ever return, whatever an instance is configured with.
    /// </summary>
    /// <remarks>
    /// A listing has no query to rank against, so nothing about the request itself limits how much comes
    /// back. Without a ceiling above the configured one, a data source holding a year of issues answers
    /// "list the tables" with every table it has, and the answer is a context window rather than a list.
    /// </remarks>
    public const int MaxAllowedResults = 200;

    /// <summary>
    /// The category the source is grouped under. It is the same category the get-by-id source uses, so both
    /// halves of reading a knowledge base sit together.
    /// </summary>
    public const string Category = KnowledgeObjectToolConstants.Category;
}
