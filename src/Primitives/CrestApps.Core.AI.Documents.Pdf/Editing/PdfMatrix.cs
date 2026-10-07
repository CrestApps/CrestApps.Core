using CrestApps.Core.AI.Documents.Pdf.Analysis;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// A PDF transformation matrix <c>[a b c d e f]</c>.
/// </summary>
/// <param name="A">The a component.</param>
/// <param name="B">The b component.</param>
/// <param name="C">The c component.</param>
/// <param name="D">The d component.</param>
/// <param name="E">The horizontal translation.</param>
/// <param name="F">The vertical translation.</param>
internal readonly record struct PdfMatrix(double A, double B, double C, double D, double E, double F)
{
    /// <summary>
    /// The identity matrix.
    /// </summary>
    public static readonly PdfMatrix Identity = new(1, 0, 0, 1, 0, 0);

    /// <summary>
    /// Creates a translation.
    /// </summary>
    /// <param name="x">The horizontal distance.</param>
    /// <param name="y">The vertical distance.</param>
    /// <returns>The matrix.</returns>
    public static PdfMatrix Translation(double x, double y)
    {
        return new PdfMatrix(1, 0, 0, 1, x, y);
    }

    /// <summary>
    /// Returns this matrix followed by another: a point is transformed by this one first.
    /// </summary>
    /// <param name="other">The matrix applied second.</param>
    /// <returns>The product.</returns>
    public PdfMatrix Multiply(PdfMatrix other)
    {
        return new PdfMatrix(
            (A * other.A) + (B * other.C),
            (A * other.B) + (B * other.D),
            (C * other.A) + (D * other.C),
            (C * other.B) + (D * other.D),
            (E * other.A) + (F * other.C) + other.E,
            (E * other.B) + (F * other.D) + other.F);
    }

    /// <summary>
    /// Transforms a rectangle and returns the axis-aligned box of the result.
    /// </summary>
    /// <param name="x1">The left edge.</param>
    /// <param name="y1">The bottom edge.</param>
    /// <param name="x2">The right edge.</param>
    /// <param name="y2">The top edge.</param>
    /// <returns>The box.</returns>
    public PdfBox Transform(double x1, double y1, double x2, double y2)
    {
        var (ax, ay) = Apply(x1, y1);
        var (bx, by) = Apply(x2, y1);
        var (cx, cy) = Apply(x1, y2);
        var (dx, dy) = Apply(x2, y2);

        return new PdfBox(
            Math.Min(Math.Min(ax, bx), Math.Min(cx, dx)),
            Math.Min(Math.Min(ay, by), Math.Min(cy, dy)),
            Math.Max(Math.Max(ax, bx), Math.Max(cx, dx)),
            Math.Max(Math.Max(ay, by), Math.Max(cy, dy)));
    }

    /// <summary>
    /// Transforms a point.
    /// </summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>The transformed point.</returns>
    public (double X, double Y) Apply(double x, double y)
    {
        return ((x * A) + (y * C) + E, (x * B) + (y * D) + F);
    }
}
