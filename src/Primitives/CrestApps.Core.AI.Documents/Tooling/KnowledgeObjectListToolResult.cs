using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Documents.Tooling;

/// <summary>
/// What one knowledge object contributes to a listing: enough to say what it is and to ask for it by
/// identifier, and never its content.
/// </summary>
/// <remarks>
/// A listing answers a question about a set, so it carries one line per object rather than each object in
/// full. The whole of any one of them is a single get-by-id call away.
/// </remarks>
public sealed class KnowledgeObjectListEntry
{
    /// <summary>
    /// Gets the canonical identifier, which is what the get-by-id tool takes.
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// Gets what kind of knowledge this is. See
    /// <see cref="CrestApps.Core.Infrastructure.Indexing.KnowledgeContentTypes"/>.
    /// </summary>
    public string ObjectType { get; init; }

    /// <summary>
    /// Gets the title used for citation.
    /// </summary>
    public string Title { get; init; }

    /// <summary>
    /// Gets the identifier of the object this hangs directly off.
    /// </summary>
    public string ParentId { get; init; }

    /// <summary>
    /// Gets the first page this was read from.
    /// </summary>
    public int? PageStart { get; init; }

    /// <summary>
    /// Gets the marker the host substitutes the picture for, such as <c>[fig:1]</c>. It is set only for a
    /// figure the host can actually serve, and only when there is an invocation to register it against.
    /// </summary>
    public string Label { get; init; }

    /// <summary>
    /// Gets the address the host serves the picture from. It is never shown to the model - the
    /// <see cref="Label"/> is - and is carried here so a caller other than a chat turn can still reach it.
    /// </summary>
    public string Link { get; init; }
}

/// <summary>
/// One listing of knowledge objects as a tool result: the rendered lines, and the entries they were
/// rendered from.
/// </summary>
/// <remarks>
/// The result is text only, on purpose. A listing of twenty figures that returned twenty pictures would
/// spend a whole vision budget answering "what figures are in this article", so a figure here is named and
/// labelled rather than sent, and the picture arrives when a label is written or the figure is read by
/// identifier.
/// </remarks>
public sealed class KnowledgeObjectListToolResult : IAIToolContentProvider
{
    /// <summary>
    /// Gets the text describing the listing.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    /// Gets the objects that were listed.
    /// </summary>
    public IReadOnlyList<KnowledgeObjectListEntry> Entries { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether more objects matched than the listing was allowed to return.
    /// </summary>
    /// <remarks>
    /// A listing that quietly returns a page is worse than one that returns nothing: the caller is handed a
    /// complete-looking answer to a question about a set, and counts it. Whenever this is
    /// <see langword="true"/>, <see cref="Text"/> says so in words as well.
    /// </remarks>
    public bool IsTruncated { get; init; }

    /// <summary>
    /// Returns the text, so the result also works as a plain chat tool result.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        return Text;
    }

    /// <summary>
    /// Renders the result as its text.
    /// </summary>
    /// <returns>The contents.</returns>
    public IReadOnlyList<AIContent> ToContents()
    {
        return [new TextContent(Text)];
    }
}
