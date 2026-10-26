using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics;

/// <summary>
/// How an area is filled: one colour, or a gradient between two.
/// </summary>
/// <param name="Color">The colour, or the gradient's start colour.</param>
/// <param name="Opacity">The opacity, from 0 (invisible) to 1.</param>
internal sealed record PdfBrush(PdfColor Color, double Opacity = 1)
{
    /// <summary>
    /// Gets the gradient's end colour, or <see langword="null"/> for a solid fill.
    /// </summary>
    public PdfColor? EndColor { get; init; }

    /// <summary>
    /// Gets where a linear gradient runs, from start to end colour, in the canvas's coordinates.
    /// </summary>
    public (double X1, double Y1, double X2, double Y2) Axis { get; init; }

    /// <summary>
    /// Gets a value indicating whether the gradient is radial: from the centre of <see cref="Axis"/>'s
    /// first point outwards to a radius of <see cref="Radius"/>.
    /// </summary>
    public bool IsRadial { get; init; }

    /// <summary>
    /// Gets the radius of a radial gradient.
    /// </summary>
    public double Radius { get; init; }

    /// <summary>
    /// Creates a linear gradient.
    /// </summary>
    /// <param name="from">The start colour.</param>
    /// <param name="to">The end colour.</param>
    /// <param name="x1">The x where the start colour is.</param>
    /// <param name="y1">The y where the start colour is.</param>
    /// <param name="x2">The x where the end colour is.</param>
    /// <param name="y2">The y where the end colour is.</param>
    /// <param name="opacity">The opacity.</param>
    /// <returns>The brush.</returns>
    public static PdfBrush Linear(PdfColor from, PdfColor to, double x1, double y1, double x2, double y2, double opacity = 1)
    {
        return new PdfBrush(from, opacity)
        {
            EndColor = to,
            Axis = (x1, y1, x2, y2),
        };
    }

    /// <summary>
    /// Creates a radial gradient.
    /// </summary>
    /// <param name="from">The colour at the centre.</param>
    /// <param name="to">The colour at the radius.</param>
    /// <param name="centerX">The x of the centre.</param>
    /// <param name="centerY">The y of the centre.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="opacity">The opacity.</param>
    /// <returns>The brush.</returns>
    public static PdfBrush Radial(PdfColor from, PdfColor to, double centerX, double centerY, double radius, double opacity = 1)
    {
        return new PdfBrush(from, opacity)
        {
            EndColor = to,
            Axis = (centerX, centerY, centerX, centerY),
            IsRadial = true,
            Radius = radius,
        };
    }
}
