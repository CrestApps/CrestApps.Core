namespace CrestApps.Core.AI.Ingestion.Pdf.Services;

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
