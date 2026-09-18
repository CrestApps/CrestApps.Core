namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// Matches figures on a page to the captions that belong to them.
/// </summary>
public interface IFigureCaptionResolver
{
    /// <summary>
    /// Assigns captions to figures.
    /// </summary>
    /// <param name="page">The measured page.</param>
    /// <param name="candidates">The caption candidates on that page.</param>
    /// <param name="prior">The direction captions sit in, per family, for this document.</param>
    /// <returns>The assignments. A figure with no confident caption is simply absent.</returns>
    IReadOnlyList<CaptionAssignment> Resolve(
        FigureCaptionPageContext page,
        IReadOnlyList<CaptionCandidate> candidates,
        CaptionDirectionPrior prior);
}
