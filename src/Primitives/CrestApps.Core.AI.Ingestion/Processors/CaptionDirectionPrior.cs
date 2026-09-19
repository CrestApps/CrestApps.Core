namespace CrestApps.Core.AI.Ingestion.Processors;

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
