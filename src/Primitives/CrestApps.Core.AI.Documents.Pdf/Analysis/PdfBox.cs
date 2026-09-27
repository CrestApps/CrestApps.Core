using System.Globalization;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// A rectangle in PDF user space: points, with the origin at the bottom-left of the page.
/// </summary>
/// <param name="Left">The left edge.</param>
/// <param name="Bottom">The bottom edge.</param>
/// <param name="Right">The right edge.</param>
/// <param name="Top">The top edge.</param>
internal readonly record struct PdfBox(double Left, double Bottom, double Right, double Top)
{
    /// <summary>
    /// Gets the width.
    /// </summary>
    public double Width => Right - Left;

    /// <summary>
    /// Gets the height.
    /// </summary>
    public double Height => Top - Bottom;

    /// <summary>
    /// Creates a box from a PdfPig rectangle, taking its axis-aligned extent.
    /// </summary>
    /// <param name="rectangle">The rectangle.</param>
    /// <returns>The box.</returns>
    public static PdfBox From(PdfRectangle rectangle)
    {
        return new PdfBox(
            Math.Min(Math.Min(rectangle.TopLeft.X, rectangle.TopRight.X), Math.Min(rectangle.BottomLeft.X, rectangle.BottomRight.X)),
            Math.Min(Math.Min(rectangle.TopLeft.Y, rectangle.TopRight.Y), Math.Min(rectangle.BottomLeft.Y, rectangle.BottomRight.Y)),
            Math.Max(Math.Max(rectangle.TopLeft.X, rectangle.TopRight.X), Math.Max(rectangle.BottomLeft.X, rectangle.BottomRight.X)),
            Math.Max(Math.Max(rectangle.TopLeft.Y, rectangle.TopRight.Y), Math.Max(rectangle.BottomLeft.Y, rectangle.BottomRight.Y)));
    }

    /// <summary>
    /// Creates a box from the top-left coordinates the tools report and accept: points from the top-left
    /// corner of the page's visible area.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="x">The distance from the left edge.</param>
    /// <param name="y">The distance from the top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The box in user space.</returns>
    public static PdfBox FromTopLeft(Page page, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(page);

        var visible = VisibleArea(page);

        return new PdfBox(
            visible.Left + x,
            visible.Top - y - height,
            visible.Left + x + width,
            visible.Top - y);
    }

    /// <summary>
    /// Gets the page's visible area in user space.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The crop box, or the media box when there is none.</returns>
    public static PdfBox VisibleArea(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var bounds = page.CropBox?.Bounds ?? page.MediaBox.Bounds;

        return From(bounds);
    }

    /// <summary>
    /// Returns whether the box contains a point.
    /// </summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns><see langword="true"/> when the point is inside or on the edge.</returns>
    public bool Contains(double x, double y)
    {
        return x >= Left && x <= Right && y >= Bottom && y <= Top;
    }

    /// <summary>
    /// Returns whether two boxes overlap.
    /// </summary>
    /// <param name="other">The other box.</param>
    /// <returns><see langword="true"/> when they share any area.</returns>
    public bool Intersects(PdfBox other)
    {
        return other.Left < Right && other.Right > Left && other.Bottom < Top && other.Top > Bottom;
    }

    /// <summary>
    /// Returns the smallest box holding both.
    /// </summary>
    /// <param name="other">The other box.</param>
    /// <returns>The union.</returns>
    public PdfBox Union(PdfBox other)
    {
        return new PdfBox(Math.Min(Left, other.Left), Math.Min(Bottom, other.Bottom), Math.Max(Right, other.Right), Math.Max(Top, other.Top));
    }

    /// <summary>
    /// Returns the box grown by a margin on every side.
    /// </summary>
    /// <param name="margin">The margin, in points.</param>
    /// <returns>The grown box.</returns>
    public PdfBox Inflate(double margin)
    {
        return new PdfBox(Left - margin, Bottom - margin, Right + margin, Top + margin);
    }

    /// <summary>
    /// Describes the box in the top-left coordinates the tools report: <c>x</c>, <c>y</c>, <c>width</c>
    /// and <c>height</c> in points from the top-left corner of the page's visible area.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The coordinates, rounded to a tenth of a point.</returns>
    public (double X, double Y, double Width, double Height) ToTopLeft(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var visible = VisibleArea(page);

        return (
            Math.Round(Left - visible.Left, 1),
            Math.Round(visible.Top - Top, 1),
            Math.Round(Width, 1),
            Math.Round(Height, 1));
    }

    /// <summary>
    /// Writes the box in top-left coordinates as compact text.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>For example <c>x=72, y=120.5, w=80, h=11</c>.</returns>
    public string Describe(Page page)
    {
        var (x, y, width, height) = ToTopLeft(page);

        return string.Create(CultureInfo.InvariantCulture, $"x={x}, y={y}, w={width}, h={height}");
    }
}
