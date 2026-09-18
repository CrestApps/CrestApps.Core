using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Ingestion.Pdf.Services;

/// <summary>
/// One drawing a page builds out of vector geometry rather than placing as an image.
/// </summary>
internal sealed class DetectedVectorFigure
{
    /// <summary>
    /// Gets or sets the segments the drawing is made of.
    /// </summary>
    public IReadOnlyList<PdfSegment> Segments { get; set; } = [];

    /// <summary>
    /// Gets or sets the region the drawing occupies, as left, bottom, right and top in PDF user space.
    /// </summary>
    public double[] Bounds { get; set; } = [];
}

/// <summary>
/// Finds the figures a page draws instead of placing.
/// </summary>
/// <remarks>
/// A chart produced by a spreadsheet or a plotting library is usually not an image at all: the file contains
/// instructions for drawing it and nothing else. Reading only placed images misses every one of them, which
/// on a technical document is most of the figures.
/// <para>
/// A cluster is only a figure when it is dense, large, and sits where there is little text. A page's rules,
/// borders and table grids are all vector geometry too, and calling those figures would fill a knowledge
/// base with pictures of lines.
/// </para>
/// </remarks>
internal static class PdfVectorFigureDetector
{
    /// <summary>
    /// How many segments a cluster needs before it is a drawing rather than a rule or a box.
    /// </summary>
    private const int MinimumSegments = 20;

    /// <summary>
    /// How near two segments have to be to belong to the same drawing, as a fraction of the page width.
    /// </summary>
    private const double ClusterDistanceRatio = 0.04;

    /// <summary>
    /// The smallest share of the page a drawing may occupy. Smaller than this and it is an icon.
    /// </summary>
    private const double MinimumAreaRatio = 0.02;

    /// <summary>
    /// How much of a region may be covered by text before it is prose with rules in it rather than a
    /// drawing. A chart carries axis labels, so the limit is not zero.
    /// </summary>
    private const double MaxTextCoverage = 0.25;

    /// <summary>
    /// Finds every vector drawing on the page.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="segments">The lines the page draws.</param>
    /// <param name="excluded">Regions already accounted for, such as detected tables.</param>
    /// <returns>The drawings.</returns>
    public static List<DetectedVectorFigure> Detect(Page page, IReadOnlyList<PdfSegment> segments, IReadOnlyList<double[]> excluded)
    {
        var figures = new List<DetectedVectorFigure>();

        if (segments is null || segments.Count < MinimumSegments)
        {
            return figures;
        }

        var threshold = page.Width * ClusterDistanceRatio;
        var pageArea = page.Width * page.Height;
        var words = page.GetWords().ToList();

        foreach (var cluster in Cluster(segments, threshold))
        {
            if (cluster.Count < MinimumSegments)
            {
                continue;
            }

            var bounds = GetBounds(cluster);
            var area = (bounds[2] - bounds[0]) * (bounds[3] - bounds[1]);

            if (pageArea <= 0 || area / pageArea < MinimumAreaRatio)
            {
                continue;
            }

            if (excluded is not null && excluded.Any(region => Overlaps(bounds, region)))
            {
                continue;
            }

            if (GetTextCoverage(words, bounds, area) > MaxTextCoverage)
            {
                continue;
            }

            figures.Add(new DetectedVectorFigure
            {
                Segments = cluster,
                Bounds = bounds,
            });
        }

        return figures;
    }

    /// <summary>
    /// Groups segments that are near each other into drawings, by walking the neighbours of each segment.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <param name="threshold">How near two segments have to be to belong together.</param>
    /// <returns>The clusters.</returns>
    private static List<List<PdfSegment>> Cluster(IReadOnlyList<PdfSegment> segments, double threshold)
    {
        var clusters = new List<List<PdfSegment>>();
        var assigned = new bool[segments.Count];

        for (var index = 0; index < segments.Count; index++)
        {
            if (assigned[index])
            {
                continue;
            }

            var cluster = new List<PdfSegment>();
            var queue = new Queue<int>();

            queue.Enqueue(index);
            assigned[index] = true;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                cluster.Add(segments[current]);

                for (var other = 0; other < segments.Count; other++)
                {
                    if (assigned[other] || !IsNear(segments[current], segments[other], threshold))
                    {
                        continue;
                    }

                    assigned[other] = true;
                    queue.Enqueue(other);
                }
            }

            clusters.Add(cluster);
        }

        return clusters;
    }

    private static bool IsNear(PdfSegment left, PdfSegment right, double threshold)
    {
        var horizontalGap = Math.Max(0, Math.Max(left.Left - right.Right, right.Left - left.Right));
        var verticalGap = Math.Max(0, Math.Max(left.Bottom - right.Top, right.Bottom - left.Top));

        return horizontalGap <= threshold && verticalGap <= threshold;
    }

    private static double[] GetBounds(IReadOnlyList<PdfSegment> segments)
    {
        var left = segments.Min(segment => segment.Left);
        var bottom = segments.Min(segment => segment.Bottom);
        var right = segments.Max(segment => segment.Right);
        var top = segments.Max(segment => segment.Top);

        return [left, bottom, right, top];
    }

    private static bool Overlaps(double[] left, double[] right)
    {
        if (left is not { Length: 4 } || right is not { Length: 4 })
        {
            return false;
        }

        return left[0] < right[2] && right[0] < left[2] && left[1] < right[3] && right[1] < left[3];
    }

    private static double GetTextCoverage(List<Word> words, double[] bounds, double area)
    {
        if (area <= 0)
        {
            return 0;
        }

        var covered = 0.0;

        foreach (var word in words)
        {
            var box = word.BoundingBox;
            var left = Math.Max(bounds[0], box.Left);
            var bottom = Math.Max(bounds[1], box.Bottom);
            var right = Math.Min(bounds[2], box.Right);
            var top = Math.Min(bounds[3], box.Top);

            if (right <= left || top <= bottom)
            {
                continue;
            }

            covered += (right - left) * (top - bottom);
        }

        return covered / area;
    }
}
