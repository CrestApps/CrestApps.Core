using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Graphics;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Images;
using CrestApps.Core.AI.Documents.Pdf.Objects;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Rendering;
using Microsoft.Extensions.Logging;

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
    private static readonly PdfColor _black = new(0, 0, 0);

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

        using var document = new PdfDocument();
        document.Info.Title = title ?? "Presentation";
        document.Info.Creator = "CrestApps";
        document.Info.Producer = "CrestApps";

        var fonts = new FontCache(_logger);

        foreach (var slide in slides)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = document.AddPage();
            page.MediaBox = new PdfRectangle(0, 0, Math.Max(1, slide.Width), Math.Max(1, slide.Height));

            var canvas = PdfCanvas.ForPage(page);

            foreach (var item in slide.Items)
            {
                canvas.Save();

                try
                {
                    Transform(canvas, item);

                    switch (item)
                    {
                        case SlideShapeDrawing shape:
                            DrawShape(canvas, shape);
                            break;
                        case SlideTextDrawing text:
                            DrawText(canvas, text, fonts);
                            break;
                        case SlideImageDrawing image:
                            DrawImage(canvas, image);
                            break;
                    }
                }
                finally
                {
                    canvas.Restore();
                }
            }

            canvas.Commit();
        }

        return Task.FromResult(document.Save());
    }

    private static void Transform(PdfCanvas canvas, SlideDrawingItem item)
    {
        if (!item.IsTransformed)
        {
            return;
        }

        if (Math.Abs(item.Rotation % 360) > 0.01)
        {
            canvas.RotateAt(item.Rotation, item.CenterX, item.CenterY);
        }

        if (item.FlipHorizontal || item.FlipVertical)
        {
            canvas.Translate(item.CenterX, item.CenterY);
            canvas.Scale(item.FlipHorizontal ? -1 : 1, item.FlipVertical ? -1 : 1);
            canvas.Translate(-item.CenterX, -item.CenterY);
        }
    }

    private static void DrawShape(PdfCanvas canvas, SlideShapeDrawing shape)
    {
        var (x, y, width, height) = shape.Box;
        var pen = Pen(shape.Stroke);

        if (shape.Fill?.Image is { Data.Length: > 0 } picture && PdfImage.TryLoad(picture.Data) is { } image)
        {
            canvas.Save();

            foreach (var path in shape.Paths.Where(path => path.Filled))
            {
                canvas.Clip(Path(path));
            }

            var fullWidth = width / Math.Max(0.01, 1 - picture.CropLeft - picture.CropRight);
            var fullHeight = height / Math.Max(0.01, 1 - picture.CropTop - picture.CropBottom);
            canvas.DrawImage(image, x - (picture.CropLeft * fullWidth), y - (picture.CropTop * fullHeight), fullWidth, fullHeight);
            canvas.Restore();

            if (pen is not null)
            {
                foreach (var path in shape.Paths.Where(path => path.Stroked))
                {
                    canvas.DrawPath(Path(path), null, pen);
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

            if (shape.Shadow && brush is not null)
            {
                // A soft shadow cannot be blurred in a PDF; a faint offset copy reads the same at slide size.
                canvas.Save();
                canvas.Translate(0, 2);
                canvas.DrawPath(Path(path), new PdfBrush(_black, 46 / 255d));
                canvas.Restore();
            }

            canvas.DrawPath(Path(path), brush, stroke);
        }
    }

    private static void DrawText(PdfCanvas canvas, SlideTextDrawing text, FontCache fonts)
    {
        foreach (var line in text.Lines)
        {
            if (line.Marker is { } marker && !string.IsNullOrEmpty(marker.Text))
            {
                var markerFont = fonts.Get(marker);

                DrawRun(canvas, marker, markerFont, line.MarkerX, line.Baseline, markerFont.Measure(marker.Text, Size(marker)));
            }

            if (line.Runs.Count == 0)
            {
                continue;
            }

            var widths = line.Runs.Select(run => string.IsNullOrEmpty(run.Text) ? 0 : fonts.Get(run).Measure(run.Text, Size(run))).ToList();
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
                        canvas.DrawRectangle(left, line.Baseline - (run.Size * 0.85), widths[index], run.Size * 1.1, Solid(run.Highlight, 1));
                    }

                    DrawRun(canvas, run, fonts.Get(run), left, line.Baseline, widths[index]);
                }

                left += widths[index];
            }
        }
    }

    private static void DrawRun(PdfCanvas canvas, SlideTextRun run, PdfFont font, double x, double baseline, double width)
    {
        var size = Size(run);
        var shifted = baseline + Shift(run);
        var brush = Solid(run.Color, 1);

        canvas.DrawText(run.Text, font, size, x, shifted, brush);

        // The lines are as thick as a typical font's own and sit where its underline and strikeout do.
        var pen = new PdfPen(brush.Color, Math.Max(0.5, size * 0.05));

        if (run.Underline)
        {
            canvas.DrawLine(x, shifted + (size * 0.1), x + width, shifted + (size * 0.1), pen);
        }

        if (run.Strikethrough)
        {
            canvas.DrawLine(x, shifted - (size * 0.28), x + width, shifted - (size * 0.28), pen);
        }
    }

    private static void DrawImage(PdfCanvas canvas, SlideImageDrawing drawing)
    {
        if (drawing.Data is not { Length: > 0 } || PdfImage.TryLoad(drawing.Data) is not { } image)
        {
            return;
        }

        if (drawing.Clip is { Count: > 0 })
        {
            foreach (var path in drawing.Clip)
            {
                canvas.Clip(Path(path));
            }
        }
        else
        {
            canvas.Clip(new PdfPath().AddRectangle(drawing.X, drawing.Y, drawing.Width, drawing.Height));
        }

        // A crop keeps the whole picture and shows part of it: the picture is drawn larger than its frame
        // and the frame cuts it.
        var fullWidth = drawing.Width / Math.Max(0.01, 1 - drawing.CropLeft - drawing.CropRight);
        var fullHeight = drawing.Height / Math.Max(0.01, 1 - drawing.CropTop - drawing.CropBottom);

        canvas.DrawImage(image, drawing.X - (drawing.CropLeft * fullWidth), drawing.Y - (drawing.CropTop * fullHeight), fullWidth, fullHeight);
    }

    private static PdfPath Path(SlidePath path)
    {
        var result = new PdfPath { EvenOdd = path.EvenOdd };

        foreach (var command in path.Commands)
        {
            switch (command.Kind)
            {
                case 'M':
                    result.MoveTo(command.Points[0].X, command.Points[0].Y);
                    break;

                case 'L':
                    result.LineTo(command.Points[0].X, command.Points[0].Y);
                    break;

                case 'C':
                    result.CurveTo(command.Points[0].X, command.Points[0].Y, command.Points[1].X, command.Points[1].Y, command.Points[2].X, command.Points[2].Y);
                    break;

                case 'Z':
                    result.Close();
                    break;
            }
        }

        return result;
    }

    private static PdfBrush Brush(SlidePaint paint, double shade, double x, double y, double width, double height)
    {
        if (paint.Stops.Count >= 2)
        {
            var first = paint.Stops[0];
            var last = paint.Stops[^1];

            // A PDF shading has one opacity; the more opaque end's is used, so a gradient fading to clear still shows.
            var opacity = Math.Clamp(Math.Max(first.Alpha, last.Alpha), 0, 1);

            if (paint.Radial)
            {
                return PdfBrush.Radial(Color(first.Color), Color(last.Color), x + (width / 2), y + (height / 2), Math.Max(width, height) * 0.7, opacity);
            }

            // DrawingML measures the angle clockwise from left-to-right, across the shape's bounding box.
            var radians = paint.Angle * Math.PI / 180;
            var dx = Math.Cos(radians) * width / 2;
            var dy = Math.Sin(radians) * height / 2;
            var middleX = x + (width / 2);
            var middleY = y + (height / 2);

            return PdfBrush.Linear(Color(first.Color), Color(last.Color), middleX - dx, middleY - dy, middleX + dx, middleY + dy, opacity);
        }

        var color = paint.Stops.Count == 1 ? paint.Stops[0].Color : paint.Color;

        if (string.IsNullOrEmpty(color))
        {
            return null;
        }

        return Solid(shade != 0 ? PresentationColor.Shade(color, -shade) : color, paint.Alpha);
    }

    private static PdfPen Pen(SlideStroke stroke)
    {
        if (stroke is null || stroke.Width <= 0)
        {
            return null;
        }

        double[] pattern = stroke.Dash switch
        {
            "dash" => [4, 3],
            "dot" => [1, 2],
            "dash_dot" => [4, 2, 1, 2],
            "long_dash" => [8, 3],
            "long_dash_dot" => [8, 3, 1, 3],
            _ => null,
        };

        return new PdfPen(Color(stroke.Color), stroke.Width, Math.Clamp(stroke.Alpha, 0, 1), pattern, RoundJoins: true);
    }

    private static PdfBrush Solid(string hex, double alpha)
    {
        return new PdfBrush(Color(hex), Math.Clamp(alpha, 0, 1));
    }

    private static PdfColor Color(string hex)
    {
        var normalized = PresentationColor.NormalizeHex(hex) ?? "000000";
        var value = Convert.ToInt32(normalized, 16);

        return new PdfColor((byte)((value >> 16) & 0xFF), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));
    }

    private static double Size(SlideTextRun run)
    {
        return Math.Round(Math.Max(1, run.Size), 2);
    }

    private static double Shift(SlideTextRun run)
    {
        // A positive baseline percentage raises the text, as superscript does; PDF y grows downwards.
        return run.Baseline == 0 ? 0 : -(run.Baseline / 100) * run.Size;
    }

    /// <summary>
    /// Resolves each font a deck names once, falling back to a common sans-serif typeface when the deck
    /// names one the host does not have.
    /// </summary>
    private sealed class FontCache
    {
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
        public PdfFont Get(SlideTextRun run)
        {
            var (family, bold) = Resolve(run.Font);

            return PdfFontLibrary.Get(family, run.Bold || bold, run.Italic);
        }

        private (string Family, bool Bold) Resolve(string requested)
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
                if (PdfFontLibrary.IsInstalled(candidate.Family))
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
    }
}
