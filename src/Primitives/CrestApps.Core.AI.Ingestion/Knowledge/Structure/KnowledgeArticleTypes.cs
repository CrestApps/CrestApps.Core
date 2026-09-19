namespace CrestApps.Core.AI.Ingestion.Knowledge.Structure;

/// <summary>
/// What kind of article a stretch of pages turned out to be.
/// </summary>
public static class KnowledgeArticleTypes
{
    /// <summary>
    /// Something written to be read. This is the default and what everything was before structure analysis.
    /// </summary>
    public const string Article = "article";

    /// <summary>
    /// A page that carries no article and no section label. It is stored so the document stays complete, and
    /// kept out of the index so it cannot be returned as an answer.
    /// </summary>
    public const string Advertisement = "advertisement";
}
