using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// One page, measured: what is on it and what its ordinary body text looks like. Caption detection compares
/// against the page's own typography, because "small type" only means anything relative to the page it sits on.
/// </summary>
public sealed class FigureCaptionPageContext
{
    /// <summary>
    /// Gets the one-based page number.
    /// </summary>
    public int PageNumber { get; init; }

    /// <summary>
    /// Gets the figures on the page.
    /// </summary>
    public IReadOnlyList<IngestionDocumentImage> Images { get; init; } = [];

    /// <summary>
    /// Gets the text elements on the page.
    /// </summary>
    public IReadOnlyList<IngestionDocumentElement> Paragraphs { get; init; } = [];

    /// <summary>
    /// Gets the font size most of the page's text is set in.
    /// </summary>
    public double ModalPointSize { get; init; }

    /// <summary>
    /// Gets the font most of the page's text is set in.
    /// </summary>
    public string ModalFontName { get; init; }

    /// <summary>
    /// Gets the page's typical line height, which distances are measured in so a gap means the same thing on
    /// a densely set page as on an airy one.
    /// </summary>
    public double ModalLineHeight { get; init; }
}

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

/// <summary>
/// One figure and the caption assigned to it.
/// </summary>
public sealed class CaptionAssignment
{
    /// <summary>
    /// Gets the figure.
    /// </summary>
    public IngestionDocumentImage Image { get; init; }

    /// <summary>
    /// Gets the caption assigned to it.
    /// </summary>
    public CaptionCandidate Candidate { get; init; }
}

/// <summary>
/// The direction captions sit in, per family, for one document.
/// </summary>
public sealed class CaptionDirectionPrior
{
    private readonly IReadOnlyDictionary<string, CaptionDirection> _directions;

    /// <summary>
    /// Initializes a new instance of the <see cref="CaptionDirectionPrior"/> class.
    /// </summary>
    /// <param name="directions">The direction learned or configured for each caption family.</param>
    public CaptionDirectionPrior(IReadOnlyDictionary<string, CaptionDirection> directions)
    {
        _directions = directions;
    }

    /// <summary>
    /// Gets the direction captions of the supplied family sit in.
    /// </summary>
    /// <param name="bucket">The caption family.</param>
    /// <returns>The direction, or <see cref="CaptionDirection.Unknown"/> when nothing is known.</returns>
    public CaptionDirection GetDirection(string bucket)
    {
        if (bucket == null || _directions == null)
        {
            return CaptionDirection.Unknown;
        }

        return _directions.TryGetValue(bucket, out var direction) ? direction : CaptionDirection.Unknown;
    }
}
