using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace CrestApps.Core.AI.Documents.Pdf.Services;

/// <summary>
/// One straight line drawn on a page.
/// </summary>
/// <param name="X1">The start x, in PDF user space.</param>
/// <param name="Y1">The start y, in PDF user space.</param>
/// <param name="X2">The end x.</param>
/// <param name="Y2">The end y.</param>
internal readonly record struct PdfSegment(double X1, double Y1, double X2, double Y2)
{
    /// <summary>
    /// Gets the leftmost x.
    /// </summary>
    public double Left => Math.Min(X1, X2);

    /// <summary>
    /// Gets the rightmost x.
    /// </summary>
    public double Right => Math.Max(X1, X2);

    /// <summary>
    /// Gets the lowest y.
    /// </summary>
    public double Bottom => Math.Min(Y1, Y2);

    /// <summary>
    /// Gets the highest y.
    /// </summary>
    public double Top => Math.Max(Y1, Y2);

    /// <summary>
    /// Gets how long the line is.
    /// </summary>
    public double Length => Math.Sqrt(((X2 - X1) * (X2 - X1)) + ((Y2 - Y1) * (Y2 - Y1)));
}

/// <summary>
/// Reads the lines a page draws.
/// </summary>
/// <remarks>
/// Everything phase 8 infers — where a table's rules are, where a chart's series run — starts from the same
/// question: which straight lines does this page draw, and where. Asking it once, here, keeps the table
/// detector and the chart extractor from each re-deriving it slightly differently.
/// </remarks>
internal static class PdfPageGeometry
{
    /// <summary>
    /// How thin a drawn rectangle has to be before it counts as a rule rather than a box.
    /// </summary>
    private const double RuleThickness = 2.0;

    /// <summary>
    /// Collects every straight line the page draws, including the edges of drawn rectangles and the
    /// straight-line approximations of curves.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The segments.</returns>
    public static List<PdfSegment> ReadSegments(Page page)
    {
        var segments = new List<PdfSegment>();

        foreach (var path in page.Paths)
        {
            if (path.IsClipping)
            {
                continue;
            }

            foreach (var subpath in path)
            {
                AddSubpath(segments, subpath);
            }
        }

        return segments;
    }

    private static void AddSubpath(List<PdfSegment> segments, PdfSubpath subpath)
    {
        // A rule is routinely drawn as a filled rectangle a fraction of a point high rather than as a
        // stroked line, so a detector that only looks at lines finds no table at all.
        if (subpath.IsDrawnAsRectangle)
        {
            var rectangle = subpath.GetBoundingRectangle();

            if (rectangle.HasValue)
            {
                AddRectangle(segments, rectangle.Value);

                return;
            }
        }

        PdfPoint? current = null;
        PdfPoint? start = null;

        foreach (var command in subpath.Commands)
        {
            switch (command)
            {
                case PdfSubpath.Move move:

                    current = move.Location;
                    start = move.Location;

                    break;

                case PdfSubpath.Line line:

                    segments.Add(new PdfSegment(line.From.X, line.From.Y, line.To.X, line.To.Y));
                    current = line.To;

                    break;

                case PdfSubpath.BezierCurve curve:
                    {
                        foreach (var line in curve.ToLines(8))
                        {
                            segments.Add(new PdfSegment(line.From.X, line.From.Y, line.To.X, line.To.Y));
                        }

                        current = curve.EndPoint;

                        break;
                    }

                case PdfSubpath.Close when current.HasValue && start.HasValue:

                    segments.Add(new PdfSegment(current.Value.X, current.Value.Y, start.Value.X, start.Value.Y));
                    current = start;

                    break;
            }
        }
    }

    private static void AddRectangle(List<PdfSegment> segments, PdfRectangle rectangle)
    {
        var width = Math.Abs(rectangle.Width);
        var height = Math.Abs(rectangle.Height);

        // A thin rectangle is one rule, and adding its four edges would report a rule on both sides of it.
        if (height <= RuleThickness && width > height)
        {
            var y = (rectangle.Bottom + rectangle.Top) / 2;

            segments.Add(new PdfSegment(rectangle.Left, y, rectangle.Right, y));

            return;
        }

        if (width <= RuleThickness && height > width)
        {
            var x = (rectangle.Left + rectangle.Right) / 2;

            segments.Add(new PdfSegment(x, rectangle.Bottom, x, rectangle.Top));

            return;
        }

        segments.Add(new PdfSegment(rectangle.Left, rectangle.Bottom, rectangle.Right, rectangle.Bottom));
        segments.Add(new PdfSegment(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Top));
        segments.Add(new PdfSegment(rectangle.Left, rectangle.Bottom, rectangle.Left, rectangle.Top));
        segments.Add(new PdfSegment(rectangle.Right, rectangle.Bottom, rectangle.Right, rectangle.Top));
    }
}
