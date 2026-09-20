namespace CrestApps.Core.AI.Ingestion.Knowledge.Structure;

/// <summary>
/// What a document's structure was worked out from.
/// </summary>
/// <remarks>
/// A split that comes out wrong is far easier to argue with when the answer says what it was based on. The
/// first three are the document stating its own structure and the last two are this library inferring it,
/// which is the distinction worth knowing before anyone starts tuning a threshold that was never consulted.
/// </remarks>
public static class DocumentStructureSources
{
    /// <summary>
    /// The document's own outline: bookmarks, with their nesting and the page each opens.
    /// </summary>
    public const string Outline = "outline";

    /// <summary>
    /// Heading levels the document states on its elements: a Word style, an <c>h2</c>, a tagged PDF's
    /// <c>H2</c>, a layout service's section heading.
    /// </summary>
    public const string StatedHeadings = "statedHeadings";

    /// <summary>
    /// A table of contents inside the document, matched to the headings printed on its pages.
    /// </summary>
    public const string TableOfContents = "tableOfContents";

    /// <summary>
    /// Headings inferred from type size and position, because nothing stated any.
    /// </summary>
    public const string InferredHeadings = "inferredHeadings";

    /// <summary>
    /// Nothing could be worked out, so the document is one division covering all of it.
    /// </summary>
    public const string Whole = "whole";
}
