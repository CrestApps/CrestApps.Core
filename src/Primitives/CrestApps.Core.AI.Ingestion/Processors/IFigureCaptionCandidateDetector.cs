namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// Decides which paragraphs on a page could be captions. Replace the default to teach the processor a
/// layout it does not recognize, without forking the processor itself.
/// </summary>
public interface IFigureCaptionCandidateDetector
{
    /// <summary>
    /// Finds the caption candidates on one page.
    /// </summary>
    /// <param name="page">The measured page.</param>
    /// <returns>The candidates, in page order.</returns>
    IReadOnlyList<CaptionCandidate> GetCandidates(FigureCaptionPageContext page);
}
