using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// Paints a <see cref="SlideDrawing"/> as SVG.
/// </summary>
/// <remarks>
/// SVG, as the tabular preview uses, because nothing in this process can rasterise: the browser that shows the
/// picture already has the fonts and the renderer, and the markup stays sharp at any size. The markup is
/// served from this host's own origin, so every value written into it — text, names, colours, font names —
/// is escaped here, and pictures are only ever embedded as data of a raster image type.
/// </remarks>
internal static class SlideSvgWriter
{
    private static readonly HashSet<string> _embeddableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/bmp",
        "image/webp",
    };

    /// <summary>
    /// Writes a drawing as an SVG document.
    /// </summary>
    /// <param name="drawing">The drawing.</param>
    /// <param name="pixelWidth">The width the picture is shown at, in pixels.</param>
    /// <returns>The markup.</returns>
    public static string Write(SlideDrawing drawing, int pixelWidth)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        var writer = new Writer();
        var width = Math.Clamp(pixelWidth, 160, 3840);
        var height = drawing.Width <= 0 ? width * 9d / 16 : width * drawing.Height / drawing.Width;

        foreach (var item in drawing.Items)
        {
            switch (item)
            {
                case SlideShapeDrawing shape:
                    writer.Shape(shape);
                    break;
                case SlideTextDrawing text:
                    writer.Text(text);
                    break;
                case SlideImageDrawing image:
                    writer.Image(image);
                    break;
            }
        }

        // A hairline frame keeps a white slide from dissolving into a white page.
        writer.Body
            .Append("<rect x=\"0\" y=\"0\" width=\"").Append(N(drawing.Width)).Append("\" height=\"").Append(N(drawing.Height))
            .Append("\" fill=\"none\" stroke=\"#d0d7de\" stroke-width=\"").Append(N(drawing.Width / width)).Append("\"/>");

        var title = string.IsNullOrWhiteSpace(drawing.Title)
            ? "Slide " + drawing.SlideNumber.ToString(CultureInfo.InvariantCulture)
            : "Slide " + drawing.SlideNumber.ToString(CultureInfo.InvariantCulture) + ": " + drawing.Title;

        var output = new StringBuilder(writer.Body.Length + writer.Definitions.Length + 512);
        output
            .Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(N(width)).Append("\" height=\"").Append(N(height))
            .Append("\" viewBox=\"0 0 ").Append(N(drawing.Width)).Append(' ').Append(N(drawing.Height))
            .Append("\" role=\"img\" aria-label=\"").Append(Escape(title)).Append("\">")
            .Append("<title>").Append(Escape(title)).Append("</title>");

        if (writer.Definitions.Length > 0)
        {
            output.Append("<defs>").Append(writer.Definitions).Append("</defs>");
        }

        output.Append(writer.Body).Append("</svg>");

        return output.ToString();
    }

    private static string N(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "0";
        }

        return Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string Color(string hex)
    {
        return "#" + (PresentationColor.NormalizeHex(hex) ?? "000000");
    }

    /// <summary>
    /// Escapes text for an element or attribute, dropping characters XML cannot hold.
    /// </summary>
    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);

        foreach (var character in value)
        {
            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"':
                    builder.Append("&quot;");
                    break;
                case '\'':
                    builder.Append("&apos;");
                    break;
                default:
                    if (character >= ' ' || character == '\t')
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private sealed class Writer
    {
        private int _next;
        private bool _shadow;

        public StringBuilder Body { get; } = new(16384);

        public StringBuilder Definitions { get; } = new();

        public void Shape(SlideShapeDrawing shape)
        {
            var open = OpenGroup(shape);

            if (shape.Fill?.Image is { Data.Length: > 0 } image && _embeddableTypes.Contains(image.ContentType ?? string.Empty))
            {
                // A picture fill: the picture stretched over the shape's box, cut to the shape.
                var clip = Id("c");
                Definitions.Append("<clipPath id=\"").Append(clip).Append("\">");

                foreach (var path in shape.Paths.Where(path => path.Filled))
                {
                    Definitions.Append("<path d=\"").Append(PathData(path)).Append("\"/>");
                }

                Definitions.Append("</clipPath>");

                var (x, y, w, h) = shape.Box;
                var fullWidth = w / Math.Max(0.01, 1 - image.CropLeft - image.CropRight);
                var fullHeight = h / Math.Max(0.01, 1 - image.CropTop - image.CropBottom);

                Body.Append("<image href=\"data:").Append(image.ContentType).Append(";base64,").Append(Convert.ToBase64String(image.Data))
                    .Append("\" x=\"").Append(N(x - (image.CropLeft * fullWidth))).Append("\" y=\"").Append(N(y - (image.CropTop * fullHeight)))
                    .Append("\" width=\"").Append(N(fullWidth)).Append("\" height=\"").Append(N(fullHeight))
                    .Append("\" preserveAspectRatio=\"none\" clip-path=\"url(#").Append(clip).Append(")\"");

                if (image.Alpha < 1)
                {
                    Body.Append(" opacity=\"").Append(N(image.Alpha)).Append('"');
                }

                Body.Append("/>");

                if (shape.Stroke is not null)
                {
                    foreach (var path in shape.Paths)
                    {
                        Body.Append("<path d=\"").Append(PathData(path)).Append("\" fill=\"none\"");
                        StrokeAttributes(shape.Stroke, path.Stroked);
                        Body.Append("/>");
                    }
                }
            }
            else
            {
                var fill = shape.Fill is null ? null : Paint(shape.Fill);

                foreach (var path in shape.Paths)
                {
                    Body.Append("<path d=\"").Append(PathData(path)).Append('"');

                    if (fill is null || !path.Filled)
                    {
                        Body.Append(" fill=\"none\"");
                    }
                    else
                    {
                        var shaded = path.Shade != 0 && shape.Fill.Stops.Count == 0 && shape.Fill.Color is not null
                            ? Color(PresentationColor.Shade(shape.Fill.Color, -path.Shade))
                            : fill;

                        Body.Append(" fill=\"").Append(shaded).Append('"');

                        if (shape.Fill.Alpha < 1 && shape.Fill.Stops.Count == 0)
                        {
                            Body.Append(" fill-opacity=\"").Append(N(shape.Fill.Alpha)).Append('"');
                        }

                        if (path.EvenOdd)
                        {
                            Body.Append(" fill-rule=\"evenodd\"");
                        }
                    }

                    StrokeAttributes(shape.Stroke, path.Stroked);

                    if (shape.Shadow)
                    {
                        Body.Append(" filter=\"url(#").Append(ShadowId()).Append(")\"");
                    }

                    Body.Append("/>");
                }
            }

            CloseGroup(open);
        }

        public void Text(SlideTextDrawing text)
        {
            var open = OpenGroup(text);

            foreach (var line in text.Lines)
            {
                if (line.Marker is { } marker)
                {
                    Body.Append("<text x=\"").Append(N(line.MarkerX)).Append("\" y=\"").Append(N(line.Baseline)).Append('"');
                    RunAttributes(marker);
                    Body.Append('>').Append(Escape(marker.Text)).Append("</text>");
                }

                if (line.Runs.Count == 0)
                {
                    continue;
                }

                var first = line.Runs[0];

                foreach (var run in line.Runs.Where(run => !string.IsNullOrEmpty(run.Highlight)))
                {
                    // Highlights are drawn behind the line from the estimated widths, which is close enough for a
                    // colour band.
                    var offset = RunLeft(line, run);
                    Body.Append("<rect x=\"").Append(N(offset)).Append("\" y=\"").Append(N(line.Baseline - (run.Size * 0.85)))
                        .Append("\" width=\"").Append(N(run.Width)).Append("\" height=\"").Append(N(run.Size * 1.1))
                        .Append("\" fill=\"").Append(Color(run.Highlight)).Append("\"/>");
                }

                Body.Append("<text x=\"").Append(N(line.X)).Append("\" y=\"").Append(N(line.Baseline)).Append('"');

                if (line.Anchor != "start")
                {
                    Body.Append(" text-anchor=\"").Append(line.Anchor).Append('"');
                }

                RunAttributes(first);
                Body.Append(" xml:space=\"preserve\">");

                foreach (var run in line.Runs)
                {
                    Body.Append("<tspan");

                    if (!ReferenceEquals(run, first))
                    {
                        RunAttributes(run);
                    }

                    Decoration(run);

                    if (run.Baseline != 0)
                    {
                        Body.Append(" baseline-shift=\"").Append(N(run.Baseline)).Append("%\"");
                    }

                    Body.Append('>').Append(Escape(run.Text)).Append("</tspan>");
                }

                Body.Append("</text>");
            }

            CloseGroup(open);
        }

        public void Image(SlideImageDrawing image)
        {
            if (image.Data is not { Length: > 0 } || !_embeddableTypes.Contains(image.ContentType ?? string.Empty))
            {
                return;
            }

            var open = OpenGroup(image);
            var clip = Id("c");

            Definitions.Append("<clipPath id=\"").Append(clip).Append("\">");

            if (image.Clip is { Count: > 0 })
            {
                foreach (var path in image.Clip)
                {
                    Definitions.Append("<path d=\"").Append(PathData(path)).Append("\"/>");
                }
            }
            else
            {
                Definitions.Append("<rect x=\"").Append(N(image.X)).Append("\" y=\"").Append(N(image.Y)).Append("\" width=\"").Append(N(image.Width)).Append("\" height=\"").Append(N(image.Height)).Append("\"/>");
            }

            Definitions.Append("</clipPath>");

            // A crop keeps the whole picture and shows part of it: the picture is drawn larger than its frame and
            // the frame cuts it.
            var fullWidth = image.Width / Math.Max(0.01, 1 - image.CropLeft - image.CropRight);
            var fullHeight = image.Height / Math.Max(0.01, 1 - image.CropTop - image.CropBottom);

            Body.Append("<image href=\"data:").Append(image.ContentType).Append(";base64,").Append(Convert.ToBase64String(image.Data))
                .Append("\" x=\"").Append(N(image.X - (image.CropLeft * fullWidth))).Append("\" y=\"").Append(N(image.Y - (image.CropTop * fullHeight)))
                .Append("\" width=\"").Append(N(fullWidth)).Append("\" height=\"").Append(N(fullHeight))
                .Append("\" preserveAspectRatio=\"none\" clip-path=\"url(#").Append(clip).Append(")\"");

            if (image.Alpha < 1)
            {
                Body.Append(" opacity=\"").Append(N(image.Alpha)).Append('"');
            }

            Body.Append("/>");
            CloseGroup(open);
        }

        private bool OpenGroup(SlideDrawingItem item)
        {
            if (!item.IsTransformed)
            {
                return false;
            }

            Body.Append("<g transform=\"");

            if (Math.Abs(item.Rotation % 360) > 0.01)
            {
                Body.Append("rotate(").Append(N(item.Rotation)).Append(' ').Append(N(item.CenterX)).Append(' ').Append(N(item.CenterY)).Append(") ");
            }

            if (item.FlipHorizontal || item.FlipVertical)
            {
                Body.Append("translate(").Append(N(item.CenterX)).Append(' ').Append(N(item.CenterY)).Append(") scale(")
                    .Append(item.FlipHorizontal ? "-1" : "1").Append(' ').Append(item.FlipVertical ? "-1" : "1")
                    .Append(") translate(").Append(N(-item.CenterX)).Append(' ').Append(N(-item.CenterY)).Append(')');
            }

            Body.Append("\">");

            return true;
        }

        private void CloseGroup(bool open)
        {
            if (open)
            {
                Body.Append("</g>");
            }
        }

        private void RunAttributes(SlideTextRun run)
        {
            Body.Append(" font-family=\"").Append(Escape(SlideFonts.CssFamily(run.Font))).Append("\" font-size=\"").Append(N(run.Size)).Append("\" fill=\"").Append(Color(run.Color)).Append('"');

            if (run.Bold)
            {
                Body.Append(" font-weight=\"bold\"");
            }

            if (run.Italic)
            {
                Body.Append(" font-style=\"italic\"");
            }
        }

        private void Decoration(SlideTextRun run)
        {
            if (run.Underline && run.Strikethrough)
            {
                Body.Append(" text-decoration=\"underline line-through\"");
            }
            else if (run.Underline)
            {
                Body.Append(" text-decoration=\"underline\"");
            }
            else if (run.Strikethrough)
            {
                Body.Append(" text-decoration=\"line-through\"");
            }
        }

        private void StrokeAttributes(SlideStroke stroke, bool stroked)
        {
            if (stroke is null || !stroked)
            {
                Body.Append(" stroke=\"none\"");

                return;
            }

            Body.Append(" stroke=\"").Append(Color(stroke.Color)).Append("\" stroke-width=\"").Append(N(stroke.Width)).Append('"');

            if (stroke.Alpha < 1)
            {
                Body.Append(" stroke-opacity=\"").Append(N(stroke.Alpha)).Append('"');
            }

            var width = stroke.Width;
            var dash = stroke.Dash switch
            {
                "dash" => N(width * 4) + " " + N(width * 3),
                "dot" => N(width) + " " + N(width * 2),
                "dash_dot" => N(width * 4) + " " + N(width * 2) + " " + N(width) + " " + N(width * 2),
                "long_dash" => N(width * 8) + " " + N(width * 3),
                "long_dash_dot" => N(width * 8) + " " + N(width * 3) + " " + N(width) + " " + N(width * 3),
                _ => null,
            };

            if (dash is not null)
            {
                Body.Append(" stroke-dasharray=\"").Append(dash).Append('"');
            }
        }

        private string Paint(SlidePaint paint)
        {
            if (paint.Stops.Count == 0)
            {
                return Color(paint.Color);
            }

            var id = Id("g");

            if (paint.Radial)
            {
                Definitions.Append("<radialGradient id=\"").Append(id).Append("\" cx=\"50%\" cy=\"50%\" r=\"70%\">");
            }
            else
            {
                // DrawingML measures the angle clockwise from left-to-right; the vector is taken across the shape's
                // bounding box so the colours meet where PowerPoint puts them.
                var radians = paint.Angle * Math.PI / 180;
                var dx = Math.Cos(radians) / 2;
                var dy = Math.Sin(radians) / 2;

                Definitions.Append("<linearGradient id=\"").Append(id).Append("\" x1=\"").Append(N(0.5 - dx)).Append("\" y1=\"").Append(N(0.5 - dy))
                    .Append("\" x2=\"").Append(N(0.5 + dx)).Append("\" y2=\"").Append(N(0.5 + dy)).Append("\">");
            }

            foreach (var stop in paint.Stops)
            {
                Definitions.Append("<stop offset=\"").Append(N(stop.Position)).Append("\" stop-color=\"").Append(Color(stop.Color)).Append('"');

                if (stop.Alpha < 1)
                {
                    Definitions.Append(" stop-opacity=\"").Append(N(stop.Alpha)).Append('"');
                }

                Definitions.Append("/>");
            }

            Definitions.Append(paint.Radial ? "</radialGradient>" : "</linearGradient>");

            return "url(#" + id + ")";
        }

        private string ShadowId()
        {
            if (!_shadow)
            {
                Definitions.Append("<filter id=\"shadow\" x=\"-20%\" y=\"-20%\" width=\"140%\" height=\"140%\"><feDropShadow dx=\"0\" dy=\"2\" stdDeviation=\"2.5\" flood-color=\"#000000\" flood-opacity=\"0.3\"/></filter>");
                _shadow = true;
            }

            return "shadow";
        }

        private string Id(string prefix)
        {
            return prefix + (++_next).ToString(CultureInfo.InvariantCulture);
        }

        private static double RunLeft(SlideTextLine line, SlideTextRun run)
        {
            var left = line.Anchor switch
            {
                "middle" => line.X - (line.Width / 2),
                "end" => line.X - line.Width,
                _ => line.X,
            };

            foreach (var candidate in line.Runs)
            {
                if (ReferenceEquals(candidate, run))
                {
                    break;
                }

                left += candidate.Width;
            }

            return left;
        }

        private static string PathData(SlidePath path)
        {
            var builder = new StringBuilder(path.Commands.Count * 16);

            foreach (var command in path.Commands)
            {
                builder.Append(command.Kind);

                foreach (var (x, y) in command.Points)
                {
                    builder.Append(N(x)).Append(' ').Append(N(y)).Append(' ');
                }
            }

            return builder.ToString().TrimEnd();
        }
    }
}
