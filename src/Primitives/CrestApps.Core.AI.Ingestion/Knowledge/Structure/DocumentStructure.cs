namespace CrestApps.Core.AI.Ingestion.Knowledge.Structure;

/// <summary>
/// What a document turned out to be made of.
/// </summary>
/// <remarks>
/// A file is not always one piece of writing. A magazine is thirty, each with its own title, its own author
/// and its own subject, and treating them as one document makes every one of them harder to find. A manual
/// or a contract really is one piece, and says so by having exactly one article here.
/// </remarks>
public sealed class DocumentStructure
{
    /// <summary>
    /// Gets the articles the document is made of. Never empty: a document nothing could be inferred about is
    /// one article.
    /// </summary>
    public IReadOnlyList<DocumentArticle> Articles { get; init; } = [];

    /// <summary>
    /// Gets the page number printed on each page, keyed by the page's one-based position in the file.
    /// </summary>
    /// <remarks>
    /// The two are rarely the same. A citation that names the position in the file sends a reader to the
    /// wrong page of the printed document.
    /// </remarks>
    public IReadOnlyDictionary<int, string> Folios { get; init; } = new Dictionary<int, string>();

    /// <summary>
    /// Gets a value indicating whether anything was actually inferred, as opposed to the whole file being
    /// treated as one article because nothing could be.
    /// </summary>
    public bool IsInferred { get; init; }
}

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
