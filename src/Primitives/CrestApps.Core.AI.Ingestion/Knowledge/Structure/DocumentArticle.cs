namespace CrestApps.Core.AI.Ingestion.Knowledge.Structure;

/// <summary>
/// One article within a document.
/// </summary>
public sealed class DocumentArticle
{
    /// <summary>
    /// Gets the one-based position of the article within its document.
    /// </summary>
    public int Ordinal { get; init; }

    /// <summary>
    /// Gets the article's title.
    /// </summary>
    public string Title { get; init; }

    /// <summary>
    /// Gets the authors credited with the article.
    /// </summary>
    public IReadOnlyList<string> Authors { get; init; } = [];

    /// <summary>
    /// Gets the running-head label that says what kind of section the article sits in.
    /// </summary>
    public string SectionLabel { get; init; }

    /// <summary>
    /// Gets what kind of article this is. See <see cref="KnowledgeArticleTypes"/>.
    /// </summary>
    public string Type { get; init; } = KnowledgeArticleTypes.Article;

    /// <summary>
    /// Gets the one-based position in the file of the article's first page.
    /// </summary>
    public int PageStart { get; init; }

    /// <summary>
    /// Gets the one-based position in the file of the article's last page.
    /// </summary>
    public int PageEnd { get; init; }
}
