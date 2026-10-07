namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// The rectangle an element occupies on its slide, in EMUs measured from the slide's top-left corner.
/// </summary>
/// <param name="X">The distance from the left edge of the slide.</param>
/// <param name="Y">The distance from the top edge of the slide.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct PresentationBounds(long X, long Y, long Width, long Height)
{
    /// <summary>
    /// Gets the distance from the left edge of the slide to the right edge of the rectangle.
    /// </summary>
    public long Right => X + Width;

    /// <summary>
    /// Gets the distance from the top edge of the slide to the bottom edge of the rectangle.
    /// </summary>
    public long Bottom => Y + Height;

    /// <summary>
    /// Gets the horizontal centre of the rectangle.
    /// </summary>
    public long CenterX => X + (Width / 2);

    /// <summary>
    /// Gets the vertical centre of the rectangle.
    /// </summary>
    public long CenterY => Y + (Height / 2);

    /// <summary>
    /// Gets a value indicating whether the rectangle has no area.
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Returns the area two rectangles share, or an empty rectangle when they do not overlap.
    /// </summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>The shared rectangle.</returns>
    public PresentationBounds Intersect(PresentationBounds other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);

        if (right <= left || bottom <= top)
        {
            return default;
        }

        return new PresentationBounds(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Returns the smallest rectangle that holds both rectangles.
    /// </summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>The enclosing rectangle.</returns>
    public PresentationBounds Union(PresentationBounds other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        var right = Math.Max(Right, other.Right);
        var bottom = Math.Max(Bottom, other.Bottom);

        return new PresentationBounds(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Gets the area of the rectangle, in square EMUs, as a floating point number so large slides cannot
    /// overflow it.
    /// </summary>
    /// <returns>The area.</returns>
    public double Area()
    {
        return IsEmpty ? 0 : (double)Width * Height;
    }
}
