namespace CrestApps.Core.AI.Ingestion.Knowledge.Structure;

/// <summary>
/// Where the built-in strategies sit in the order of authority.
/// </summary>
/// <remarks>
/// The gaps are deliberate. A host that knows something about its own corpus can place a strategy between
/// two built-in ones without renumbering them, which is the difference between contributing a rung and
/// forking the ladder.
/// </remarks>
public static class DocumentStructureOrders
{
    /// <summary>The document's own outline.</summary>
    public const int Outline = 100;

    /// <summary>Heading levels the document states on its elements.</summary>
    public const int StatedHeadings = 200;

    /// <summary>A table of contents printed inside the document.</summary>
    public const int TableOfContents = 300;

    /// <summary>Headings inferred from type size and position.</summary>
    public const int InferredHeadings = 400;
}

/// <summary>
/// Divides a document by the outline it carries.
/// </summary>
/// <remarks>
/// The most authoritative rung there is, because an outline is the document stating its own structure with
/// its own nesting and the page each entry opens. A manual, a technical report or a book almost always
/// carries one; a magazine or a newspaper almost never does, and falls through.
/// </remarks>
public sealed class OutlineStructureStrategy : IDocumentStructureStrategy
{
    /// <inheritdoc />
    public string Source => DocumentStructureSources.Outline;

    /// <inheritdoc />
    public int Order => DocumentStructureOrders.Outline;

    /// <inheritdoc />
    public IReadOnlyList<DocumentArticle> Divide(DocumentStructureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return DocumentStructureRungs.BuildFromOutline(
            DocumentStructureRungs.ReadOutline(context.Document),
            context.PageCount);
    }
}

/// <summary>
/// Divides a document by the heading levels its elements state.
/// </summary>
/// <remarks>
/// A Word paragraph styled <c>Heading 2</c>, an <c>h2</c>, a tagged PDF's <c>H2</c> and a layout service's
/// section heading are one fact written four ways. Every reader records it the same way, so this one rung
/// serves every format that states its headings rather than implying them.
/// </remarks>
public sealed class StatedHeadingStructureStrategy : IDocumentStructureStrategy
{
    /// <inheritdoc />
    public string Source => DocumentStructureSources.StatedHeadings;

    /// <inheritdoc />
    public int Order => DocumentStructureOrders.StatedHeadings;

    /// <inheritdoc />
    public IReadOnlyList<DocumentArticle> Divide(DocumentStructureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return DocumentStructureRungs.BuildFromHeadingLevels(context.Document, context.PageCount);
    }
}

/// <summary>
/// Divides a document by a table of contents printed inside it.
/// </summary>
/// <remarks>
/// A contents page states the titles and the pages they are on, which turns "where does an article begin"
/// from a judgement into a fuzzy string match. This is the magazine rung, and the only one that knows what
/// an advertisement is: a page with no article and no running head means something in a magazine and nothing
/// in a manual, so the notion is kept here rather than applied to every document.
/// </remarks>
public sealed class TableOfContentsStructureStrategy : IDocumentStructureStrategy
{
    /// <inheritdoc />
    public string Source => DocumentStructureSources.TableOfContents;

    /// <inheritdoc />
    public int Order => DocumentStructureOrders.TableOfContents;

    /// <inheritdoc />
    public IReadOnlyList<DocumentArticle> Divide(DocumentStructureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return DocumentStructureRungs.BuildFromTableOfContents(
            context.Document,
            context.SectionLabels,
            context.PageCount);
    }
}

/// <summary>
/// Divides a document by the headings its type size implies.
/// </summary>
/// <remarks>
/// The last rung before giving up, and the only one that guesses. A contents page is an answer key, not a
/// precondition: plenty of publications have none that can be read, and answering "one article" for a
/// twenty-three page magazine is a worse answer than reading its own headings.
/// </remarks>
public sealed class InferredHeadingStructureStrategy : IDocumentStructureStrategy
{
    /// <inheritdoc />
    public string Source => DocumentStructureSources.InferredHeadings;

    /// <inheritdoc />
    public int Order => DocumentStructureOrders.InferredHeadings;

    /// <inheritdoc />
    public IReadOnlyList<DocumentArticle> Divide(DocumentStructureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return DocumentStructureRungs.BuildFromInferredHeadings(
            context.Document,
            context.SectionLabels,
            context.PageCount);
    }
}
