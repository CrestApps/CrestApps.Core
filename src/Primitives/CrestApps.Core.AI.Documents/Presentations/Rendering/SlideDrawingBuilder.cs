using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// Draws a slide of the resolved presentation model as a <see cref="SlideDrawing"/>.
/// </summary>
/// <remarks>
/// This is the one place a slide is turned into a picture. The SVG preview and the PDF export both paint the
/// drawing it returns, and it reads the same resolved model the tools report from, which is itself read from
/// the package the export writes — so what the reader is shown, told and handed all come from the same file.
/// </remarks>
internal static class SlideDrawingBuilder
{
    private static readonly HashSet<string> _displayableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/bmp",
        "image/webp",
    };

    /// <summary>
    /// Draws a slide.
    /// </summary>
    /// <param name="model">The deck the slide belongs to.</param>
    /// <param name="slide">The slide.</param>
    /// <param name="options">The limits to draw within.</param>
    /// <returns>The drawing.</returns>
    public static SlideDrawing Build(PresentationModel model, PresentationSlide slide, SlideDrawingOptions options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(slide);

        var drawing = new SlideDrawing
        {
            Width = PresentationUnits.ToPoints(model.SlideWidth),
            Height = PresentationUnits.ToPoints(model.SlideHeight),
            SlideNumber = slide.Number,
            Title = slide.Title,
        };

        var context = new Context(drawing, model, options ?? new SlideDrawingOptions(), slide.Background);

        DrawBackground(context, slide.Background);

        foreach (var element in slide.InheritedElements)
        {
            DrawElement(context, element);
        }

        foreach (var element in slide.Elements)
        {
            DrawElement(context, element);
        }

        return drawing;
    }

    private static void DrawBackground(Context context, PresentationFill background)
    {
        var drawing = context.Drawing;
        var paint = ToPaint(context, background) ?? SlidePaint.Solid("FFFFFF");

        drawing.Items.Add(new SlideShapeDrawing
        {
            Paths = [SlideGeometry.Rectangle(0, 0, drawing.Width, drawing.Height)],
            Fill = paint,
            Box = (0, 0, drawing.Width, drawing.Height),
        });
    }

    private static void DrawElement(Context context, PresentationElement element)
    {
        if (element.Hidden)
        {
            return;
        }

        switch (element.Kind)
        {
            case PresentationElementKind.Group:
                foreach (var child in element.Children)
                {
                    DrawElement(context, child);
                }

                break;

            case PresentationElementKind.Shape:
            case PresentationElementKind.TextBox:
            case PresentationElementKind.Placeholder:
                DrawShape(context, element);
                break;

            case PresentationElementKind.Connector:
                DrawShape(context, element);
                break;

            case PresentationElementKind.Picture:
                DrawPicture(context, element, "Picture");
                break;

            case PresentationElementKind.Video:
            case PresentationElementKind.Audio:
                DrawMedia(context, element);
                break;

            case PresentationElementKind.Table:
                DrawTable(context, element);
                break;

            case PresentationElementKind.Chart:
                DrawChart(context, element);
                break;

            default:
                if (element.Image is { Data.Length: > 0 })
                {
                    DrawPicture(context, element, element.UnsupportedDescription ?? "Object");
                }
                else
                {
                    DrawPlaceholder(context, element, element.UnsupportedDescription ?? element.KindName, element.FallbackText);
                }

                break;
        }
    }

    private static void DrawShape(Context context, PresentationElement element)
    {
        var (x, y, w, h) = Box(element.Bounds);
        var isLine = element.Kind == PresentationElementKind.Connector || element.Geometry is "line" or "straightConnector1";

        if (!isLine && (w <= 0.1 || h <= 0.1))
        {
            return;
        }

        var hasText = element.Text?.HasText == true;

        // An empty placeholder is a prompt in PowerPoint's editor and nothing at all in the slide show.
        if (element.Kind == PresentationElementKind.Placeholder && !hasText && !element.Fill.IsVisible && !element.Line.IsVisible)
        {
            return;
        }

        var paths = SlideGeometry.Build(element, x, y, w, h);
        var fill = isLine ? null : ToPaint(context, element.Fill);
        var stroke = ToStroke(element.Line);

        if (fill is not null || stroke is not null)
        {
            var shape = new SlideShapeDrawing
            {
                Paths = paths,
                Fill = fill,
                Stroke = stroke,
                Shadow = element.HasShadow,
                Box = (x, y, w, h),
            };

            Transform(shape, element, x, y, w, h);
            context.Drawing.Items.Add(shape);

            if (stroke is not null && (stroke.StartArrow != "none" || stroke.EndArrow != "none"))
            {
                AddArrowheads(context, element, paths, stroke, x, y, w, h);
            }
        }

        if (hasText)
        {
            var rectangle = SlideGeometry.TextRectangle(element.Geometry, x, y, w, h);
            DrawText(context, element.Text, rectangle.X, rectangle.Y, rectangle.W, rectangle.H, element, x, y, w, h);
        }
    }

    private static void DrawText(
        Context context,
        PresentationTextBody body,
        double x,
        double y,
        double w,
        double h,
        PresentationElement owner,
        double ownerX,
        double ownerY,
        double ownerWidth,
        double ownerHeight)
    {
        var layout = PresentationTextLayout.Layout(body, x, y, w, h, 1, body.LineSpacingReduction);
        var text = new SlideTextDrawing();

        foreach (var line in layout.Lines)
        {
            if (line.Spans.Count == 0 && line.Marker is null)
            {
                continue;
            }

            var drawn = new SlideTextLine
            {
                X = line.X,
                Baseline = line.Baseline,
                Anchor = line.Anchor,
                Width = line.Width,
                Runs = line.Spans.Select(ToRun).ToList(),
                Marker = line.Marker is null ? null : ToRun(line.Marker),
                MarkerX = line.MarkerX,
            };

            text.Lines.Add(drawn);
        }

        if (text.Lines.Count == 0)
        {
            return;
        }

        if (owner is not null)
        {
            // Text turns with its shape but is never mirrored: PowerPoint keeps flipped text readable, turning a
            // vertically flipped shape's text upside down instead.
            text.Rotation = owner.Rotation + (owner.FlipVertical ? 180 : 0);
            text.CenterX = ownerX + (ownerWidth / 2);
            text.CenterY = ownerY + (ownerHeight / 2);
        }

        context.Drawing.Items.Add(text);
    }

    private static SlideTextRun ToRun(TextLayoutSpan span)
    {
        return new SlideTextRun
        {
            Text = span.Text,
            Font = span.Style.Font,
            Size = span.Style.Size,
            Bold = span.Style.Bold,
            Italic = span.Style.Italic,
            Underline = span.Style.Underline,
            Strikethrough = span.Style.Strikethrough,
            Color = span.Style.Color ?? "000000",
            Highlight = span.Style.Highlight,
            Baseline = span.Style.Baseline,
            Width = span.Width,
        };
    }

    private static void DrawPicture(Context context, PresentationElement element, string label)
    {
        var (x, y, w, h) = Box(element.Bounds);

        if (w <= 0.1 || h <= 0.1)
        {
            return;
        }

        var image = element.Image;

        if (image?.Data is not { Length: > 0 } || !_displayableTypes.Contains(image.ContentType ?? string.Empty))
        {
            var reason = image?.IsMissing == true
                ? label + " (missing)"
                : image?.Data is null && image?.ByteLength > 0
                    ? label + " (too large to preview)"
                    : image?.ContentType is { } type && !_displayableTypes.Contains(type)
                        ? label + " (" + ShortType(type) + ")"
                        : label;

            DrawPlaceholder(context, element, reason, element.AltText);

            return;
        }

        if (context.ImageBytes + image.Data.Length > context.Options.MaxImageBytes)
        {
            context.Drawing.OmittedImageBytes += image.Data.Length;
            DrawPlaceholder(context, element, label + " (left out of the preview to keep it small)", element.AltText);

            return;
        }

        context.ImageBytes += image.Data.Length;

        var picture = new SlideImageDrawing
        {
            X = x,
            Y = y,
            Width = w,
            Height = h,
            Data = image.Data,
            ContentType = image.ContentType,
            CropLeft = image.CropLeft,
            CropTop = image.CropTop,
            CropRight = image.CropRight,
            CropBottom = image.CropBottom,
            Alpha = image.Alpha,
            Clip = element.Geometry is null or "rect" ? null : SlideGeometry.Build(element, x, y, w, h),
        };

        Transform(picture, element, x, y, w, h);
        context.Drawing.Items.Add(picture);

        if (ToStroke(element.Line) is { } stroke)
        {
            var outline = new SlideShapeDrawing { Paths = SlideGeometry.Build(element, x, y, w, h), Stroke = stroke, Box = (x, y, w, h) };
            Transform(outline, element, x, y, w, h);
            context.Drawing.Items.Add(outline);
        }
    }

    private static void DrawMedia(Context context, PresentationElement element)
    {
        if (element.Image is { Data.Length: > 0 })
        {
            DrawPicture(context, element, element.Kind == PresentationElementKind.Video ? "Video" : "Audio");
        }
        else
        {
            DrawPlaceholder(context, element, element.Kind == PresentationElementKind.Video ? "Video" : "Audio clip", element.MediaUrl);
        }

        var (x, y, w, h) = Box(element.Bounds);

        if (element.Kind == PresentationElementKind.Video && w > 30 && h > 30)
        {
            // The play button PowerPoint shows over a video's poster.
            var radius = Math.Min(w, h) * 0.12;
            var cx = x + (w / 2);
            var cy = y + (h / 2);

            context.Drawing.Items.Add(new SlideShapeDrawing { Paths = [SlideGeometry.Ellipse(cx, cy, radius, radius)], Fill = SlidePaint.Solid("000000", 0.55) });
            context.Drawing.Items.Add(new SlideShapeDrawing { Paths = [SlideGeometry.Polygon((cx - (radius * 0.35), cy - (radius * 0.5)), (cx + (radius * 0.55), cy), (cx - (radius * 0.35), cy + (radius * 0.5)))], Fill = SlidePaint.Solid("FFFFFF") });
        }
    }

    private static void DrawPlaceholder(Context context, PresentationElement element, string label, string detail)
    {
        var (x, y, w, h) = Box(element.Bounds);

        if (w <= 0.1 || h <= 0.1)
        {
            return;
        }

        context.Drawing.Placeholders.Add(label);

        var box = new SlideShapeDrawing
        {
            Paths = [SlideGeometry.Rectangle(x, y, w, h)],
            Fill = SlidePaint.Solid("F3F4F6"),
            Stroke = new SlideStroke { Color = "9CA3AF", Width = 1, Dash = "dash" },
            Box = (x, y, w, h),
        };

        Transform(box, element, x, y, w, h);
        context.Drawing.Items.Add(box);

        var paragraphs = new List<PresentationParagraph>
        {
            new()
            {
                Alignment = "center",
                Runs = [new PresentationTextRun { Text = label, Size = Math.Clamp(h / 8, 8, 16), Bold = true, Color = "4B5563", Font = context.Model.Theme.BodyFont }],
            },
        };

        if (!string.IsNullOrWhiteSpace(detail))
        {
            var excerpt = detail.Length > 160 ? detail[..157] + "…" : detail;

            paragraphs.Add(new PresentationParagraph
            {
                Alignment = "center",
                SpaceBefore = 4,
                Runs = [new PresentationTextRun { Text = excerpt, Size = Math.Clamp(h / 12, 7, 12), Color = "6B7280", Font = context.Model.Theme.BodyFont }],
            });
        }

        DrawText(context, new PresentationTextBody { Paragraphs = paragraphs, VerticalAnchor = "middle" }, x, y, w, h, null, 0, 0, 0, 0);
    }

    private static void DrawTable(Context context, PresentationElement element)
    {
        var table = element.Table;

        if (table is null || table.Rows.Count == 0 || table.ColumnWidths.Count == 0)
        {
            return;
        }

        var (x, y, _, _) = Box(element.Bounds);
        var widths = table.ColumnWidths.Select(PresentationUnits.ToPoints).ToList();
        var heights = new List<double>();

        // Rows grow to fit their text the way PowerPoint grows them; the stored height is only a minimum.
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var height = rowIndex < table.RowHeights.Count ? PresentationUnits.ToPoints(table.RowHeights[rowIndex]) : 20;

            for (var column = 0; column < row.Count && column < widths.Count; column++)
            {
                var cell = row[column];

                if (cell.IsMerged || cell.RowSpan > 1)
                {
                    continue;
                }

                var width = widths.Skip(column).Take(Math.Max(1, cell.ColumnSpan)).Sum();
                var layout = PresentationTextLayout.Layout(cell.Text, 0, 0, width, 10_000, 1, cell.Text.LineSpacingReduction);
                height = Math.Max(height, layout.RequiredHeight);
            }

            heights.Add(height);
        }

        var top = y;

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var left = x;

            for (var column = 0; column < row.Count && column < widths.Count; column++)
            {
                var cell = row[column];
                var width = widths.Skip(column).Take(Math.Max(1, cell.ColumnSpan)).Sum();
                var height = heights.Skip(rowIndex).Take(Math.Max(1, cell.RowSpan)).Sum();

                if (!cell.IsMerged)
                {
                    if (ToPaint(context, cell.Fill) is { } paint)
                    {
                        context.Drawing.Items.Add(new SlideShapeDrawing { Paths = [SlideGeometry.Rectangle(left, top, width, height)], Fill = paint, Box = (left, top, width, height) });
                    }

                    if (cell.Text.HasText)
                    {
                        DrawText(context, cell.Text, left, top, width, height, null, 0, 0, 0, 0);
                    }

                    Border(context, cell.BorderTop, left, top, left + width, top);
                    Border(context, cell.BorderBottom, left, top + height, left + width, top + height);
                    Border(context, cell.BorderLeft, left, top, left, top + height);
                    Border(context, cell.BorderRight, left + width, top, left + width, top + height);
                }

                left += widths[column];
            }

            top += heights[rowIndex];
        }
    }

    private static void Border(Context context, PresentationLine line, double x1, double y1, double x2, double y2)
    {
        if (ToStroke(line) is not { } stroke)
        {
            return;
        }

        context.Drawing.Items.Add(new SlideShapeDrawing
        {
            Paths = [new SlidePath { Filled = false, Commands = [SlidePathCommand.MoveTo(x1, y1), SlidePathCommand.LineTo(x2, y2)] }],
            Stroke = stroke,
        });
    }

    private static void DrawChart(Context context, PresentationElement element)
    {
        var (x, y, w, h) = Box(element.Bounds);

        if (element.Chart is null || element.Chart.Kind == "other")
        {
            DrawPlaceholder(context, element, "Chart", element.Chart?.Title);

            return;
        }

        var dark = context.Background is { } background && background.Color is { } color && PresentationColor.RelativeLuminance(color) < 0.3;
        var textColor = dark ? "D9D9D9" : "595959";

        SlideChartRenderer.Draw(element.Chart, x, y, w, h, context.Model.Theme.BodyFont, textColor, context.Drawing.Items);
    }

    private static void AddArrowheads(Context context, PresentationElement element, List<SlidePath> paths, SlideStroke stroke, double x, double y, double w, double h)
    {
        var points = paths.SelectMany(path => path.Commands).Where(command => command.Points.Length > 0).SelectMany(command => command.Points).ToList();

        if (points.Count < 2)
        {
            return;
        }

        var size = Math.Max(4, stroke.Width * 3.5);

        void Head(string kind, (double X, double Y) tip, (double X, double Y) from)
        {
            if (kind is null or "none")
            {
                return;
            }

            var angle = Math.Atan2(tip.Y - from.Y, tip.X - from.X);
            var back = (X: tip.X - (Math.Cos(angle) * size), Y: tip.Y - (Math.Sin(angle) * size));
            var side = (X: Math.Cos(angle + (Math.PI / 2)) * size * 0.5, Y: Math.Sin(angle + (Math.PI / 2)) * size * 0.5);
            var middle = (X: (tip.X + back.X) / 2, Y: (tip.Y + back.Y) / 2);
            var head = kind switch
            {
                "oval" => SlideGeometry.Ellipse(tip.X, tip.Y, size * 0.45, size * 0.45),
                "diamond" => SlideGeometry.Polygon(tip, (middle.X + side.X, middle.Y + side.Y), back, (middle.X - side.X, middle.Y - side.Y)),
                _ => SlideGeometry.Polygon(tip, (back.X + side.X, back.Y + side.Y), (back.X - side.X, back.Y - side.Y)),
            };

            var shape = new SlideShapeDrawing { Paths = [head], Fill = SlidePaint.Solid(stroke.Color, stroke.Alpha), Box = (x, y, w, h) };
            Transform(shape, element, x, y, w, h);
            context.Drawing.Items.Add(shape);
        }

        Head(stroke.EndArrow, points[^1], points[^2]);
        Head(stroke.StartArrow, points[0], points[1]);
    }

    private static void Transform(SlideDrawingItem item, PresentationElement element, double x, double y, double w, double h)
    {
        item.Rotation = element.Rotation;
        item.FlipHorizontal = element.FlipHorizontal;
        item.FlipVertical = element.FlipVertical;
        item.CenterX = x + (w / 2);
        item.CenterY = y + (h / 2);
    }

    private static SlidePaint ToPaint(Context context, PresentationFill fill)
    {
        if (fill is null || !fill.IsVisible)
        {
            return null;
        }

        switch (fill.Kind)
        {
            case PresentationFillKind.Gradient:
                return new SlidePaint
                {
                    Color = fill.Color,
                    Alpha = fill.Alpha,
                    Stops = fill.Stops,
                    Angle = fill.GradientAngle,
                    Radial = fill.IsRadial,
                };

            case PresentationFillKind.Picture:
                if (fill.Image?.Data is { Length: > 0 } data && _displayableTypes.Contains(fill.Image.ContentType ?? string.Empty) &&
                    context.ImageBytes + data.Length <= context.Options.MaxImageBytes)
                {
                    context.ImageBytes += data.Length;

                    return new SlidePaint { Image = fill.Image, Alpha = fill.Image.Alpha };
                }

                context.Drawing.OmittedImageBytes += fill.Image?.ByteLength ?? 0;

                return SlidePaint.Solid("E5E7EB");

            default:
                return fill.Color is null ? null : SlidePaint.Solid(fill.Color, fill.Alpha);
        }
    }

    private static SlideStroke ToStroke(PresentationLine line)
    {
        if (line is null || !line.IsVisible)
        {
            return null;
        }

        return new SlideStroke
        {
            Color = line.Color,
            Alpha = line.Alpha,
            Width = Math.Max(0.25, line.Width),
            Dash = line.Dash ?? "solid",
            StartArrow = line.StartArrow ?? "none",
            EndArrow = line.EndArrow ?? "none",
        };
    }

    private static (double X, double Y, double W, double H) Box(PresentationBounds bounds)
    {
        return (PresentationUnits.ToPoints(bounds.X), PresentationUnits.ToPoints(bounds.Y), PresentationUnits.ToPoints(bounds.Width), PresentationUnits.ToPoints(bounds.Height));
    }

    private static string ShortType(string contentType)
    {
        var slash = contentType.IndexOf('/');
        var name = slash >= 0 ? contentType[(slash + 1)..] : contentType;

        return name.Replace("x-", string.Empty, StringComparison.Ordinal).ToUpperInvariant() + " image";
    }

    /// <summary>
    /// What drawing one slide keeps track of.
    /// </summary>
    private sealed class Context
    {
        public Context(SlideDrawing drawing, PresentationModel model, SlideDrawingOptions options, PresentationFill background)
        {
            Drawing = drawing;
            Model = model;
            Options = options;
            Background = background;
        }

        public SlideDrawing Drawing { get; }

        public PresentationModel Model { get; }

        public SlideDrawingOptions Options { get; }

        public PresentationFill Background { get; }

        public long ImageBytes { get; set; }
    }
}
