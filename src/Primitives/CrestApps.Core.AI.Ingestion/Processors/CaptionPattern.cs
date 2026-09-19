using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// One caption pattern and the family it identifies.
/// </summary>
public sealed class CaptionPattern
{
    /// <summary>
    /// Gets the expression a caption paragraph must match.
    /// </summary>
    public Regex Expression { get; init; }

    /// <summary>
    /// Gets the caption family the match identifies. See <see cref="CaptionBuckets"/>.
    /// </summary>
    public string Bucket { get; init; }
}
