using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Graphics.Core;
using UglyToad.PdfPig.Graphics.Operations.SpecialGraphicsState;
using UglyToad.PdfPig.Graphics.Operations.TextShowing;
using UglyToad.PdfPig.Tokens;

namespace CrestApps.Core.AI.Documents.Pdf.Rendering;

/// <summary>
/// Draws PDF pages as SVG pictures from the geometry the file itself records: every glyph at its baseline,
/// every filled and stroked path, every placed picture, the visible annotations and the values of form
/// fields.
/// </summary>
/// <remarks>
/// Nothing in this process can rasterize a PDF, and shipping a rasterizer means shipping a native library
/// with the package. The file already says where everything goes, though, so reading that and writing it back
/// out as SVG gives a picture that matches the file — the same page an export writes, because it is drawn
/// from those very bytes — and the browser that shows it supplies the fonts.
/// <para>
/// The picture is faithful in layout, colour and text, and approximate in typography: glyphs are drawn in the
/// nearest generic family and stretched to the width the file gives them, so a line breaks where it breaks
/// in the file. Clipping, gradients and blend modes are not reproduced.
/// </para>
/// <para>
/// Every value that reaches the markup is either a number this code formatted or text it escaped. The file's
/// own strings — font names included — never become attributes, because the picture is served from this
/// host's origin.
/// </para>
/// </remarks>
internal static class PdfPageSvgRenderer
{
    private const string SansFamily = "Arial, Helvetica, sans-serif";
    private const string SerifFamily = "'Times New Roman', Times, serif";
    private const string MonospaceFamily = "'Courier New', Courier, monospace";

    /// <summary>
    /// Draws pages of a PDF.
    /// </summary>
    /// <param name="pdf">The PDF file.</param>
    /// <param name="pageNumbers">The one-based pages to draw.</param>
    /// <param name="options">The drawing limits.</param>
    /// <returns>One drawing per page, in the order asked for.</returns>
    public static List<PdfPageRendering> Render(byte[] pdf, IReadOnlyList<int> pageNumbers, PdfPreviewOptions options)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(pageNumbers);
        ArgumentNullException.ThrowIfNull(options);

        using var document = PdfDocument.Open(pdf);

        document.TryGetForm(out var form);

        var renderings = new List<PdfPageRendering>(pageNumbers.Count);

        foreach (var number in pageNumbers)
        {
            if (number < 1 || number > document.NumberOfPages)
            {
                continue;
            }

            renderings.Add(RenderPage(document, document.GetPage(number), form, options));
        }

        return renderings;
    }

    private static PdfPageRendering RenderPage(PdfDocument document, Page page, AcroForm form, PdfPreviewOptions options)
    {
        var box = page.CropBox?.Bounds ?? page.MediaBox.Bounds;
        var canvas = new Canvas(box.Left, box.Bottom, box.Width, box.Height);
        var rotation = ((int)page.Rotation.Value % 360 + 360) % 360;
        var displayWidth = rotation is 90 or 270 ? canvas.Height : canvas.Width;
        var displayHeight = rotation is 90 or 270 ? canvas.Width : canvas.Height;
        var pixelWidth = Math.Max(200, options.PageWidthPixels);
        var pixelHeight = pixelWidth * displayHeight / Math.Max(displayWidth, 1);

        var builder = new StringBuilder(16 * 1024);

        builder
            .Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(Number(pixelWidth))
            .Append("\" height=\"").Append(Number(pixelHeight))
            .Append("\" viewBox=\"0 0 ").Append(Number(displayWidth)).Append(' ').Append(Number(displayHeight))
            .Append("\">");

        builder
            .Append("<rect x=\"0\" y=\"0\" width=\"").Append(Number(displayWidth))
            .Append("\" height=\"").Append(Number(displayHeight)).Append("\" fill=\"#ffffff\"/>");

        switch (rotation)
        {
            case 90:
                builder.Append("<g transform=\"translate(").Append(Number(canvas.Height)).Append(" 0) rotate(90)\">");

                break;
            case 180:
                builder.Append("<g transform=\"translate(").Append(Number(canvas.Width)).Append(' ').Append(Number(canvas.Height)).Append(") rotate(180)\">");

                break;
            case 270:
                builder.Append("<g transform=\"translate(0 ").Append(Number(canvas.Width)).Append(") rotate(270)\">");

                break;
            default:
                builder.Append("<g>");

                break;
        }

        var simplified = AppendPaths(builder, page, canvas, options.MaxPathSegmentsPerPage);
        var (drawn, skipped) = AppendImages(builder, page, canvas, options.MaxImageBytesPerPage);

        AppendText(builder, document, page, canvas);
        AppendAnnotations(builder, document, page, canvas);
        AppendFormValues(builder, form, page.Number, canvas);

        builder.Append("</g></svg>");

        return new PdfPageRendering(page.Number, builder.ToString(), displayWidth, displayHeight, drawn, skipped, simplified);
    }

    private static bool AppendPaths(StringBuilder builder, Page page, Canvas canvas, int maxSegments)
    {
        var segments = 0;
        IReadOnlyList<PdfPath> paths;

        try
        {
            paths = page.Paths;
        }
        catch (Exception)
        {
            return true;
        }

        foreach (var path in paths)
        {
            if (path is null || path.IsClipping || (!path.IsFilled && !path.IsStroked))
            {
                continue;
            }

            var data = new StringBuilder();

            foreach (var subpath in path)
            {
                foreach (var command in subpath.Commands)
                {
                    if (++segments > maxSegments)
                    {
                        return true;
                    }

                    switch (command)
                    {
                        case PdfSubpath.Move move:
                            data.Append('M').Append(canvas.X(move.Location.X)).Append(' ').Append(canvas.Y(move.Location.Y));

                            break;
                        case PdfSubpath.Line line:
                            data.Append('L').Append(canvas.X(line.To.X)).Append(' ').Append(canvas.Y(line.To.Y));

                            break;
                        case PdfSubpath.CubicBezierCurve cubic:
                            data.Append('C')
                                .Append(canvas.X(cubic.FirstControlPoint.X)).Append(' ').Append(canvas.Y(cubic.FirstControlPoint.Y)).Append(' ')
                                .Append(canvas.X(cubic.SecondControlPoint.X)).Append(' ').Append(canvas.Y(cubic.SecondControlPoint.Y)).Append(' ')
                                .Append(canvas.X(cubic.EndPoint.X)).Append(' ').Append(canvas.Y(cubic.EndPoint.Y));

                            break;
                        case PdfSubpath.QuadraticBezierCurve quadratic:
                            data.Append('Q')
                                .Append(canvas.X(quadratic.ControlPoint.X)).Append(' ').Append(canvas.Y(quadratic.ControlPoint.Y)).Append(' ')
                                .Append(canvas.X(quadratic.EndPoint.X)).Append(' ').Append(canvas.Y(quadratic.EndPoint.Y));

                            break;
                        case PdfSubpath.Close:
                            data.Append('Z');

                            break;
                    }
                }
            }

            if (data.Length == 0)
            {
                continue;
            }

            builder.Append("<path d=\"").Append(data).Append("\" fill=\"")
                .Append(path.IsFilled ? ToHex(path.FillColor) : "none").Append('"');

            if (path.IsFilled && path.FillingRule == FillingRule.EvenOdd)
            {
                builder.Append(" fill-rule=\"evenodd\"");
            }

            if (path.IsStroked)
            {
                builder.Append(" stroke=\"").Append(ToHex(path.StrokeColor))
                    .Append("\" stroke-width=\"").Append(Number(Math.Max(path.LineWidth, 0.25))).Append('"');

                if (path.LineDashPattern is { } dash && dash.Array is { Count: > 0 } array && array.Any(value => value > 0))
                {
                    builder.Append(" stroke-dasharray=\"").Append(string.Join(' ', array.Select(value => Number(Math.Max(value, 0))))).Append('"');
                }

                builder.Append(path.LineCapStyle switch
                {
                    LineCapStyle.Round => " stroke-linecap=\"round\"",
                    LineCapStyle.ProjectingSquare => " stroke-linecap=\"square\"",
                    _ => string.Empty,
                });
            }

            builder.Append("/>");
        }

        return false;
    }

    private static (int Drawn, int Skipped) AppendImages(StringBuilder builder, Page page, Canvas canvas, int maxBytes)
    {
        var drawn = 0;
        var skipped = 0;
        var budget = Math.Max(0, maxBytes);
        IEnumerable<IPdfImage> images;

        try
        {
            images = page.GetImages().ToList();
        }
        catch (Exception)
        {
            return (0, 0);
        }

        foreach (var image in images)
        {
            var bounds = image.BoundingBox;

            if (bounds.Width < 0.5 || bounds.Height < 0.5)
            {
                continue;
            }

            var (bytes, mediaType) = TryEncode(image);

            if (bytes is not null && bytes.Length <= budget)
            {
                budget -= bytes.Length;
                drawn++;

                builder
                    .Append("<image x=\"").Append(canvas.X(bounds.Left)).Append("\" y=\"").Append(canvas.Y(bounds.Top))
                    .Append("\" width=\"").Append(Number(bounds.Width)).Append("\" height=\"").Append(Number(bounds.Height))
                    .Append("\" preserveAspectRatio=\"none\" href=\"data:").Append(mediaType).Append(";base64,")
                    .Append(Convert.ToBase64String(bytes)).Append("\"/>");

                continue;
            }

            skipped++;

            // A picture that cannot be drawn is still shown to be there, so the reader does not mistake the
            // page for one that has nothing on it.
            builder
                .Append("<rect x=\"").Append(canvas.X(bounds.Left)).Append("\" y=\"").Append(canvas.Y(bounds.Top))
                .Append("\" width=\"").Append(Number(bounds.Width)).Append("\" height=\"").Append(Number(bounds.Height))
                .Append("\" fill=\"#eef1f5\" stroke=\"#b9c2cd\" stroke-width=\"0.75\"/>");

            if (bounds.Width > 40 && bounds.Height > 14)
            {
                builder
                    .Append("<text x=\"").Append(Number(canvas.XValue(bounds.Left) + (bounds.Width / 2)))
                    .Append("\" y=\"").Append(Number(canvas.YValue(bounds.Top) + (bounds.Height / 2) + 3))
                    .Append("\" font-family=\"").Append(SansFamily).Append("\" font-size=\"9\" fill=\"#5b6673\" text-anchor=\"middle\">image</text>");
            }
        }

        return (drawn, skipped);
    }

    /// <summary>
    /// Returns a picture's bytes in a format a browser draws: JPEG as stored, anything PdfPig can decode as PNG.
    /// </summary>
    /// <param name="image">The picture.</param>
    /// <returns>The bytes and their media type, or <see langword="null"/> when the picture cannot be drawn.</returns>
    public static (byte[] Bytes, string MediaType) TryEncode(IPdfImage image)
    {
        try
        {
            if (IsSingleFilter(image, "DCTDecode"))
            {
                return (image.RawBytes.ToArray(), "image/jpeg");
            }

            if (image.TryGetPng(out var png) && png is { Length: > 0 })
            {
                return (png, "image/png");
            }
        }
        catch (Exception)
        {
            // An image PdfPig cannot decode is drawn as a placeholder, never allowed to fail the page.
        }

        return (null, null);
    }

    private static bool IsSingleFilter(IPdfImage image, string filter)
    {
        if (image.ImageDictionary is null || !image.ImageDictionary.TryGet(NameToken.Filter, out var token))
        {
            return false;
        }

        return token switch
        {
            NameToken name => string.Equals(name.Data, filter, StringComparison.Ordinal),
            ArrayToken { Data.Count: 1 } array => array.Data[0] is NameToken only && string.Equals(only.Data, filter, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static void AppendText(StringBuilder builder, PdfDocument document, Page page, Canvas canvas)
    {
        IReadOnlyList<Letter> letters;

        try
        {
            letters = page.Letters;
        }
        catch (Exception)
        {
            return;
        }

        var opacity = ReadTextOpacity(document, page);
        var index = 0;

        while (index < letters.Count)
        {
            var first = letters[index];

            if (IsInvisible(first) || string.IsNullOrEmpty(first.Value))
            {
                index++;

                continue;
            }

            var run = new StringBuilder(first.Value);
            var last = first;
            var color = ToHex(first.FillColor);
            var next = index + 1;

            while (next < letters.Count)
            {
                var candidate = letters[next];

                if (IsInvisible(candidate) ||
                    !string.Equals(candidate.FontName, first.FontName, StringComparison.Ordinal) ||
                    Math.Abs(candidate.PointSize - first.PointSize) > 0.05 ||
                    candidate.TextOrientation != first.TextOrientation ||
                    !SameBaseline(candidate, last) ||
                    Gap(last, candidate) > first.PointSize * 0.8 ||
                    Gap(last, candidate) < -first.PointSize * 0.3 ||
                    !string.Equals(ToHex(candidate.FillColor), color, StringComparison.Ordinal))
                {
                    break;
                }

                // A gap wider than a thin space is a word break the file drew as movement, not as a space
                // glyph; writing it out keeps the words apart when the run is stretched to its width.
                if (Gap(last, candidate) > first.PointSize * 0.12 && last.Value != " " && candidate.Value != " ")
                {
                    run.Append(' ');
                }

                run.Append(candidate.Value);
                last = candidate;
                next++;
            }

            AppendRun(builder, first, last, run.ToString(), color, opacity, canvas);
            index = next;
        }
    }

    private static void AppendRun(
        StringBuilder builder,
        Letter first,
        Letter last,
        string text,
        string color,
        Dictionary<int, double> opacity,
        Canvas canvas)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var fontName = first.FontName ?? string.Empty;
        var plus = fontName.IndexOf('+', StringComparison.Ordinal);

        if (plus >= 0 && plus < fontName.Length - 1)
        {
            fontName = fontName[(plus + 1)..];
        }

        var bold = first.FontDetails?.IsBold == true ||
            fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Black", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Heavy", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Semibold", StringComparison.OrdinalIgnoreCase);
        var italic = first.FontDetails?.IsItalic == true ||
            fontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

        var startX = first.StartBaseLine.X;
        var startY = first.StartBaseLine.Y;
        var length = Math.Sqrt(Math.Pow(last.EndBaseLine.X - startX, 2) + Math.Pow(last.EndBaseLine.Y - startY, 2));
        var fontSize = Math.Max(first.PointSize, 0.5);

        builder
            .Append("<text x=\"").Append(canvas.X(startX)).Append("\" y=\"").Append(canvas.Y(startY))
            .Append("\" font-family=\"").Append(FamilyFor(fontName))
            .Append("\" font-size=\"").Append(Number(fontSize))
            .Append("\" fill=\"").Append(color).Append('"');

        if (bold)
        {
            builder.Append(" font-weight=\"bold\"");
        }

        if (italic)
        {
            builder.Append(" font-style=\"italic\"");
        }

        if (opacity.TryGetValue(first.TextSequence, out var alpha) && alpha < 0.999)
        {
            builder.Append(" fill-opacity=\"").Append(Number(Math.Clamp(alpha, 0, 1))).Append('"');
        }

        // Stretching the run to the width the file gives it is what keeps a line breaking where it breaks in
        // the file, whatever font the browser substitutes.
        if (text.Length > 1 && length > fontSize * 0.5)
        {
            builder.Append(" textLength=\"").Append(Number(length)).Append("\" lengthAdjust=\"spacingAndGlyphs\"");
        }

        var dx = last.EndBaseLine.X - startX;
        var dy = last.EndBaseLine.Y - startY;

        if (first.TextOrientation != TextOrientation.Horizontal && (Math.Abs(dx) > 0.01 || Math.Abs(dy) > 0.01))
        {
            var angle = -Math.Atan2(dy, dx) * 180 / Math.PI;

            builder.Append(" transform=\"rotate(").Append(Number(angle)).Append(' ')
                .Append(canvas.X(startX)).Append(' ').Append(canvas.Y(startY)).Append(")\"");
        }

        builder.Append(" xml:space=\"preserve\">");
        AppendEscaped(builder, text);
        builder.Append("</text>");
    }

    /// <summary>
    /// Works out the fill opacity every text-showing operation ran with, keyed by the sequence number its
    /// letters carry, from the graphics state dictionaries the content stream selects.
    /// </summary>
    private static Dictionary<int, double> ReadTextOpacity(PdfDocument document, Page page)
    {
        var result = new Dictionary<int, double>();

        try
        {
            var resources = PdfPigTokens.GetDictionary(document, page.Dictionary, "Resources");
            var states = PdfPigTokens.GetDictionary(document, resources, "ExtGState");

            if (states is null)
            {
                return result;
            }

            var stack = new Stack<double>();
            var current = 1d;
            var sequence = 0;

            foreach (var operation in page.Operations)
            {
                switch (operation)
                {
                    case Push:
                        stack.Push(current);

                        break;
                    case Pop:
                        current = stack.Count > 0 ? stack.Pop() : 1d;

                        break;
                    case SetGraphicsStateParametersFromDictionary state:
                        var parameters = PdfPigTokens.GetDictionary(document, states, state.Name.Data);
                        var alpha = PdfPigTokens.GetNumber(document, parameters, "ca");

                        if (alpha.HasValue)
                        {
                            current = alpha.Value;
                        }

                        break;
                    case ShowText or ShowTextsWithPositioning or MoveToNextLineShowText or MoveToNextLineShowTextWithSpacing:
                        sequence++;

                        if (current < 0.999)
                        {
                            result[sequence] = current;
                        }

                        break;
                }
            }
        }
        catch (Exception)
        {
            // Opacity is a refinement; a page whose state cannot be read is drawn opaque.
        }

        return result;
    }

    private static void AppendAnnotations(StringBuilder builder, PdfDocument document, Page page, Canvas canvas)
    {
        IEnumerable<Annotation> annotations;

        try
        {
            annotations = page.GetAnnotations().ToList();
        }
        catch (Exception)
        {
            return;
        }

        foreach (var annotation in annotations)
        {
            // Hidden (2) and no-view (32) annotations are not shown by a viewer either.
            if (((int)annotation.Flags & (2 | 32)) != 0)
            {
                continue;
            }

            var color = ReadAnnotationColor(document, annotation.AnnotationDictionary, "C") ?? "#FFD54F";
            var rect = annotation.Rectangle;

            switch (annotation.Type)
            {
                case AnnotationType.Highlight:
                    AppendQuads(builder, annotation, canvas, quad =>
                        $"<polygon points=\"{quad}\" fill=\"{color}\" fill-opacity=\"0.35\"/>");

                    break;

                case AnnotationType.Underline:
                case AnnotationType.StrikeOut:
                case AnnotationType.Squiggly:
                    AppendMarkupLines(builder, annotation, canvas, color, strike: annotation.Type == AnnotationType.StrikeOut);

                    break;

                case AnnotationType.Square:
                case AnnotationType.Circle:
                    {
                        var interior = ReadAnnotationColor(document, annotation.AnnotationDictionary, "IC");
                        var width = Number(Math.Max(annotation.Border?.BorderWidth ?? 1, 0.5));

                        if (annotation.Type == AnnotationType.Square)
                        {
                            builder.Append("<rect x=\"").Append(canvas.X(rect.Left)).Append("\" y=\"").Append(canvas.Y(rect.Top))
                                .Append("\" width=\"").Append(Number(rect.Width)).Append("\" height=\"").Append(Number(rect.Height))
                                .Append("\" fill=\"").Append(interior ?? "none").Append("\" stroke=\"").Append(color)
                                .Append("\" stroke-width=\"").Append(width).Append("\"/>");
                        }
                        else
                        {
                            builder.Append("<ellipse cx=\"").Append(Number(canvas.XValue(rect.Left) + (rect.Width / 2)))
                                .Append("\" cy=\"").Append(Number(canvas.YValue(rect.Top) + (rect.Height / 2)))
                                .Append("\" rx=\"").Append(Number(rect.Width / 2)).Append("\" ry=\"").Append(Number(rect.Height / 2))
                                .Append("\" fill=\"").Append(interior ?? "none").Append("\" stroke=\"").Append(color)
                                .Append("\" stroke-width=\"").Append(width).Append("\"/>");
                        }

                        break;
                    }

                case AnnotationType.FreeText:
                    AppendBoxedText(builder, canvas, rect, annotation.Content, "#1f2933", fill: null, border: color);

                    break;

                case AnnotationType.Stamp:
                    AppendBoxedText(builder, canvas, rect, StampLabel(annotation), color, fill: null, border: color);

                    break;

                case AnnotationType.Text:
                    // A sticky note is drawn as the small note icon a viewer shows.
                    builder.Append("<rect x=\"").Append(canvas.X(rect.Left)).Append("\" y=\"").Append(canvas.Y(rect.Top))
                        .Append("\" width=\"14\" height=\"14\" rx=\"2\" fill=\"").Append(color)
                        .Append("\" stroke=\"#8a6d00\" stroke-width=\"0.75\"/>");
                    builder.Append("<path d=\"M").Append(Number(canvas.XValue(rect.Left) + 3)).Append(' ').Append(Number(canvas.YValue(rect.Top) + 4.5))
                        .Append("h8M").Append(Number(canvas.XValue(rect.Left) + 3)).Append(' ').Append(Number(canvas.YValue(rect.Top) + 7.5))
                        .Append("h8M").Append(Number(canvas.XValue(rect.Left) + 3)).Append(' ').Append(Number(canvas.YValue(rect.Top) + 10.5))
                        .Append("h5\" stroke=\"#5a4700\" stroke-width=\"0.9\"/>");

                    break;

                case AnnotationType.Ink:
                    AppendInk(builder, document, annotation, canvas, color);

                    break;

                case AnnotationType.Line:
                    {
                        var points = PdfPigTokens.GetNumbers(document, annotation.AnnotationDictionary, "L");

                        if (points.Count >= 4)
                        {
                            builder.Append("<line x1=\"").Append(canvas.X(points[0])).Append("\" y1=\"").Append(canvas.Y(points[1]))
                                .Append("\" x2=\"").Append(canvas.X(points[2])).Append("\" y2=\"").Append(canvas.Y(points[3]))
                                .Append("\" stroke=\"").Append(color).Append("\" stroke-width=\"1.5\"/>");
                        }

                        break;
                    }
            }
        }
    }

    private static void AppendQuads(StringBuilder builder, Annotation annotation, Canvas canvas, Func<string, string> draw)
    {
        var quads = annotation.QuadPoints;

        if (quads is { Count: > 0 })
        {
            foreach (var quad in quads)
            {
                var points = quad.Points.ToArray();

                if (points.Length != 4)
                {
                    continue;
                }

                // Quad points are written top-left, top-right, bottom-left, bottom-right; a polygon walks the edge.
                builder.Append(draw(string.Join(' ', new[] { points[0], points[1], points[3], points[2] }.Select(point => canvas.X(point.X) + "," + canvas.Y(point.Y)))));
            }

            return;
        }

        var rect = annotation.Rectangle;

        builder.Append(draw(string.Join(' ', new[] { rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft }.Select(point => canvas.X(point.X) + "," + canvas.Y(point.Y)))));
    }

    private static void AppendMarkupLines(StringBuilder builder, Annotation annotation, Canvas canvas, string color, bool strike)
    {
        var boxes = new List<PdfRectangle>();

        foreach (var quad in annotation.QuadPoints ?? [])
        {
            var points = quad.Points.ToArray();

            if (points.Length == 4)
            {
                boxes.Add(new PdfRectangle(
                    points.Min(point => point.X),
                    points.Min(point => point.Y),
                    points.Max(point => point.X),
                    points.Max(point => point.Y)));
            }
        }

        if (boxes.Count == 0)
        {
            boxes.Add(annotation.Rectangle);
        }

        foreach (var box in boxes)
        {
            var y = strike
                ? box.Bottom + (box.Height / 2)
                : box.Bottom + (box.Height * 0.1);

            builder.Append("<line x1=\"").Append(canvas.X(box.Left)).Append("\" y1=\"").Append(canvas.Y(y))
                .Append("\" x2=\"").Append(canvas.X(box.Right)).Append("\" y2=\"").Append(canvas.Y(y))
                .Append("\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(Number(Math.Max(box.Height * 0.07, 0.8))).Append("\"/>");
        }
    }

    private static void AppendInk(StringBuilder builder, PdfDocument document, Annotation annotation, Canvas canvas, string color)
    {
        if (!annotation.AnnotationDictionary.TryGet(NameToken.Create("InkList"), out var token) ||
            PdfPigTokens.Resolve(document, token) is not ArrayToken strokes)
        {
            return;
        }

        foreach (var stroke in strokes.Data)
        {
            if (PdfPigTokens.Resolve(document, stroke) is not ArrayToken coordinates)
            {
                continue;
            }

            var values = coordinates.Data.Select(value => PdfPigTokens.Resolve(document, value)).OfType<NumericToken>().Select(value => value.Double).ToList();
            var points = new StringBuilder();

            for (var index = 0; index + 1 < values.Count; index += 2)
            {
                points.Append(canvas.X(values[index])).Append(',').Append(canvas.Y(values[index + 1])).Append(' ');
            }

            if (points.Length > 0)
            {
                builder.Append("<polyline points=\"").Append(points.ToString().TrimEnd())
                    .Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
            }
        }
    }

    private static string StampLabel(Annotation annotation)
    {
        if (!string.IsNullOrWhiteSpace(annotation.Content))
        {
            return annotation.Content;
        }

        var name = annotation.AnnotationDictionary.TryGet(NameToken.Create("Name"), out var token) && token is NameToken nameToken
            ? nameToken.Data
            : "Stamp";

        // A standard stamp is named in PascalCase (NotApproved, ForPublicRelease); a viewer prints it as words.
        var words = new StringBuilder();

        foreach (var character in name)
        {
            if (char.IsUpper(character) && words.Length > 0)
            {
                words.Append(' ');
            }

            words.Append(char.ToUpperInvariant(character));
        }

        return words.ToString();
    }

    private static void AppendBoxedText(StringBuilder builder, Canvas canvas, PdfRectangle rect, string text, string color, string fill, string border)
    {
        builder.Append("<rect x=\"").Append(canvas.X(rect.Left)).Append("\" y=\"").Append(canvas.Y(rect.Top))
            .Append("\" width=\"").Append(Number(rect.Width)).Append("\" height=\"").Append(Number(rect.Height))
            .Append("\" fill=\"").Append(fill ?? "none").Append("\" stroke=\"").Append(border).Append("\" stroke-width=\"1.25\"/>");

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var fontSize = Math.Clamp(rect.Height * 0.45, 5, 12);
        var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        var y = canvas.YValue(rect.Top) + fontSize + 2;

        foreach (var line in lines.Take(Math.Max(1, (int)(rect.Height / (fontSize * 1.2)))))
        {
            builder.Append("<text x=\"").Append(Number(canvas.XValue(rect.Left) + 3)).Append("\" y=\"").Append(Number(y))
                .Append("\" font-family=\"").Append(SansFamily).Append("\" font-size=\"").Append(Number(fontSize))
                .Append("\" font-weight=\"bold\" fill=\"").Append(color).Append("\" xml:space=\"preserve\">");
            AppendEscaped(builder, line);
            builder.Append("</text>");

            y += fontSize * 1.2;
        }
    }

    private static void AppendFormValues(StringBuilder builder, AcroForm form, int pageNumber, Canvas canvas)
    {
        if (form is null)
        {
            return;
        }

        IEnumerable<AcroFieldBase> fields;

        try
        {
            fields = form.Fields ?? [];
        }
        catch (Exception)
        {
            return;
        }

        foreach (var field in Flatten(fields))
        {
            if (field.PageNumber != pageNumber || field.Bounds is not { } bounds || bounds.Width < 1 || bounds.Height < 1)
            {
                continue;
            }

            switch (field)
            {
                case AcroTextField text when !string.IsNullOrEmpty(text.Value):
                    AppendFieldText(builder, canvas, bounds, text.Value, text.IsMultiline);

                    break;

                case AcroComboBoxField combo when combo.SelectedOptions is { Count: > 0 }:
                    AppendFieldText(builder, canvas, bounds, combo.SelectedOptions[0], multiline: false);

                    break;

                case AcroListBoxField list when list.SelectedOptions is { Count: > 0 }:
                    AppendFieldText(builder, canvas, bounds, string.Join(", ", list.SelectedOptions), multiline: true);

                    break;

                case AcroCheckboxField checkbox when checkbox.IsChecked:
                    AppendCheck(builder, canvas, bounds);

                    break;

                case AcroRadioButtonField radio when radio.IsSelected:
                    builder.Append("<circle cx=\"").Append(Number(canvas.XValue(bounds.Left) + (bounds.Width / 2)))
                        .Append("\" cy=\"").Append(Number(canvas.YValue(bounds.Top) + (bounds.Height / 2)))
                        .Append("\" r=\"").Append(Number(Math.Min(bounds.Width, bounds.Height) * 0.28))
                        .Append("\" fill=\"#1f2933\"/>");

                    break;
            }
        }
    }

    private static IEnumerable<AcroFieldBase> Flatten(IEnumerable<AcroFieldBase> fields)
    {
        foreach (var field in fields)
        {
            if (field is AcroNonTerminalField parent && parent.Children is { Count: > 0 })
            {
                foreach (var child in Flatten(parent.Children))
                {
                    yield return child;
                }

                continue;
            }

            yield return field;
        }
    }

    private static void AppendFieldText(StringBuilder builder, Canvas canvas, PdfRectangle bounds, string value, bool multiline)
    {
        var fontSize = Math.Clamp(bounds.Height * (multiline ? 0.3 : 0.62), 5, 11);
        var lines = multiline
            ? value.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n')
            : [value.Replace('\n', ' ').Replace("\r", string.Empty, StringComparison.Ordinal)];

        var y = multiline
            ? canvas.YValue(bounds.Top) + fontSize + 1.5
            : canvas.YValue(bounds.Top) + (bounds.Height / 2) + (fontSize * 0.35);

        foreach (var line in lines.Take(Math.Max(1, (int)(bounds.Height / (fontSize * 1.15)))))
        {
            builder.Append("<text x=\"").Append(Number(canvas.XValue(bounds.Left) + 2)).Append("\" y=\"").Append(Number(y))
                .Append("\" font-family=\"").Append(SansFamily).Append("\" font-size=\"").Append(Number(fontSize))
                .Append("\" fill=\"#0b2545\" xml:space=\"preserve\">");
            AppendEscaped(builder, line);
            builder.Append("</text>");

            y += fontSize * 1.15;
        }
    }

    private static void AppendCheck(StringBuilder builder, Canvas canvas, PdfRectangle bounds)
    {
        var left = canvas.XValue(bounds.Left);
        var top = canvas.YValue(bounds.Top);
        var width = bounds.Width;
        var height = bounds.Height;

        builder.Append("<path d=\"M").Append(Number(left + (width * 0.2))).Append(' ').Append(Number(top + (height * 0.55)))
            .Append('L').Append(Number(left + (width * 0.42))).Append(' ').Append(Number(top + (height * 0.78)))
            .Append('L').Append(Number(left + (width * 0.82))).Append(' ').Append(Number(top + (height * 0.24)))
            .Append("\" fill=\"none\" stroke=\"#1f2933\" stroke-width=\"").Append(Number(Math.Max(Math.Min(width, height) * 0.12, 0.8)))
            .Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
    }

    private static string ReadAnnotationColor(PdfDocument document, DictionaryToken dictionary, string key)
    {
        var components = PdfPigTokens.GetNumbers(document, dictionary, key);

        return components.Count switch
        {
            1 => Hex(components[0], components[0], components[0]),
            3 => Hex(components[0], components[1], components[2]),
            4 => Hex(
                (1 - components[0]) * (1 - components[3]),
                (1 - components[1]) * (1 - components[3]),
                (1 - components[2]) * (1 - components[3])),
            _ => null,
        };
    }

    private static bool IsInvisible(Letter letter)
    {
        return letter.RenderingMode is TextRenderingMode.Neither or TextRenderingMode.NeitherClip;
    }

    private static bool SameBaseline(Letter candidate, Letter previous)
    {
        if (candidate.TextOrientation == TextOrientation.Horizontal)
        {
            return Math.Abs(candidate.StartBaseLine.Y - previous.StartBaseLine.Y) < Math.Max(0.5, previous.PointSize * 0.08);
        }

        // Rotated text shares a baseline when the new letter starts where the last one ended.
        return Math.Abs(candidate.StartBaseLine.X - previous.EndBaseLine.X) < previous.PointSize * 0.8 &&
            Math.Abs(candidate.StartBaseLine.Y - previous.EndBaseLine.Y) < previous.PointSize * 0.8;
    }

    private static double Gap(Letter previous, Letter next)
    {
        if (previous.TextOrientation == TextOrientation.Horizontal)
        {
            return next.StartBaseLine.X - previous.EndBaseLine.X;
        }

        return Math.Sqrt(Math.Pow(next.StartBaseLine.X - previous.EndBaseLine.X, 2) + Math.Pow(next.StartBaseLine.Y - previous.EndBaseLine.Y, 2));
    }

    private static string FamilyFor(string fontName)
    {
        if (fontName.Contains("Courier", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Consol", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Code", StringComparison.OrdinalIgnoreCase))
        {
            return MonospaceFamily;
        }

        if (fontName.Contains("Times", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Serif", StringComparison.OrdinalIgnoreCase) && !fontName.Contains("Sans", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Georgia", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Garamond", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Cambria", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Palatino", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Book", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Minion", StringComparison.OrdinalIgnoreCase))
        {
            return SerifFamily;
        }

        return SansFamily;
    }

    private static string ToHex(IColor color)
    {
        if (color is null)
        {
            return "#000000";
        }

        try
        {
            var (red, green, blue) = color.ToRGBValues();

            return Hex(red, green, blue);
        }
        catch (Exception)
        {
            // A pattern or an unsupported colour space has no single colour; grey is the honest stand-in.
            return "#808080";
        }
    }

    private static string Hex(double red, double green, double blue)
    {
        static int Channel(double value)
        {
            return (int)Math.Round(Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 1) * 255);
        }

        return string.Create(CultureInfo.InvariantCulture, $"#{Channel(red):X2}{Channel(green):X2}{Channel(blue):X2}");
    }

    private static string Number(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "0";
        }

        return Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Escapes the characters that would otherwise close an element early, and drops the ones XML cannot hold.
    /// </summary>
    private static void AppendEscaped(StringBuilder builder, string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

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
                    if (char.IsHighSurrogate(character))
                    {
                        // A surrogate pair is kept whole; an unpaired half is not legal XML and is dropped.
                        if (index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                        {
                            builder.Append(character).Append(value[index + 1]);
                            index++;
                        }
                    }
                    else if (character >= ' ' && !char.IsLowSurrogate(character) && character is not '￾' and not '￿')
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Maps PDF user space, whose origin is the bottom-left of the page box, onto SVG's top-left origin.
    /// </summary>
    private readonly record struct Canvas(double Left, double Bottom, double Width, double Height)
    {
        public double XValue(double x)
        {
            return x - Left;
        }

        public double YValue(double y)
        {
            return Bottom + Height - y;
        }

        public string X(double x)
        {
            return Number(XValue(x));
        }

        public string Y(double y)
        {
            return Number(YValue(y));
        }
    }
}
