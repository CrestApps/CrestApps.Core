namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// The caption families a numbered caption can belong to. They are kept apart because the direction a
/// caption sits in is a property of the family, not of the document: the same publisher routinely prints
/// figure captions below the artwork and table captions above it.
/// </summary>
public static class CaptionBuckets
{
    /// <summary>
    /// Artwork: a photograph, a chart, a diagram.
    /// </summary>
    public const string Figure = "figure";

    /// <summary>
    /// A table.
    /// </summary>
    public const string Table = "table";

    /// <summary>
    /// Nothing identified the family.
    /// </summary>
    public const string Unknown = "unknown";
}
