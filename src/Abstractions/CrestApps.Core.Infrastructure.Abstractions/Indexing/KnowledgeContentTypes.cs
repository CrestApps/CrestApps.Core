namespace CrestApps.Core.Infrastructure.Indexing;

/// <summary>
/// What kind of knowledge one knowledge-base row holds.
/// </summary>
/// <remarks>
/// A page carrying five hundred words, a chart and a table is four pieces of knowledge, not one. Storing them
/// as separate rows with a type on each is what lets retrieval return "the chart on page 8" as its own hit
/// instead of burying it in one embedding averaged over everything on the page.
/// <para>
/// The same values name the object types in the knowledge store, so the store and the index never disagree
/// about what something is.
/// </para>
/// </remarks>
public static class KnowledgeContentTypes
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
