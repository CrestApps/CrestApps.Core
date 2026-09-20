namespace CrestApps.Core.AI.Ingestion.Knowledge.Structure;

/// <summary>
/// One division of a document, and the divisions nested inside it.
/// </summary>
/// <remarks>
/// "Article" is what a magazine calls its top-level division; a manual calls it a chapter and a report calls
/// it a part. The type keeps the one name because the word is written into stored rows and read back
/// verbatim, so what it means is "a top-level division, whatever this genre calls one".
/// <para>
/// A division nested inside another is the same type at a greater <see cref="Depth"/>. Keeping them in one
/// ordered, flat list with a depth on each — rather than a list of children — is what lets every existing
/// consumer walk the document in reading order without knowing whether it is nested at all.
/// </para>
/// </remarks>
public sealed class DocumentArticle
{
    /// <summary>
    /// Gets the one-based position of the article within its document.
    /// </summary>
    public int Ordinal { get; init; }

    /// <summary>
    /// Gets how deeply the division is nested, starting at one for a top-level division.
    /// </summary>
    /// <remarks>
    /// Everything that infers structure from a page's appearance produces depth one, because a type size
    /// says something is a heading and nothing about what it is a heading inside of. Only a source that
    /// states nesting — a document's outline, or tagged content — produces anything deeper.
    /// </remarks>
    public int Depth { get; init; } = 1;

    /// <summary>
    /// Gets the <see cref="Ordinal"/> of the division this one sits inside, or zero when it is top level.
    /// </summary>
    public int ParentOrdinal { get; init; }

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
