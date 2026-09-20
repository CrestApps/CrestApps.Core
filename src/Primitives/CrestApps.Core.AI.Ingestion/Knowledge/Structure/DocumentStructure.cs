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

    /// <summary>
    /// Gets what the structure was worked out from. See <see cref="DocumentStructureSources"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="IsInferred"/> says whether anything was found; this says what found it. The two are not
    /// the same question, and the second is the one worth asking when a document comes out divided wrongly.
    /// </remarks>
    public string Source { get; init; } = DocumentStructureSources.Whole;
}
