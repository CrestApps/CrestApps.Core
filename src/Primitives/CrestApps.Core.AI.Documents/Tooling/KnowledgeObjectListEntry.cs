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
    /// <see cref="CrestApps.Core.Infrastructure.Indexing.KnowledgeObjectTypes"/>.
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
