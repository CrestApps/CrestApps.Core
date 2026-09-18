using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// Assigns captions to figures by scoring every plausible pairing and taking the cheapest ones first.
/// </summary>
/// <remarks>
/// The score is a cost, so lower is better. It adds up the vertical gap measured in lines, how far the
/// caption sticks out horizontally from the figure, whether it is even in the same column, and whether it
/// sits on the side this document puts captions on. Any pairing with body text between the two is
/// disqualified outright: a caption never has a paragraph of prose between it and what it captions.
/// </remarks>
public sealed class DefaultFigureCaptionResolver : IFigureCaptionResolver
{
    private readonly IOptions<CaptionPatternOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultFigureCaptionResolver"/> class.
    /// </summary>
    /// <param name="options">The caption options.</param>
    public DefaultFigureCaptionResolver(IOptions<CaptionPatternOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// Assigns captions to figures.
    /// </summary>
    /// <param name="page">The measured page.</param>
    /// <param name="candidates">The caption candidates on that page.</param>
    /// <param name="prior">The direction captions sit in, per family, for this document.</param>
    /// <returns>The assignments. A figure with no confident caption is simply absent.</returns>
    public IReadOnlyList<CaptionAssignment> Resolve(
        FigureCaptionPageContext page,
        IReadOnlyList<CaptionCandidate> candidates,
        CaptionDirectionPrior prior)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (candidates == null || candidates.Count == 0 || page.Images.Count == 0)
        {
            return [];
        }

        var options = _options.Value;
        var blockers = GetBlockers(page, candidates);
        var pairs = new List<(IngestionDocumentImage Image, CaptionCandidate Candidate, double Cost)>();

        foreach (var image in page.Images)
        {
            var imageBounds = FigureGeometry.GetBounds(image);

            if (imageBounds == null)
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                var cost = GetCost(page, imageBounds, candidate, prior, blockers);

                if (double.IsPositiveInfinity(cost) || cost > options.MaxAssignmentCost)
                {
                    continue;
                }

                pairs.Add((image, candidate, cost));
            }
        }

        var assignments = new List<CaptionAssignment>();
        var usedImages = new HashSet<IngestionDocumentImage>();
        var usedCandidates = new HashSet<CaptionCandidate>();

        foreach (var pair in pairs.OrderBy(pair => pair.Cost))
        {
            if (!usedImages.Add(pair.Image))
            {
                continue;
            }

            if (!usedCandidates.Add(pair.Candidate))
            {
                usedImages.Remove(pair.Image);

                continue;
            }

            assignments.Add(new CaptionAssignment
            {
                Image = pair.Image,
                Candidate = pair.Candidate,
            });
        }

        return assignments;
    }

    private static double GetCost(
        FigureCaptionPageContext page,
        double[] imageBounds,
        CaptionCandidate candidate,
        CaptionDirectionPrior prior,
        IReadOnlyList<double[]> blockers)
    {
        var gap = FigureGeometry.GetVerticalGap(imageBounds, candidate.Bounds) / Math.Max(1, page.ModalLineHeight);
        var overlap = 1 - FigureGeometry.GetHorizontalOverlapRatio(imageBounds, candidate.Bounds);
        var sameColumn = FigureGeometry.IsCentreWithinRange(candidate.Bounds, imageBounds) ? 0 : 1;

        if (IsBlocked(imageBounds, candidate.Bounds, blockers))
        {
            return double.PositiveInfinity;
        }

        return gap + (2 * overlap) + sameColumn + GetPriorCost(imageBounds, candidate, prior);
    }

    private static double GetPriorCost(double[] imageBounds, CaptionCandidate candidate, CaptionDirectionPrior prior)
    {
        var learned = prior?.GetDirection(candidate.Bucket) ?? CaptionDirection.Unknown;

        if (learned == CaptionDirection.Unknown)
        {
            return 0.25;
        }

        var actual = FigureGeometry.GetDirection(imageBounds, candidate.Bounds);

        return actual == learned ? 0 : 0.5;
    }

    /// <summary>
    /// Collects the text on the page that is not a caption. A paragraph of prose lying between a figure and
    /// a candidate proves they do not belong together.
    /// </summary>
    /// <param name="page">The measured page.</param>
    /// <param name="candidates">The caption candidates.</param>
    /// <returns>The bounds of every non-candidate paragraph.</returns>
    private static List<double[]> GetBlockers(FigureCaptionPageContext page, IReadOnlyList<CaptionCandidate> candidates)
    {
        var candidateElements = new HashSet<IngestionDocumentElement>(candidates.Select(candidate => candidate.Element));
        var blockers = new List<double[]>();

        foreach (var paragraph in page.Paragraphs)
        {
            if (candidateElements.Contains(paragraph))
            {
                continue;
            }

            var bounds = FigureGeometry.GetBounds(paragraph);

            if (bounds != null)
            {
                blockers.Add(bounds);
            }
        }

        return blockers;
    }

    private static bool IsBlocked(double[] imageBounds, double[] candidateBounds, IReadOnlyList<double[]> blockers)
    {
        var lower = Math.Min(imageBounds[1], candidateBounds[1]);
        var upper = Math.Max(imageBounds[3], candidateBounds[3]);
        var innerLower = Math.Min(imageBounds[3], candidateBounds[3]);
        var innerUpper = Math.Max(imageBounds[1], candidateBounds[1]);

        if (innerLower >= innerUpper)
        {
            // The figure and the candidate overlap vertically, so there is no band between them.
            return false;
        }

        foreach (var blocker in blockers)
        {
            if (blocker[3] <= innerLower || blocker[1] >= innerUpper)
            {
                continue;
            }

            if (blocker[1] < lower || blocker[3] > upper)
            {
                continue;
            }

            if (FigureGeometry.GetHorizontalOverlapRatio(imageBounds, blocker) > 0)
            {
                return true;
            }
        }

        return false;
    }
}
