using CrestApps.Core.AI.Documents.Pdf.Services;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Rendering;
using Microsoft.Extensions.Logging;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Presentations;

/// <summary>
/// Draws slides into a PDF, one page per slide at the slide's own size, by painting the same display lists
/// the slide preview paints, so an exported PDF looks like the preview and like the deck in PowerPoint.
/// </summary>
/// <remarks>
/// Text keeps the line breaks the shared layout chose; only each run's position along its line is measured
/// again with the embedded font, so runs never overlap when the PDF font is wider than the estimate.
/// Gradients use their first and last colours, which is what a PDF gradient brush can hold.
/// </remarks>
internal sealed class SlidePdfRenderer : IPresentationPdfRenderer
{
    private static readonly string[] _fallbackFonts = ["Arial", "Segoe UI", "Helvetica", "Liberation Sans", "DejaVu Sans"];
    private static readonly (string Suffix, bool Bold)[] _weightSuffixes = [(" Semibold", true), (" SemiBold", true), (" Bold", true), (" Black", true), (" Light", false), (" Semilight", false), (" SemiLight", false)];
    private static int _fontsConfigured;

    private readonly ILogger<SlidePdfRenderer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SlidePdfRenderer"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public SlidePdfRenderer(ILogger<SlidePdfRenderer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Renders slides as a PDF.
    /// </summary>
    /// <param name="slides">The slides, in order.</param>
    /// <param name="title">The document title.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The PDF.</returns>
    public Task<byte[]> RenderAsync(IReadOnlyList<SlideDrawing> slides, string title, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slides);

        EnsureFonts();

        using var document = new PdfDocument();
        document.Info.Title = title ?? "Presentation";
        document.Info.Creator = "CrestApps";

        var fonts = new FontCache(_logger);

        foreach (var slide in slides)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = document.AddPage();
            page.Width = XUnit.FromPoint(Math.Max(1, slide.Width));
            page.Height = XUnit.FromPoint(Math.Max(1, slide.Height));

            using var graphics = XGraphics.FromPdfPage(page);

            foreach (var item in slide.Items)
            {
                var state = graphics.Save();

                try
                {
                    Transform(graphics, item);

                    switch (item)
                    {
                        case SlideShapeDrawing shape:
                            DrawShape(graphics, shape);
                            break;
                        case SlideTextDrawing text:
                            DrawText(graphics, text, fonts);
                            break;
                        case SlideImageDrawing image:
                            DrawImage(graphics, image);
                            break;
                    }
                }
                finally
                {
                    graphics.Restore(state);
                }
            }
        }

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);

        return Task.FromResult(output.ToArray());
    }

    private static void EnsureFonts()
    {
        if (Interlocked.Exchange(ref _fontsConfigured, 1) != 0)
        {
            return;
        }

        try
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            GlobalFontSettings.UseWindowsFontsUnderWsl2 = true;

            if (!OperatingSystem.IsWindows() && GlobalFontSettings.FontResolver is null && SystemFontResolver.TryCreate(out var resolver))
            {
                GlobalFontSettings.FontResolver = resolver;
            }
        }
        catch (InvalidOperationException)
        {
            // The settings can only be changed before the first font is made; another writer already did.
        }
    }

    private static void Transform(XGraphics graphics, SlideDrawingItem item)
    {
        if (!item.IsTransformed)
        {
            return;
        }

        var center = new XPoint(item.CenterX, item.CenterY);

        if (Math.Abs(item.Rotation % 360) > 0.01)
        {
            graphics.RotateAtTransform(item.Rotation, center);
        }

        if (item.FlipHorizontal || item.FlipVertical)
        {
            graphics.ScaleAtTransform(item.FlipHorizontal ? -1 : 1, item.FlipVertical ? -1 : 1, center);
        }
    }

    private static void DrawShape(XGraphics graphics, SlideShapeDrawing shape)
    {
        var (x, y, width, height) = shape.Box;
        var pen = Pen(shape.Stroke);

        if (shape.Fill?.Image is { Data.Length: > 0 } picture && TryLoad(picture.Data, out var image))
        {
            using (image)
            {
                var state = graphics.Save();

                foreach (var path in shape.Paths.Where(path => path.Filled))
                {
                    graphics.IntersectClip(Path(path));
                }

                var fullWidth = width / Math.Max(0.01, 1 - picture.CropLeft - picture.CropRight);
                var fullHeight = height / Math.Max(0.01, 1 - picture.CropTop - picture.CropBottom);
                graphics.DrawImage(image, x - (picture.CropLeft * fullWidth), y - (picture.CropTop * fullHeight), fullWidth, fullHeight);
                graphics.Restore(state);
            }

            if (pen is not null)
            {
                foreach (var path in shape.Paths.Where(path => path.Stroked))
                {
                    graphics.DrawPath(pen, Path(path));
                }
            }

            return;
        }

        foreach (var path in shape.Paths)
        {
            var brush = path.Filled && shape.Fill is { } fill ? Brush(fill, path.Shade, x, y, width, height) : null;
            var stroke = path.Stroked ? pen : null;

            if (brush is null && stroke is null)
            {
                continue;
            }

            var geometry = Path(path);

            if (shape.Shadow && brush is not null)
            {
                // A soft shadow cannot be blurred in a PDF; a faint offset copy reads the same at slide size.
                var state = graphics.Save();
                graphics.TranslateTransform(0, 2);
                graphics.DrawPath(new XSolidBrush(XColor.FromArgb(46, 0, 0, 0)), Path(path));
                graphics.Restore(state);
            }

            if (brush is not null && stroke is not null)
            {
                graphics.DrawPath(stroke, brush, geometry);
            }
            else if (brush is not null)
            {
                graphics.DrawPath(brush, geometry);
            }
            else
            {
                graphics.DrawPath(stroke, geometry);
            }
        }
    }

    private static void DrawText(XGraphics graphics, SlideTextDrawing text, FontCache fonts)
    {
        foreach (var line in text.Lines)
        {
            if (line.Marker is { } marker && !string.IsNullOrEmpty(marker.Text))
            {
                graphics.DrawString(marker.Text, fonts.Get(marker), Solid(marker.Color, 1), line.MarkerX, line.Baseline + Shift(marker), XStringFormats.BaseLineLeft);
            }

            if (line.Runs.Count == 0)
            {
                continue;
            }

            var widths = line.Runs.Select(run => string.IsNullOrEmpty(run.Text) ? 0 : graphics.MeasureString(run.Text, fonts.Get(run)).Width).ToList();
            var total = widths.Sum();
            var left = line.Anchor switch
            {
                "middle" => line.X - (total / 2),
                "end" => line.X - total,
                _ => line.X,
            };

            for (var index = 0; index < line.Runs.Count; index++)
            {
                var run = line.Runs[index];

                if (!string.IsNullOrEmpty(run.Text))
                {
                    if (!string.IsNullOrEmpty(run.Highlight))
                    {
                        graphics.DrawRectangle(Solid(run.Highlight, 1), left, line.Baseline - (run.Size * 0.85), widths[index], run.Size * 1.1);
                    }

                    graphics.DrawString(run.Text, fonts.Get(run), Solid(run.Color, 1), left, line.Baseline + Shift(run), XStringFormats.BaseLineLeft);
                }

                left += widths[index];
            }
        }
    }

    private static void DrawImage(XGraphics graphics, SlideImageDrawing drawing)
    {
        if (drawing.Data is not { Length: > 0 } || !TryLoad(drawing.Data, out var image))
        {
            return;
        }

        using (image)
        {
            if (drawing.Clip is { Count: > 0 })
            {
                foreach (var path in drawing.Clip)
                {
                    graphics.IntersectClip(Path(path));
                }
            }
            else
            {
                graphics.IntersectClip(new XRect(drawing.X, drawing.Y, drawing.Width, drawing.Height));
            }

            // A crop keeps the whole picture and shows part of it: the picture is drawn larger than its frame
            // and the frame cuts it.
            var fullWidth = drawing.Width / Math.Max(0.01, 1 - drawing.CropLeft - drawing.CropRight);
            var fullHeight = drawing.Height / Math.Max(0.01, 1 - drawing.CropTop - drawing.CropBottom);

            graphics.DrawImage(image, drawing.X - (drawing.CropLeft * fullWidth), drawing.Y - (drawing.CropTop * fullHeight), fullWidth, fullHeight);
        }
    }

    private static bool TryLoad(byte[] data, out XImage image)
    {
        image = null;

        try
        {
            // The stream stays open for the image's lifetime, which ends with the page drawing it.
            image = XImage.FromStream(new MemoryStream(data, writable: false));

            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException or IOException)
        {
            return false;
        }
    }

    private static XGraphicsPath Path(SlidePath path)
    {
        var result = new XGraphicsPath { FillMode = path.EvenOdd ? XFillMode.Alternate : XFillMode.Winding };
        var current = (X: 0d, Y: 0d);
        var start = current;
        var open = false;

        foreach (var command in path.Commands)
        {
            switch (command.Kind)
            {
                case 'M':
                    if (open)
                    {
                        result.StartFigure();
                    }

                    current = command.Points[0];
                    start = current;
                    open = true;
                    break;

                case 'L':
                    result.AddLine(current.X, current.Y, command.Points[0].X, command.Points[0].Y);
                    current = command.Points[0];
                    break;

                case 'C':
                    result.AddBezier(current.X, current.Y, command.Points[0].X, command.Points[0].Y, command.Points[1].X, command.Points[1].Y, command.Points[2].X, command.Points[2].Y);
                    current = command.Points[2];
                    break;

                case 'Z':
                    result.CloseFigure();
                    current = start;
                    open = false;
                    break;
            }
        }

        return result;
    }

    private static XBrush Brush(SlidePaint paint, double shade, double x, double y, double width, double height)
    {
        if (paint.Stops.Count >= 2)
        {
            var first = paint.Stops[0];
            var last = paint.Stops[^1];
            var from = Color(first.Color, first.Alpha);
            var to = Color(last.Color, last.Alpha);

            if (paint.Radial)
            {
                var center = new XPoint(x + (width / 2), y + (height / 2));

                return new XRadialGradientBrush(center, center, 0, Math.Max(width, height) * 0.7, from, to);
            }

            // DrawingML measures the angle clockwise from left-to-right, across the shape's bounding box.
            var radians = paint.Angle * Math.PI / 180;
            var dx = Math.Cos(radians) * width / 2;
            var dy = Math.Sin(radians) * height / 2;
            var middle = new XPoint(x + (width / 2), y + (height / 2));

            return new XLinearGradientBrush(new XPoint(middle.X - dx, middle.Y - dy), new XPoint(middle.X + dx, middle.Y + dy), from, to);
        }

        var color = paint.Stops.Count == 1 ? paint.Stops[0].Color : paint.Color;

        if (string.IsNullOrEmpty(color))
        {
            return null;
        }

        return Solid(shade != 0 ? PresentationColor.Shade(color, -shade) : color, paint.Alpha);
    }

    private static XPen Pen(SlideStroke stroke)
    {
        if (stroke is null || stroke.Width <= 0)
        {
            return null;
        }

        var pen = new XPen(Color(stroke.Color, stroke.Alpha), stroke.Width)
        {
            LineJoin = XLineJoin.Round,
        };

        var width = stroke.Width;
        double[] pattern = stroke.Dash switch
        {
            "dash" => [4, 3],
            "dot" => [1, 2],
            "dash_dot" => [4, 2, 1, 2],
            "long_dash" => [8, 3],
            "long_dash_dot" => [8, 3, 1, 3],
            _ => null,
        };

        if (pattern is not null && width > 0)
        {
            pen.DashPattern = pattern;
        }

        return pen;
    }

    private static XSolidBrush Solid(string hex, double alpha)
    {
        return new XSolidBrush(Color(hex, alpha));
    }

    private static XColor Color(string hex, double alpha)
    {
        var normalized = PresentationColor.NormalizeHex(hex) ?? "000000";
        var value = Convert.ToInt32(normalized, 16);

        return XColor.FromArgb((int)Math.Round(Math.Clamp(alpha, 0, 1) * 255), (value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
    }

    private static double Shift(SlideTextRun run)
    {
        // A positive baseline percentage raises the text, as superscript does; PDF y grows downwards.
        return run.Baseline == 0 ? 0 : -(run.Baseline / 100) * run.Size;
    }

    /// <summary>
    /// Makes each font once, falling back to a common sans-serif typeface when the deck names one the host
    /// does not have.
    /// </summary>
    private sealed class FontCache
    {
        private readonly Dictionary<(string Family, double Size, XFontStyleEx Style), XFont> _fonts = [];
        private readonly Dictionary<string, (string Family, bool Bold)> _resolved = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="FontCache"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public FontCache(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Gets the font a run is drawn in.
        /// </summary>
        /// <param name="run">The run.</param>
        /// <returns>The font.</returns>
        public XFont Get(SlideTextRun run)
        {
            var style = XFontStyleEx.Regular;

            if (run.Bold)
            {
                style |= XFontStyleEx.Bold;
            }

            if (run.Italic)
            {
                style |= XFontStyleEx.Italic;
            }

            if (run.Underline)
            {
                style |= XFontStyleEx.Underline;
            }

            if (run.Strikethrough)
            {
                style |= XFontStyleEx.Strikeout;
            }

            var size = Math.Round(Math.Max(1, run.Size), 2);
            var (family, bold) = Resolve(run.Font, style);

            if (bold)
            {
                style |= XFontStyleEx.Bold;
            }

            var key = (family, size, style);

            if (!_fonts.TryGetValue(key, out var font))
            {
                font = new XFont(family, size, style);
                _fonts[key] = font;
            }

            return font;
        }

        private (string Family, bool Bold) Resolve(string requested, XFontStyleEx style)
        {
            var name = string.IsNullOrWhiteSpace(requested) ? _fallbackFonts[0] : requested.Trim();

            if (_resolved.TryGetValue(name, out var known))
            {
                return known;
            }

            // A face named with its weight, such as "Segoe UI Semibold", is its family drawn in that weight
            // when the host only knows the family.
            var candidates = new List<(string Family, bool Bold)> { (name, false) };

            foreach (var (suffix, bold) in _weightSuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
                {
                    candidates.Add((name[..^suffix.Length], bold));
                    break;
                }
            }

            candidates.AddRange(_fallbackFonts.Select(fallback => (fallback, false)));

            foreach (var candidate in candidates)
            {
                if (Exists(candidate.Family, style))
                {
                    if (!string.Equals(candidate.Family, name, StringComparison.OrdinalIgnoreCase) && _logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("The slide font '{Font}' is not available for PDF export; '{Fallback}' is used instead.", name, candidate.Family);
                    }

                    _resolved[name] = candidate;

                    return candidate;
                }
            }

            _resolved[name] = (name, false);

            return (name, false);
        }

        private static bool Exists(string family, XFontStyleEx style)
        {
            try
            {
                _ = new XFont(family, 10, style & (XFontStyleEx.Bold | XFontStyleEx.Italic));

                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                return false;
            }
        }
    }
}
