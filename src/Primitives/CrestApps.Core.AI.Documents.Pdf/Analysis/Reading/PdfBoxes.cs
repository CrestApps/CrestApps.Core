using System.Globalization;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Converts user-space boxes to the top-left coordinates the tools report, given the page's visible area
/// rather than the page itself, so a result can be described after the document that produced it is closed.
/// </summary>
internal static class PdfBoxes
{
    /// <summary>
    /// Converts a box to top-left coordinates.
    /// </summary>
    /// <param name="box">The box in user space.</param>
    /// <param name="visible">The page's visible area in user space.</param>
    /// <returns><c>x</c>, <c>y</c>, <c>width</c> and <c>height</c> in points from the top-left corner, rounded to a tenth.</returns>
    public static (double X, double Y, double Width, double Height) ToTopLeft(PdfBox box, PdfBox visible)
    {
        return (
            Math.Round(box.Left - visible.Left, 1),
            Math.Round(visible.Top - box.Top, 1),
            Math.Round(box.Width, 1),
            Math.Round(box.Height, 1));
    }

    /// <summary>
    /// Describes a box in top-left coordinates as compact text.
    /// </summary>
    /// <param name="box">The box in user space.</param>
    /// <param name="visible">The page's visible area in user space.</param>
    /// <returns>For example <c>x=72, y=120.5, w=80, h=11</c>.</returns>
    public static string Describe(PdfBox box, PdfBox visible)
    {
        var (x, y, width, height) = ToTopLeft(box, visible);

        return string.Create(CultureInfo.InvariantCulture, $"x={x}, y={y}, w={width}, h={height}");
    }

    /// <summary>
    /// Writes a box in top-left coordinates as an array, for JSON answers.
    /// </summary>
    /// <param name="box">The box in user space.</param>
    /// <param name="visible">The page's visible area in user space.</param>
    /// <returns><c>[x, y, width, height]</c>.</returns>
    public static double[] ToArray(PdfBox box, PdfBox visible)
    {
        var (x, y, width, height) = ToTopLeft(box, visible);

        return [x, y, width, height];
    }

    /// <summary>
    /// Reads a box from the <c>[left, bottom, right, top]</c> array the ingestion reader records.
    /// </summary>
    /// <param name="value">The metadata value.</param>
    /// <param name="box">The box.</param>
    /// <returns><see langword="true"/> when the value is such an array.</returns>
    public static bool TryRead(object value, out PdfBox box)
    {
        box = default;

        if (value is not double[] { Length: 4 } edges)
        {
            return false;
        }

        box = new PdfBox(
            Math.Min(edges[0], edges[2]),
            Math.Min(edges[1], edges[3]),
            Math.Max(edges[0], edges[2]),
            Math.Max(edges[1], edges[3]));

        return true;
    }

    /// <summary>
    /// Returns how far apart two boxes are vertically, zero when they overlap.
    /// </summary>
    /// <param name="first">The first box.</param>
    /// <param name="second">The second box.</param>
    /// <returns>The gap in points.</returns>
    public static double VerticalGap(PdfBox first, PdfBox second)
    {
        if (first.Bottom >= second.Top)
        {
            return first.Bottom - second.Top;
        }

        if (second.Bottom >= first.Top)
        {
            return second.Bottom - first.Top;
        }

        return 0;
    }

    /// <summary>
    /// Returns how much two boxes overlap horizontally.
    /// </summary>
    /// <param name="first">The first box.</param>
    /// <param name="second">The second box.</param>
    /// <returns>The shared width in points, zero when they do not overlap.</returns>
    public static double HorizontalOverlap(PdfBox first, PdfBox second)
    {
        return Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));
    }

    /// <summary>
    /// Returns whether a box's centre lies inside another box.
    /// </summary>
    /// <param name="inner">The box whose centre is tested.</param>
    /// <param name="outer">The box it may lie in.</param>
    /// <returns><see langword="true"/> when the centre is inside.</returns>
    public static bool CenterInside(PdfBox inner, PdfBox outer)
    {
        return outer.Contains((inner.Left + inner.Right) / 2, (inner.Bottom + inner.Top) / 2);
    }
}
