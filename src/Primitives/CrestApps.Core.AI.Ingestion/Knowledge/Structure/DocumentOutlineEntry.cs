namespace CrestApps.Core.AI.Ingestion.Knowledge.Structure;

/// <summary>
/// One entry of a document's own outline: a title, how deeply it is nested, and the page it opens.
/// </summary>
/// <remarks>
/// An outline is the rare case of a document stating its structure rather than implying it. Every other
/// signal this library reads — type size, a contents page, where a heading sits — is inference that can be
/// wrong about a layout nobody anticipated. A manual, a report or a book that carries an outline has already
/// answered the question, so a reader that can see one captures it and the analyzer prefers it over anything
/// it would otherwise work out for itself.
/// </remarks>
public sealed class DocumentOutlineEntry
{
    /// <summary>
    /// Gets the title as the outline states it.
    /// </summary>
    public string Title { get; init; }

    /// <summary>
    /// Gets how deeply the entry is nested, starting at zero for a top-level entry.
    /// </summary>
    /// <remarks>
    /// This is the depth the document declares, which is not always a tidy sequence: an outline may skip
    /// from a top-level entry straight to a third-level one. Consumers normalize it rather than trusting it
    /// to step by one.
    /// </remarks>
    public int Level { get; init; }

    /// <summary>
    /// Gets the one-based position in the file of the page the entry opens.
    /// </summary>
    /// <remarks>
    /// This is the position in the file, not the number printed on the page. The two differ in almost every
    /// publication that has front matter.
    /// </remarks>
    public int PageNumber { get; init; }
}
