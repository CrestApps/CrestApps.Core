using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics;

/// <summary>
/// A shape to fill, stroke or clip with: lines and Bézier curves in a canvas's coordinates.
/// </summary>
internal sealed class PdfPath
{
    // How far a cubic Bézier's control points sit along the tangent to draw a quarter circle.
    private const double Kappa = 0.5522847498;

    private readonly StringBuilder _operators = new();

    /// <summary>
    /// Gets a value indicating whether the path has any segment.
    /// </summary>
    public bool IsEmpty => _operators.Length == 0;

    /// <summary>
    /// Gets or sets a value indicating whether the inside is found with the even-odd rule rather than the
    /// non-zero winding rule.
    /// </summary>
    public bool EvenOdd { get; set; }

    /// <summary>
    /// Starts a new figure.
    /// </summary>
    /// <param name="x">The x.</param>
    /// <param name="y">The y.</param>
    /// <returns>The path.</returns>
    public PdfPath MoveTo(double x, double y)
    {
        return Append(x, y).Append("m");
    }

    /// <summary>
    /// Adds a straight line.
    /// </summary>
    /// <param name="x">The x it ends at.</param>
    /// <param name="y">The y it ends at.</param>
    /// <returns>The path.</returns>
    public PdfPath LineTo(double x, double y)
    {
        return Append(x, y).Append("l");
    }

    /// <summary>
    /// Adds a cubic Bézier curve.
    /// </summary>
    /// <param name="x1">The x of the first control point.</param>
    /// <param name="y1">The y of the first control point.</param>
    /// <param name="x2">The x of the second control point.</param>
    /// <param name="y2">The y of the second control point.</param>
    /// <param name="x3">The x it ends at.</param>
    /// <param name="y3">The y it ends at.</param>
    /// <returns>The path.</returns>
    public PdfPath CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        return Append(x1, y1).Append(x2, y2).Append(x3, y3).Append("c");
    }

    /// <summary>
    /// Closes the current figure with a line back to where it started.
    /// </summary>
    /// <returns>The path.</returns>
    public PdfPath Close()
    {
        return Append("h");
    }

    /// <summary>
    /// Adds a rectangle as a figure of its own.
    /// </summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The path.</returns>
    public PdfPath AddRectangle(double x, double y, double width, double height)
    {
        return Append(x, y).Append(width, height).Append("re");
    }

    /// <summary>
    /// Adds a rectangle with rounded corners as a figure of its own.
    /// </summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="radius">The corner radius.</param>
    /// <returns>The path.</returns>
    public PdfPath AddRoundedRectangle(double x, double y, double width, double height, double radius)
    {
        var r = Math.Clamp(radius, 0, Math.Min(width, height) / 2);

        if (r <= 0)
        {
            return AddRectangle(x, y, width, height);
        }

        var k = r * Kappa;

        return MoveTo(x + r, y)
            .LineTo(x + width - r, y)
            .CurveTo(x + width - r + k, y, x + width, y + r - k, x + width, y + r)
            .LineTo(x + width, y + height - r)
            .CurveTo(x + width, y + height - r + k, x + width - r + k, y + height, x + width - r, y + height)
            .LineTo(x + r, y + height)
            .CurveTo(x + r - k, y + height, x, y + height - r + k, x, y + height - r)
            .LineTo(x, y + r)
            .CurveTo(x, y + r - k, x + r - k, y, x + r, y)
            .Close();
    }

    /// <summary>
    /// Adds an ellipse as a figure of its own.
    /// </summary>
    /// <param name="x">The left edge of the box it fills.</param>
    /// <param name="y">The top edge of the box it fills.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The path.</returns>
    public PdfPath AddEllipse(double x, double y, double width, double height)
    {
        var rx = width / 2;
        var ry = height / 2;
        var cx = x + rx;
        var cy = y + ry;
        var kx = rx * Kappa;
        var ky = ry * Kappa;

        return MoveTo(cx + rx, cy)
            .CurveTo(cx + rx, cy + ky, cx + kx, cy + ry, cx, cy + ry)
            .CurveTo(cx - kx, cy + ry, cx - rx, cy + ky, cx - rx, cy)
            .CurveTo(cx - rx, cy - ky, cx - kx, cy - ry, cx, cy - ry)
            .CurveTo(cx + kx, cy - ry, cx + rx, cy - ky, cx + rx, cy)
            .Close();
    }

    /// <summary>
    /// Adds a circular arc, continuing the current figure with a line to its start, or starting one.
    /// </summary>
    /// <param name="centerX">The x of the centre.</param>
    /// <param name="centerY">The y of the centre.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="startDegrees">Where it starts, in degrees clockwise from three o'clock.</param>
    /// <param name="sweepDegrees">How far it turns, in degrees clockwise.</param>
    /// <param name="startFigure">Whether the arc starts a figure rather than continuing one.</param>
    /// <returns>The path.</returns>
    public PdfPath AddArc(double centerX, double centerY, double radius, double startDegrees, double sweepDegrees, bool startFigure)
    {
        var segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepDegrees) / 90));
        var step = sweepDegrees / segments * Math.PI / 180;
        var angle = startDegrees * Math.PI / 180;
        var handle = 4.0 / 3 * Math.Tan(step / 4) * radius;
        var startX = centerX + (radius * Math.Cos(angle));
        var startY = centerY + (radius * Math.Sin(angle));

        if (startFigure)
        {
            MoveTo(startX, startY);
        }
        else
        {
            LineTo(startX, startY);
        }

        // With y growing downwards, a positive angle turns clockwise.
        for (var segment = 0; segment < segments; segment++)
        {
            var end = angle + step;

            CurveTo(
                centerX + (radius * Math.Cos(angle)) - (handle * Math.Sin(angle)),
                centerY + (radius * Math.Sin(angle)) + (handle * Math.Cos(angle)),
                centerX + (radius * Math.Cos(end)) + (handle * Math.Sin(end)),
                centerY + (radius * Math.Sin(end)) - (handle * Math.Cos(end)),
                centerX + (radius * Math.Cos(end)),
                centerY + (radius * Math.Sin(end)));

            angle = end;
        }

        return this;
    }

    /// <summary>
    /// Returns the path as content stream operators.
    /// </summary>
    /// <returns>The operators.</returns>
    public override string ToString()
    {
        return _operators.ToString();
    }

    private PdfPath Append(double x, double y)
    {
        _operators.Append(PdfObjectModel.Format(x)).Append(' ').Append(PdfObjectModel.Format(y)).Append(' ');

        return this;
    }

    private PdfPath Append(string op)
    {
        _operators.Append(op).Append('\n');

        return this;
    }
}
