using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Ingestion.Processors;

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
