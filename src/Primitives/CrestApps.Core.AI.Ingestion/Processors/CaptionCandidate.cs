using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// A paragraph that could be a caption.
/// </summary>
public sealed class CaptionCandidate
{
    /// <summary>
    /// Gets the paragraph.
    /// </summary>
    public IngestionDocumentElement Element { get; init; }

    /// <summary>
    /// Gets the paragraph text.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    /// Gets the caption family the paragraph belongs to. See <see cref="CaptionBuckets"/>.
    /// </summary>
    public string Bucket { get; init; }

    /// <summary>
    /// Gets a value indicating whether the paragraph matched a configured caption pattern, rather than
    /// merely looking like a caption.
    /// </summary>
    public bool MatchedPattern { get; init; }

    /// <summary>
    /// Gets the number printed in the caption, when it is numbered.
    /// </summary>
    public int? Ordinal { get; init; }

    /// <summary>
    /// Gets the paragraph bounds as left, bottom, right and top.
    /// </summary>
    public double[] Bounds { get; init; }
}
