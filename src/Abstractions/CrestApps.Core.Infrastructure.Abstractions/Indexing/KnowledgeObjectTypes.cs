namespace CrestApps.Core.Infrastructure.Indexing;

/// <summary>
/// What kind of knowledge one knowledge-base row holds.
/// </summary>
/// <remarks>
/// A page carrying five hundred words, a chart and a table is four pieces of knowledge, not one. Storing them
/// as separate rows with a type on each is what lets retrieval return "the chart on page 8" as its own hit
/// instead of burying it in one embedding averaged over everything on the page.
/// <para>
/// These are the same values <see cref="CrestApps.Core.AI.Models.KnowledgeObject.ObjectType"/> carries, so the
/// store and the index never disagree about what something is. The name follows the store's word deliberately:
/// "content type" means a content item's type to anyone arriving from Orchard Core, which this library is used
/// from, and that is not what these are.
/// </para>
/// <para>
/// The indexed row still names the column <c>contentType</c>, and a data source search tool instance still
/// stores its selection under <c>ContentTypes</c>. Both are written into stored data and read back verbatim,
/// so they are pinned rather than renamed — the same reason the figure storage segment is still
/// <c>Ingested</c>. Renaming either means rewriting what is already stored, which is a migration rather than
/// a rename.
/// </para>
/// </remarks>
public static class KnowledgeObjectTypes
{
    /// <summary>
    /// An ingested file as a whole.
    /// </summary>
    public const string Document = "document";

    /// <summary>
    /// One article within a document. A document with no discernible structure has exactly one.
    /// </summary>
    public const string Article = "article";

    /// <summary>
    /// One division nested inside an article: a chapter's section, a manual's procedure, a paper's Methods.
    /// </summary>
    /// <remarks>
    /// A section exists only where a document states its own nesting, because nothing that infers structure
    /// from how a page looks can tell a section from an article. It is a separate type rather than another
    /// article so that retrieval can be asked for the part rather than the whole — "the Methods section of
    /// that paper" is a different answer from the paper.
    /// </remarks>
    public const string Section = "section";

    /// <summary>
    /// One chunk of article text. This is what every row written before typed knowledge existed is read as.
    /// </summary>
    public const string Text = "text";

    /// <summary>
    /// A figure: its caption, its surrounding context and, once transcribed, its description.
    /// </summary>
    public const string Figure = "figure";

    /// <summary>
    /// A figure that is a chart, which may additionally carry series values and how far they can be trusted.
    /// </summary>
    public const string Chart = "chart";

    /// <summary>
    /// A table: its caption, its columns and its rows.
    /// </summary>
    public const string Table = "table";
}
