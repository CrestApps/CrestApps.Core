using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// Turns DrawingML preset shapes into outlines, and says where in each shape its text goes.
/// </summary>
/// <remarks>
/// The presets are drawn from their own definitions — the adjustment each one takes, measured against the
/// shorter side of its box the way PowerPoint measures it — so a chevron with a deep notch or a rounded
/// rectangle with tight corners looks in the preview the way it does in the file. A preset this class does
/// not know is drawn as its box rather than left out.
/// </remarks>
internal static class SlideGeometry
{
    private const double Kappa = 0.5522847498;

    /// <summary>
    /// Builds the outlines of a shape.
    /// </summary>
    /// <param name="element">The element, for its preset, adjustments and freeform paths.</param>
    /// <param name="x">The left edge of the box in points.</param>
    /// <param name="y">The top edge of the box in points.</param>
    /// <param name="w">The width in points.</param>
    /// <param name="h">The height in points.</param>
    /// <returns>The outlines.</returns>
    public static List<SlidePath> Build(PresentationElement element, double x, double y, double w, double h)
    {
        var preset = element.Geometry ?? "rect";
        var adjustments = element.GeometryAdjustments;
        var ss = Math.Min(w, h);

        double Adjust(string name, double fallback)
        {
            return adjustments is not null && adjustments.TryGetValue(name, out var value) ? value : fallback;
        }

        var cx = x + (w / 2);
        var cy = y + (h / 2);

        switch (preset)
        {
            case "custom" when element.Paths.Count > 0:
                return element.Paths.Select(path => new SlidePath
                {
                    Filled = path.Filled,
                    Stroked = path.Stroked,
                    Commands = path.Commands.Select(command => new SlidePathCommand(command.Kind, command.Points.Select(point => (x + (point.X * w), y + (point.Y * h))).ToArray())).ToList(),
                }).ToList();

            case "roundRect":
            case "flowChartAlternateProcess":
                return [RoundRect(x, y, w, h, ss * Math.Clamp(Adjust("adj", 16_667), 0, 50_000) / 100_000)];

            case "flowChartTerminator":
                return [RoundRect(x, y, w, h, ss / 2)];

            case "ellipse":
            case "flowChartConnector":
                return [Ellipse(cx, cy, w / 2, h / 2)];

            case "triangle":
            case "flowChartExtract":
                var apex = x + (w * Math.Clamp(Adjust("adj", 50_000), 0, 100_000) / 100_000);

                return [Polygon((apex, y), (x + w, y + h), (x, y + h))];

            case "rtTriangle":
                return [Polygon((x, y), (x + w, y + h), (x, y + h))];

            case "diamond":
            case "flowChartDecision":
                return [Polygon((cx, y), (x + w, cy), (cx, y + h), (x, cy))];

            case "parallelogram":
            case "flowChartInputOutput":
                var slant = preset == "parallelogram" ? ss * Adjust("adj", 25_000) / 100_000 : w * 0.2;

                return [Polygon((x + slant, y), (x + w, y), (x + w - slant, y + h), (x, y + h))];

            case "trapezoid":
                var inset = ss * Adjust("adj", 25_000) / 100_000;

                return [Polygon((x + inset, y), (x + w - inset, y), (x + w, y + h), (x, y + h))];

            case "pentagon":
                return [Polygon((cx, y), (x + w, y + (h * 0.382)), (x + (w * 0.809), y + h), (x + (w * 0.191), y + h), (x, y + (h * 0.382)))];

            case "hexagon":
                var hexInset = ss * Adjust("adj", 25_000) / 100_000;

                return [Polygon((x + hexInset, y), (x + w - hexInset, y), (x + w, cy), (x + w - hexInset, y + h), (x + hexInset, y + h), (x, cy))];

            case "octagon":
                var corner = ss * Adjust("adj", 29_289) / 100_000;

                return [Polygon((x + corner, y), (x + w - corner, y), (x + w, y + corner), (x + w, y + h - corner), (x + w - corner, y + h), (x + corner, y + h), (x, y + h - corner), (x, y + corner))];

            case "star4":
                return [Star(cx, cy, w / 2, h / 2, 4, Adjust("adj", 12_500) / 50_000)];

            case "star5":
                return [Star(cx, cy, w / 2, h / 2, 5, Adjust("adj", 19_098) / 50_000)];

            case "star6":
                return [Star(cx, cy, w / 2, h / 2, 6, Adjust("adj", 28_868) / 50_000)];

            case "star8":
                return [Star(cx, cy, w / 2, h / 2, 8, Adjust("adj", 38_250) / 50_000)];

            case "rightArrow":
            case "leftArrow":
            case "leftRightArrow":
                return [HorizontalArrow(preset, x, y, w, h, ss, Adjust("adj1", 50_000), Adjust("adj2", 50_000))];

            case "upArrow":
            case "downArrow":
            case "upDownArrow":
                return [VerticalArrow(preset, x, y, w, h, ss, Adjust("adj1", 50_000), Adjust("adj2", 50_000))];

            case "chevron":
                var notch = ss * Adjust("adj", 50_000) / 100_000;

                return [Polygon((x, y), (x + w - notch, y), (x + w, cy), (x + w - notch, y + h), (x, y + h), (x + notch, cy))];

            case "homePlate":
                var point = ss * Adjust("adj", 50_000) / 100_000;

                return [Polygon((x, y), (x + w - point, y), (x + w, cy), (x + w - point, y + h), (x, y + h))];

            case "plus":
            case "flowChartSummingJunction":
                var arm = ss * Adjust("adj", 25_000) / 100_000;

                return [Polygon(
                    (x + arm, y), (x + w - arm, y), (x + w - arm, y + arm), (x + w, y + arm), (x + w, y + h - arm), (x + w - arm, y + h - arm),
                    (x + w - arm, y + h), (x + arm, y + h), (x + arm, y + h - arm), (x, y + h - arm), (x, y + arm), (x + arm, y + arm))];

            case "heart":
                return [new SlidePath
                {
                    Commands =
                    [
                        SlidePathCommand.MoveTo(cx, y + (h * 0.25)),
                        SlidePathCommand.CurveTo(cx, y, x, y, x, y + (h * 0.3)),
                        SlidePathCommand.CurveTo(x, y + (h * 0.6), cx, y + (h * 0.8), cx, y + h),
                        SlidePathCommand.CurveTo(cx, y + (h * 0.8), x + w, y + (h * 0.6), x + w, y + (h * 0.3)),
                        SlidePathCommand.CurveTo(x + w, y, cx, y, cx, y + (h * 0.25)),
                        SlidePathCommand.Close(),
                    ],
                }];

            case "lightningBolt":
                (double X, double Y)[] bolt = [(8458, 0), (0, 3923), (7564, 8416), (4993, 9720), (12197, 13904), (9987, 14934), (21600, 21600), (14768, 12911), (16558, 12016), (11030, 6840), (12831, 6120)];

                return [Polygon(bolt.Select(p => (x + (p.X / 21600 * w), y + (p.Y / 21600 * h))).ToArray())];

            case "sun":
                var sun = new List<SlidePath> { Ellipse(cx, cy, w * 0.26, h * 0.26) };

                for (var ray = 0; ray < 8; ray++)
                {
                    var angle = ray * Math.PI / 4;
                    var spread = Math.PI / 16;
                    sun.Add(Polygon(
                        (cx + (Math.Cos(angle - spread) * w * 0.34), cy + (Math.Sin(angle - spread) * h * 0.34)),
                        (cx + (Math.Cos(angle) * w * 0.5), cy + (Math.Sin(angle) * h * 0.5)),
                        (cx + (Math.Cos(angle + spread) * w * 0.34), cy + (Math.Sin(angle + spread) * h * 0.34))));
                }

                return sun;

            case "moon":
                return [new SlidePath
                {
                    Commands =
                    [
                        SlidePathCommand.MoveTo(x + w, y),
                        SlidePathCommand.CurveTo(x - (w / 3), y, x - (w / 3), y + h, x + w, y + h),
                        SlidePathCommand.CurveTo(x + (w / 3), y + h, x + (w / 3), y, x + w, y),
                        SlidePathCommand.Close(),
                    ],
                }];

            case "cloud":
            case "cloudCallout":
                return [Cloud(cx, cy, w, h)];

            case "donut":
                var thickness = ss * Adjust("adj", 25_000) / 100_000;
                var ring = Ellipse(cx, cy, w / 2, h / 2);

                foreach (var command in Ellipse(cx, cy, Math.Max(0, (w / 2) - thickness), Math.Max(0, (h / 2) - thickness)).Commands)
                {
                    ring.Commands.Add(command);
                }

                ring.EvenOdd = true;

                return [ring];

            case "frame":
                var border = ss * Adjust("adj1", 12_500) / 100_000;
                var frame = Rectangle(x, y, w, h);

                foreach (var command in Rectangle(x + border, y + border, w - (2 * border), h - (2 * border)).Commands)
                {
                    frame.Commands.Add(command);
                }

                frame.EvenOdd = true;

                return [frame];

            case "wedgeRectCallout":
            case "wedgeRoundRectCallout":
            case "wedgeEllipseCallout":
                var tipX = cx + (w * Adjust("adj1", -20_833) / 100_000);
                var tipY = cy + (h * Adjust("adj2", 62_500) / 100_000);
                var body = preset switch
                {
                    "wedgeEllipseCallout" => Ellipse(cx, cy, w / 2, h / 2),
                    "wedgeRectCallout" => Rectangle(x, y, w, h),
                    _ => RoundRect(x, y, w, h, ss * 0.16667),
                };

                var baseX = Math.Clamp(tipX, x + (w * 0.2), x + (w * 0.8));

                return [body, Polygon((baseX - (w * 0.08), cy), (baseX + (w * 0.08), cy), (tipX, tipY))];

            case "can":
            case "flowChartMagneticDisk":
                var capHeight = ss * Adjust("adj", 25_000) / 100_000;

                return
                [
                    new SlidePath
                    {
                        Commands =
                        [
                            SlidePathCommand.MoveTo(x, y + (capHeight / 2)),
                            SlidePathCommand.LineTo(x, y + h - (capHeight / 2)),
                            SlidePathCommand.CurveTo(x, y + h - (capHeight / 2) + (Kappa * capHeight / 2), cx - (Kappa * w / 2), y + h, cx, y + h),
                            SlidePathCommand.CurveTo(cx + (Kappa * w / 2), y + h, x + w, y + h - (capHeight / 2) + (Kappa * capHeight / 2), x + w, y + h - (capHeight / 2)),
                            SlidePathCommand.LineTo(x + w, y + (capHeight / 2)),
                            SlidePathCommand.Close(),
                        ],
                    },
                    Shaded(Ellipse(cx, y + (capHeight / 2), w / 2, capHeight / 2), -0.2),
                ];

            case "cube":
                var depth = ss * Adjust("adj", 25_000) / 100_000;

                return
                [
                    Rectangle(x, y + depth, w - depth, h - depth),
                    Shaded(Polygon((x, y + depth), (x + depth, y), (x + w, y), (x + w - depth, y + depth)), -0.2),
                    Shaded(Polygon((x + w - depth, y + depth), (x + w, y), (x + w, y + h - depth), (x + w - depth, y + h)), 0.2),
                ];

            case "foldedCorner":
                var fold = ss * Adjust("adj", 16_667) / 100_000;

                return
                [
                    Polygon((x, y), (x + w, y), (x + w, y + h - fold), (x + w - fold, y + h), (x, y + h)),
                    Shaded(Polygon((x + w, y + h - fold), (x + w - (fold * 0.8), y + h - (fold * 0.8)), (x + w - fold, y + h)), 0.2),
                ];

            case "smileyFace":
                var smile = new SlidePath
                {
                    Filled = false,
                    Commands =
                    [
                        SlidePathCommand.MoveTo(x + (w * 0.28), y + (h * 0.66)),
                        SlidePathCommand.CurveTo(x + (w * 0.4), y + (h * 0.8), x + (w * 0.6), y + (h * 0.8), x + (w * 0.72), y + (h * 0.66)),
                    ],
                };

                return
                [
                    Ellipse(cx, cy, w / 2, h / 2),
                    Shaded(Ellipse(x + (w * 0.35), y + (h * 0.38), w * 0.05, h * 0.06), 0.35),
                    Shaded(Ellipse(x + (w * 0.65), y + (h * 0.38), w * 0.05, h * 0.06), 0.35),
                    smile,
                ];

            case "gear6":
            case "gear9":
                return [Gear(cx, cy, w / 2, h / 2, preset == "gear6" ? 6 : 9)];

            case "teardrop":
                return [new SlidePath
                {
                    Commands =
                    [
                        SlidePathCommand.MoveTo(x, cy),
                        SlidePathCommand.CurveTo(x, y + (h / 2) - (Kappa * h / 2), cx - (Kappa * w / 2), y, cx, y),
                        SlidePathCommand.LineTo(x + w, y),
                        SlidePathCommand.LineTo(x + w, cy),
                        SlidePathCommand.CurveTo(x + w, cy + (Kappa * h / 2), cx + (Kappa * w / 2), y + h, cx, y + h),
                        SlidePathCommand.CurveTo(cx - (Kappa * w / 2), y + h, x, cy + (Kappa * h / 2), x, cy),
                        SlidePathCommand.Close(),
                    ],
                }];

            case "funnel":
                return [Polygon((x, y), (x + w, y), (x + (w * 0.62), y + (h * 0.62)), (x + (w * 0.62), y + h), (x + (w * 0.38), y + h), (x + (w * 0.38), y + (h * 0.62)))];

            case "flowChartDocument":
                return [new SlidePath
                {
                    Commands =
                    [
                        SlidePathCommand.MoveTo(x, y),
                        SlidePathCommand.LineTo(x + w, y),
                        SlidePathCommand.LineTo(x + w, y + (h * 0.8)),
                        SlidePathCommand.CurveTo(x + (w * 0.72), y + (h * 0.68), x + (w * 0.5), y + (h * 1.02), x + (w * 0.25), y + (h * 0.92)),
                        SlidePathCommand.CurveTo(x + (w * 0.12), y + (h * 0.86), x + (w * 0.05), y + (h * 0.86), x, y + (h * 0.9)),
                        SlidePathCommand.Close(),
                    ],
                }];

            case "snip1Rect":
                var snip = ss * Adjust("adj", 16_667) / 100_000;

                return [Polygon((x, y), (x + w - snip, y), (x + w, y + snip), (x + w, y + h), (x, y + h))];

            case "snip2SameRect":
                var snipTop = ss * Adjust("adj1", 16_667) / 100_000;

                return [Polygon((x + snipTop, y), (x + w - snipTop, y), (x + w, y + snipTop), (x + w, y + h), (x, y + h), (x, y + snipTop))];

            case "round1Rect":
            case "round2SameRect":
            case "round2DiagRect":
                return [RoundCorners(x, y, w, h, ss * Adjust("adj1", Adjust("adj", 16_667)) / 100_000, preset)];

            case "plaque":
            case "bevel":
                return [RoundRect(x, y, w, h, ss * 0.1)];

            case "pie":
            case "blockArc":
            case "chord":
            case "arc":
                return [Arc(preset, cx, cy, w / 2, h / 2, Adjust("adj1", preset == "arc" ? 16_200_000 : 0) / 60_000, Adjust("adj2", preset == "arc" ? 0 : 16_200_000) / 60_000)];

            case "line":
            case "straightConnector1":
                return [new SlidePath { Filled = false, Commands = [SlidePathCommand.MoveTo(x, y), SlidePathCommand.LineTo(x + w, y + h)] }];

            case "bentConnector2":
                return [new SlidePath { Filled = false, Commands = [SlidePathCommand.MoveTo(x, y), SlidePathCommand.LineTo(x + w, y), SlidePathCommand.LineTo(x + w, y + h)] }];

            case "bentConnector3":
            case "bentConnector4":
            case "bentConnector5":
                var bend = x + (w * Adjust("adj1", 50_000) / 100_000);

                return [new SlidePath { Filled = false, Commands = [SlidePathCommand.MoveTo(x, y), SlidePathCommand.LineTo(bend, y), SlidePathCommand.LineTo(bend, y + h), SlidePathCommand.LineTo(x + w, y + h)] }];

            case "curvedConnector2":
            case "curvedConnector3":
            case "curvedConnector4":
            case "curvedConnector5":
                return [new SlidePath { Filled = false, Commands = [SlidePathCommand.MoveTo(x, y), SlidePathCommand.CurveTo(x + (w / 2), y, x + (w / 2), y + h, x + w, y + h)] }];

            default:
                return [Rectangle(x, y, w, h)];
        }
    }

    /// <summary>
    /// Returns the rectangle inside a shape that its text is laid out in, before the text insets.
    /// </summary>
    /// <param name="preset">The preset name.</param>
    /// <param name="x">The left edge of the box.</param>
    /// <param name="y">The top edge of the box.</param>
    /// <param name="w">The width.</param>
    /// <param name="h">The height.</param>
    /// <returns>The text rectangle.</returns>
    public static (double X, double Y, double W, double H) TextRectangle(string preset, double x, double y, double w, double h)
    {
        var ss = Math.Min(w, h);

        return preset switch
        {
            "ellipse" or "flowChartConnector" or "donut" or "sun" or "smileyFace" or "gear6" or "gear9" or "cloud" or "wedgeEllipseCallout" or "teardrop"
                => (x + (w * 0.146), y + (h * 0.146), w * 0.708, h * 0.708),
            "diamond" or "flowChartDecision" => (x + (w * 0.25), y + (h * 0.25), w * 0.5, h * 0.5),
            "triangle" => (x + (w * 0.25), y + (h * 0.5), w * 0.5, h * 0.5),
            "rtTriangle" => (x + (w * 0.08), y + (h * 0.5), w * 0.5, h * 0.42),
            "chevron" => (x + (ss * 0.5), y, Math.Max(1, w - ss), h),
            "homePlate" => (x, y, Math.Max(1, w - (ss * 0.4)), h),
            "hexagon" or "octagon" => (x + (ss * 0.2), y + (ss * 0.1), Math.Max(1, w - (ss * 0.4)), Math.Max(1, h - (ss * 0.2))),
            "star5" or "star6" or "star8" or "star4" => (x + (w * 0.25), y + (h * 0.3), w * 0.5, h * 0.45),
            "can" => (x, y + (ss * 0.25), w, Math.Max(1, h - (ss * 0.25))),
            "cube" => (x, y + (ss * 0.25), Math.Max(1, w - (ss * 0.25)), Math.Max(1, h - (ss * 0.25))),
            "rightArrow" or "leftArrow" or "leftRightArrow" => (x, y + (h * 0.25), w, h * 0.5),
            "roundRect" => (x + (ss * 0.05), y + (ss * 0.05), Math.Max(1, w - (ss * 0.1)), Math.Max(1, h - (ss * 0.1))),
            _ => (x, y, w, h),
        };
    }

    /// <summary>
    /// Creates a rectangle outline.
    /// </summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="w">The width.</param>
    /// <param name="h">The height.</param>
    /// <returns>The outline.</returns>
    public static SlidePath Rectangle(double x, double y, double w, double h)
    {
        return Polygon((x, y), (x + w, y), (x + w, y + h), (x, y + h));
    }

    /// <summary>
    /// Creates a closed polygon outline.
    /// </summary>
    /// <param name="points">The corners, in order.</param>
    /// <returns>The outline.</returns>
    public static SlidePath Polygon(params (double X, double Y)[] points)
    {
        var path = new SlidePath();

        for (var index = 0; index < points.Length; index++)
        {
            path.Commands.Add(index == 0 ? SlidePathCommand.MoveTo(points[index].X, points[index].Y) : SlidePathCommand.LineTo(points[index].X, points[index].Y));
        }

        path.Commands.Add(SlidePathCommand.Close());

        return path;
    }

    /// <summary>
    /// Creates an ellipse outline from four curves.
    /// </summary>
    /// <param name="cx">The horizontal centre.</param>
    /// <param name="cy">The vertical centre.</param>
    /// <param name="rx">The horizontal radius.</param>
    /// <param name="ry">The vertical radius.</param>
    /// <returns>The outline.</returns>
    public static SlidePath Ellipse(double cx, double cy, double rx, double ry)
    {
        var kx = rx * Kappa;
        var ky = ry * Kappa;

        return new SlidePath
        {
            Commands =
            [
                SlidePathCommand.MoveTo(cx + rx, cy),
                SlidePathCommand.CurveTo(cx + rx, cy + ky, cx + kx, cy + ry, cx, cy + ry),
                SlidePathCommand.CurveTo(cx - kx, cy + ry, cx - rx, cy + ky, cx - rx, cy),
                SlidePathCommand.CurveTo(cx - rx, cy - ky, cx - kx, cy - ry, cx, cy - ry),
                SlidePathCommand.CurveTo(cx + kx, cy - ry, cx + rx, cy - ky, cx + rx, cy),
                SlidePathCommand.Close(),
            ],
        };
    }

    /// <summary>
    /// Creates a rounded rectangle outline.
    /// </summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="w">The width.</param>
    /// <param name="h">The height.</param>
    /// <param name="radius">The corner radius.</param>
    /// <returns>The outline.</returns>
    public static SlidePath RoundRect(double x, double y, double w, double h, double radius)
    {
        var r = Math.Clamp(radius, 0, Math.Min(w, h) / 2);

        if (r <= 0.01)
        {
            return Rectangle(x, y, w, h);
        }

        var k = r * (1 - Kappa);

        return new SlidePath
        {
            Commands =
            [
                SlidePathCommand.MoveTo(x + r, y),
                SlidePathCommand.LineTo(x + w - r, y),
                SlidePathCommand.CurveTo(x + w - k, y, x + w, y + k, x + w, y + r),
                SlidePathCommand.LineTo(x + w, y + h - r),
                SlidePathCommand.CurveTo(x + w, y + h - k, x + w - k, y + h, x + w - r, y + h),
                SlidePathCommand.LineTo(x + r, y + h),
                SlidePathCommand.CurveTo(x + k, y + h, x, y + h - k, x, y + h - r),
                SlidePathCommand.LineTo(x, y + r),
                SlidePathCommand.CurveTo(x, y + k, x + k, y, x + r, y),
                SlidePathCommand.Close(),
            ],
        };
    }

    /// <summary>
    /// Creates the outline of a pie slice, from its start angle through its sweep, in degrees clockwise from
    /// the positive horizontal axis.
    /// </summary>
    /// <param name="cx">The horizontal centre.</param>
    /// <param name="cy">The vertical centre.</param>
    /// <param name="rx">The horizontal radius.</param>
    /// <param name="ry">The vertical radius.</param>
    /// <param name="startDegrees">Where the slice starts.</param>
    /// <param name="sweepDegrees">How far it sweeps.</param>
    /// <param name="innerRatio">The size of a doughnut's hole relative to the radius; 0 for a pie.</param>
    /// <returns>The outline.</returns>
    public static SlidePath Slice(double cx, double cy, double rx, double ry, double startDegrees, double sweepDegrees, double innerRatio = 0)
    {
        var path = new SlidePath();
        var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweepDegrees) / 6));
        var start = startDegrees * Math.PI / 180;
        var sweep = sweepDegrees * Math.PI / 180;

        if (innerRatio <= 0)
        {
            path.Commands.Add(SlidePathCommand.MoveTo(cx, cy));
        }

        for (var step = 0; step <= steps; step++)
        {
            var angle = start + (sweep * step / steps);
            var point = (cx + (Math.Cos(angle) * rx), cy + (Math.Sin(angle) * ry));

            path.Commands.Add(step == 0 && innerRatio > 0 ? SlidePathCommand.MoveTo(point.Item1, point.Item2) : SlidePathCommand.LineTo(point.Item1, point.Item2));
        }

        if (innerRatio > 0)
        {
            for (var step = steps; step >= 0; step--)
            {
                var angle = start + (sweep * step / steps);
                path.Commands.Add(SlidePathCommand.LineTo(cx + (Math.Cos(angle) * rx * innerRatio), cy + (Math.Sin(angle) * ry * innerRatio)));
            }
        }

        path.Commands.Add(SlidePathCommand.Close());

        return path;
    }

    private static SlidePath Shaded(SlidePath path, double shade)
    {
        path.Shade = shade;

        return path;
    }

    private static SlidePath Star(double cx, double cy, double rx, double ry, int points, double innerRatio)
    {
        var corners = new (double X, double Y)[points * 2];
        var inner = Math.Clamp(innerRatio, 0.05, 0.95);

        for (var index = 0; index < corners.Length; index++)
        {
            var angle = (-Math.PI / 2) + (index * Math.PI / points);
            var radius = index % 2 == 0 ? 1 : inner;
            corners[index] = (cx + (Math.Cos(angle) * rx * radius), cy + (Math.Sin(angle) * ry * radius));
        }

        return Polygon(corners);
    }

    private static SlidePath Gear(double cx, double cy, double rx, double ry, int teeth)
    {
        var corners = new List<(double X, double Y)>();
        var step = 2 * Math.PI / teeth;

        for (var tooth = 0; tooth < teeth; tooth++)
        {
            var angle = (-Math.PI / 2) + (tooth * step);

            // Each tooth: a flat top on the outer radius between two flanks down to the root circle.
            foreach (var (offset, radius) in new[] { (-0.5, 0.78), (-0.28, 1.0), (0.28, 1.0), (0.5, 0.78) })
            {
                var a = angle + (offset * step * 0.9);
                corners.Add((cx + (Math.Cos(a) * rx * radius), cy + (Math.Sin(a) * ry * radius)));
            }
        }

        return Polygon([.. corners]);
    }

    private static SlidePath Cloud(double cx, double cy, double w, double h)
    {
        const int Bumps = 9;
        var path = new SlidePath();
        var rx = w * 0.42;
        var ry = h * 0.38;
        var previous = (X: cx + rx, Y: cy);

        path.Commands.Add(SlidePathCommand.MoveTo(previous.X, previous.Y));

        for (var bump = 1; bump <= Bumps; bump++)
        {
            var angle = bump * 2 * Math.PI / Bumps;
            var next = (X: cx + (Math.Cos(angle) * rx), Y: cy + (Math.Sin(angle) * ry));
            var middle = angle - (Math.PI / Bumps);
            var bulge = (X: cx + (Math.Cos(middle) * rx * 1.28), Y: cy + (Math.Sin(middle) * ry * 1.3));

            path.Commands.Add(SlidePathCommand.CurveTo(
                previous.X + ((bulge.X - previous.X) * 0.9),
                previous.Y + ((bulge.Y - previous.Y) * 0.9),
                next.X + ((bulge.X - next.X) * 0.9),
                next.Y + ((bulge.Y - next.Y) * 0.9),
                next.X,
                next.Y));

            previous = next;
        }

        path.Commands.Add(SlidePathCommand.Close());

        return path;
    }

    private static SlidePath HorizontalArrow(string preset, double x, double y, double w, double h, double ss, double thickness, double head)
    {
        var cy = y + (h / 2);
        var half = h * Math.Clamp(thickness, 0, 100_000) / 200_000;
        var length = Math.Min(ss * Math.Clamp(head, 0, 100_000) / 100_000, preset == "leftRightArrow" ? w / 2 : w);
        var top = cy - half;
        var bottom = cy + half;

        return preset switch
        {
            "leftArrow" => Polygon((x, cy), (x + length, y), (x + length, top), (x + w, top), (x + w, bottom), (x + length, bottom), (x + length, y + h)),
            "leftRightArrow" => Polygon((x, cy), (x + length, y), (x + length, top), (x + w - length, top), (x + w - length, y), (x + w, cy), (x + w - length, y + h), (x + w - length, bottom), (x + length, bottom), (x + length, y + h)),
            _ => Polygon((x, top), (x + w - length, top), (x + w - length, y), (x + w, cy), (x + w - length, y + h), (x + w - length, bottom), (x, bottom)),
        };
    }

    private static SlidePath VerticalArrow(string preset, double x, double y, double w, double h, double ss, double thickness, double head)
    {
        var cx = x + (w / 2);
        var half = w * Math.Clamp(thickness, 0, 100_000) / 200_000;
        var length = Math.Min(ss * Math.Clamp(head, 0, 100_000) / 100_000, preset == "upDownArrow" ? h / 2 : h);
        var left = cx - half;
        var right = cx + half;

        return preset switch
        {
            "upArrow" => Polygon((cx, y), (x + w, y + length), (right, y + length), (right, y + h), (left, y + h), (left, y + length), (x, y + length)),
            "upDownArrow" => Polygon((cx, y), (x + w, y + length), (right, y + length), (right, y + h - length), (x + w, y + h - length), (cx, y + h), (x, y + h - length), (left, y + h - length), (left, y + length), (x, y + length)),
            _ => Polygon((left, y), (right, y), (right, y + h - length), (x + w, y + h - length), (cx, y + h), (x, y + h - length), (left, y + h - length)),
        };
    }

    private static SlidePath RoundCorners(double x, double y, double w, double h, double radius, string preset)
    {
        var r = Math.Clamp(radius, 0, Math.Min(w, h) / 2);
        var k = r * (1 - Kappa);
        var topLeft = preset is "round2SameRect" or "round2DiagRect";
        var topRight = true;
        var bottomRight = preset == "round2DiagRect";
        var path = new SlidePath();

        path.Commands.Add(SlidePathCommand.MoveTo(x + (topLeft ? r : 0), y));
        path.Commands.Add(SlidePathCommand.LineTo(x + w - (topRight ? r : 0), y));

        if (topRight)
        {
            path.Commands.Add(SlidePathCommand.CurveTo(x + w - k, y, x + w, y + k, x + w, y + r));
        }

        path.Commands.Add(SlidePathCommand.LineTo(x + w, y + h - (bottomRight ? r : 0)));

        if (bottomRight)
        {
            path.Commands.Add(SlidePathCommand.CurveTo(x + w, y + h - k, x + w - k, y + h, x + w - r, y + h));
        }

        path.Commands.Add(SlidePathCommand.LineTo(x, y + h));
        path.Commands.Add(SlidePathCommand.LineTo(x, y + (topLeft ? r : 0)));

        if (topLeft)
        {
            path.Commands.Add(SlidePathCommand.CurveTo(x, y + k, x + k, y, x + r, y));
        }

        path.Commands.Add(SlidePathCommand.Close());

        return path;
    }

    private static SlidePath Arc(string preset, double cx, double cy, double rx, double ry, double startDegrees, double endDegrees)
    {
        var sweep = endDegrees - startDegrees;

        if (sweep <= 0)
        {
            sweep += 360;
        }

        return preset switch
        {
            "arc" => ArcStroke(cx, cy, rx, ry, startDegrees, sweep),
            "blockArc" => Slice(cx, cy, rx, ry, startDegrees, sweep, 0.75),
            _ => Slice(cx, cy, rx, ry, startDegrees, sweep),
        };
    }

    private static SlidePath ArcStroke(double cx, double cy, double rx, double ry, double startDegrees, double sweepDegrees)
    {
        var path = new SlidePath { Filled = false };
        var steps = Math.Max(2, (int)Math.Ceiling(sweepDegrees / 6));

        for (var step = 0; step <= steps; step++)
        {
            var angle = (startDegrees + (sweepDegrees * step / steps)) * Math.PI / 180;
            var point = (cx + (Math.Cos(angle) * rx), cy + (Math.Sin(angle) * ry));
            path.Commands.Add(step == 0 ? SlidePathCommand.MoveTo(point.Item1, point.Item2) : SlidePathCommand.LineTo(point.Item1, point.Item2));
        }

        return path;
    }
}
