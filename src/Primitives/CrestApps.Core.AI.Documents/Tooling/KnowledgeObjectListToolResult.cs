using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Documents.Tooling;

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
