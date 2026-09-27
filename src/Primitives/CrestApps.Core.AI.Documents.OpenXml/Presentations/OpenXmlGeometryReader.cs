using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations.Models;
using DocumentFormat.OpenXml;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Reads the outline of a shape: a preset name with its adjustments, or the paths of a freeform.
/// </summary>
internal static class OpenXmlGeometryReader
{
    /// <summary>
    /// Reads the geometry of a shape's properties into an element.
    /// </summary>
    /// <param name="shapeProperties">The <c>p:spPr</c> element.</param>
    /// <param name="element">The element to record it on.</param>
    /// <returns><see langword="true"/> when the properties set a geometry.</returns>
    public static bool Read(OpenXmlElement shapeProperties, PresentationElement element)
    {
        var preset = OpenXmlMarkup.Child(shapeProperties, "prstGeom");

        if (preset is not null)
        {
            element.Geometry = OpenXmlMarkup.Attribute(preset, "prst") ?? "rect";

            foreach (var guide in OpenXmlMarkup.Children(OpenXmlMarkup.Child(preset, "avLst"), "gd"))
            {
                var name = OpenXmlMarkup.Attribute(guide, "name");
                var formula = OpenXmlMarkup.Attribute(guide, "fmla");

                if (name is not null && formula is not null && formula.StartsWith("val ", StringComparison.Ordinal) &&
                    long.TryParse(formula.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    element.GeometryAdjustments[name] = value;
                }
            }

            return true;
        }

        var custom = OpenXmlMarkup.Child(shapeProperties, "custGeom");

        if (custom is null)
        {
            return false;
        }

        element.Geometry = "custom";

        var guides = ReadGuides(custom);

        foreach (var path in OpenXmlMarkup.Children(OpenXmlMarkup.Child(custom, "pathLst"), "path"))
        {
            var read = ReadPath(path, guides);

            if (read is not null)
            {
                element.Paths.Add(read);
            }
        }

        // A freeform whose paths use formulas this reader cannot evaluate is still drawn, as its box.
        if (element.Paths.Count == 0)
        {
            element.Geometry = "rect";
        }

        return true;
    }

    private static Dictionary<string, double> ReadGuides(OpenXmlElement custom)
    {
        var guides = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var list in new[] { "avLst", "gdLst" })
        {
            foreach (var guide in OpenXmlMarkup.Children(OpenXmlMarkup.Child(custom, list), "gd"))
            {
                var name = OpenXmlMarkup.Attribute(guide, "name");
                var formula = OpenXmlMarkup.Attribute(guide, "fmla");

                if (name is not null && formula is not null && formula.StartsWith("val ", StringComparison.Ordinal) &&
                    double.TryParse(formula.AsSpan(4), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    guides[name] = value;
                }
            }
        }

        return guides;
    }

    private static PresentationPath ReadPath(OpenXmlElement path, Dictionary<string, double> guides)
    {
        var width = OpenXmlMarkup.Long(path, "w") ?? 0;
        var height = OpenXmlMarkup.Long(path, "h") ?? 0;

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var result = new PresentationPath
        {
            Filled = OpenXmlMarkup.Attribute(path, "fill") != "none",
            Stroked = OpenXmlMarkup.Bool(path, "stroke") != false,
        };

        var current = (X: 0d, Y: 0d);

        foreach (var command in path.ChildElements)
        {
            var points = new List<(double X, double Y)>();

            foreach (var point in OpenXmlMarkup.Children(command, "pt"))
            {
                if (!TryValue(OpenXmlMarkup.Attribute(point, "x"), guides, out var x) || !TryValue(OpenXmlMarkup.Attribute(point, "y"), guides, out var y))
                {
                    return null;
                }

                points.Add((x / width, y / height));
            }

            switch (command.LocalName)
            {
                case "moveTo" when points.Count == 1:
                    result.Commands.Add(new PresentationPathCommand { Kind = 'M', Points = points });
                    current = (points[0].X * width, points[0].Y * height);
                    break;

                case "lnTo" when points.Count == 1:
                    result.Commands.Add(new PresentationPathCommand { Kind = 'L', Points = points });
                    current = (points[0].X * width, points[0].Y * height);
                    break;

                case "cubicBezTo" when points.Count == 3:
                    result.Commands.Add(new PresentationPathCommand { Kind = 'C', Points = points });
                    current = (points[2].X * width, points[2].Y * height);
                    break;

                case "quadBezTo" when points.Count == 2:
                    // A quadratic curve is the cubic whose control points sit two thirds of the way to the
                    // quadratic's one.
                    var start = (X: current.X / width, Y: current.Y / height);
                    var control = points[0];
                    var end = points[1];

                    result.Commands.Add(new PresentationPathCommand
                    {
                        Kind = 'C',
                        Points =
                        [
                            (start.X + (2d / 3 * (control.X - start.X)), start.Y + (2d / 3 * (control.Y - start.Y))),
                            (end.X + (2d / 3 * (control.X - end.X)), end.Y + (2d / 3 * (control.Y - end.Y))),
                            end,
                        ],
                    });
                    current = (end.X * width, end.Y * height);
                    break;

                case "arcTo":
                    if (!TryValue(OpenXmlMarkup.Attribute(command, "wR"), guides, out var radiusX) ||
                        !TryValue(OpenXmlMarkup.Attribute(command, "hR"), guides, out var radiusY) ||
                        !TryValue(OpenXmlMarkup.Attribute(command, "stAng"), guides, out var startAngle) ||
                        !TryValue(OpenXmlMarkup.Attribute(command, "swAng"), guides, out var sweepAngle))
                    {
                        return null;
                    }

                    current = AppendArc(result, current, radiusX, radiusY, startAngle / 60_000d, sweepAngle / 60_000d, width, height);
                    break;

                case "close":
                    result.Commands.Add(new PresentationPathCommand { Kind = 'Z' });
                    break;
            }
        }

        return result.Commands.Count == 0 ? null : result;
    }

    private static (double X, double Y) AppendArc(
        PresentationPath path,
        (double X, double Y) current,
        double radiusX,
        double radiusY,
        double startDegrees,
        double sweepDegrees,
        double width,
        double height)
    {
        var start = startDegrees * Math.PI / 180;
        var centerX = current.X - (radiusX * Math.Cos(start));
        var centerY = current.Y - (radiusY * Math.Sin(start));
        var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweepDegrees) / 10));
        var end = current;

        for (var step = 1; step <= steps; step++)
        {
            var angle = (startDegrees + (sweepDegrees * step / steps)) * Math.PI / 180;
            end = (centerX + (radiusX * Math.Cos(angle)), centerY + (radiusY * Math.Sin(angle)));

            path.Commands.Add(new PresentationPathCommand
            {
                Kind = 'L',
                Points = [(end.X / width, end.Y / height)],
            });
        }

        return end;
    }

    private static bool TryValue(string text, Dictionary<string, double> guides, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return text is not null && guides.TryGetValue(text, out value);
    }
}
