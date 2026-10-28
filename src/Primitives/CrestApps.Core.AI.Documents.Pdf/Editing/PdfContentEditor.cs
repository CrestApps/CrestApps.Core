using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Graphics;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Images;
using CrestApps.Core.AI.Documents.Pdf.Objects;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Changes what existing pages show: replaces text where it stands, writes new text and pictures onto a
/// page, and removes or covers areas.
/// </summary>
/// <remarks>
/// A replacement removes the old glyphs from the content — so the old words can no longer be selected or
/// extracted — and sets the new text on the same baseline, at the same size and in the same colour. The
/// original font usually cannot be reused (a document embeds only the glyphs it uses), so the new text is set
/// in the closest standard family; the answer says when the new text is wider than the old.
/// </remarks>
internal static class PdfContentEditor
{
    /// <summary>
    /// Replaces text.
    /// </summary>
    /// <param name="bytes">The PDF.</param>
    /// <param name="password">The password, when the file is protected.</param>
    /// <param name="operation">The replacement.</param>
    /// <param name="warnings">Receives notes about replacements that may not fit.</param>
    /// <returns>The edited PDF and how many occurrences were replaced.</returns>
    public static (byte[] Bytes, int Replaced) ReplaceText(byte[] bytes, string password, PdfContentEditOperation operation, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (string.IsNullOrEmpty(operation.Find))
        {
            throw new PdfToolException("replace_text needs 'find'.");
        }

        var targets = new List<(int Page, PdfTextMatch Match)>();

        using (var pdf = PdfFiles.OpenForReading(bytes, password))
        {
            var count = 0;

            foreach (var number in PdfPageRangeParser(operation.Pages, pdf.NumberOfPages))
            {
                foreach (var match in PdfTextFinder.Find(pdf.GetPage(number), operation.Find, isRegex: false, operation.MatchCase == true, operation.WholeWord == true, 500))
                {
                    count++;

                    if (operation.Occurrence is null || operation.Occurrence == count)
                    {
                        targets.Add((number, match));
                    }
                }
            }
        }

        if (targets.Count == 0)
        {
            return (bytes, 0);
        }

        using var document = PdfFiles.OpenForEditing(bytes, password);

        foreach (var group in targets.GroupBy(target => target.Page))
        {
            var page = document.Pages[group.Key - 1];
            var areas = group.SelectMany(target => target.Match.Boxes.Select(box => box.Inflate(0.5))).ToList();

            PdfContentRewriter.Rewrite(page, new PdfContentRewriteOptions { Areas = areas });

            var canvas = PdfCanvas.ForPage(page);

            foreach (var (_, match) in group)
            {
                DrawReplacement(canvas, page, match, operation, warnings);
            }

            canvas.Commit();
        }

        return (PdfFiles.Save(document), targets.Count);
    }

    /// <summary>
    /// Writes text onto a page.
    /// </summary>
    /// <param name="bytes">The PDF.</param>
    /// <param name="password">The password, when the file is protected.</param>
    /// <param name="operation">The text and where it goes.</param>
    /// <returns>The edited PDF.</returns>
    public static byte[] AddText(byte[] bytes, string password, PdfContentEditOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (string.IsNullOrEmpty(operation.Text))
        {
            throw new PdfToolException("add_text needs 'text'.");
        }

        using var document = PdfFiles.OpenForEditing(bytes, password);

        var page = PageOf(document, operation.Page);
        var canvas = PdfCanvas.ForPage(page);
        var font = Font(operation.FontFamily, operation.Bold == true, operation.Italic == true);
        var size = Size(operation.FontSize ?? 11);
        var brush = new PdfBrush(PdfColor.Parse(operation.Color, new PdfColor(0, 0, 0)));
        var (x, y) = ToGraphics(page, operation.X ?? 72, operation.Y ?? 72);
        var text = operation.Text.Replace("\r", string.Empty, StringComparison.Ordinal);

        if (operation.Width is > 0)
        {
            var height = operation.Height ?? Math.Max(size * 1.3, page.Height - y);

            DrawWrapped(canvas, text, font, size, brush, x, y, operation.Width.Value, height, operation.Align?.Trim().ToLowerInvariant());
        }
        else
        {
            var lineHeight = size * 1.2;
            var lines = text.Split('\n');

            for (var index = 0; index < lines.Length; index++)
            {
                canvas.DrawText(lines[index], font, size, x, y + (index * lineHeight) + (font.Ascent * size), brush);
            }
        }

        canvas.Commit();

        return PdfFiles.Save(document);
    }

    /// <summary>
    /// Places a picture on a page.
    /// </summary>
    /// <param name="bytes">The PDF.</param>
    /// <param name="password">The password, when the file is protected.</param>
    /// <param name="operation">Where the picture goes.</param>
    /// <param name="image">The picture.</param>
    /// <returns>The edited PDF.</returns>
    public static byte[] AddImage(byte[] bytes, string password, PdfContentEditOperation operation, PdfImageData image)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(image);

        if (!PdfImageInfo.TryRead(image.Bytes, out _, out var pixelWidth, out var pixelHeight) || PdfImage.TryLoad(image.Bytes) is not { } picture)
        {
            throw new PdfToolException("The image is not a JPEG, PNG, GIF or BMP.");
        }

        using var document = PdfFiles.OpenForEditing(bytes, password);

        var page = PageOf(document, operation.Page);
        var aspect = (double)pixelWidth / pixelHeight;
        var width = operation.Width ?? (operation.Height is > 0 ? operation.Height.Value * aspect : Math.Min(pixelWidth * 0.75, page.Width / 2));
        var height = operation.Height ?? (width / aspect);

        var canvas = PdfCanvas.ForPage(page);
        var (x, y) = ToGraphics(page, operation.X ?? 72, operation.Y ?? 72);

        canvas.DrawImage(picture, x, y, width, height);
        canvas.Commit();

        return PdfFiles.Save(document);
    }

    /// <summary>
    /// Removes the content of an area, and optionally fills it.
    /// </summary>
    /// <param name="bytes">The PDF.</param>
    /// <param name="password">The password, when the file is protected.</param>
    /// <param name="operation">The area.</param>
    /// <param name="removeContent">Whether the content under the area is removed; otherwise it is only covered.</param>
    /// <returns>The edited PDF and how many glyphs and images were removed.</returns>
    public static (byte[] Bytes, int Glyphs, int Images) RemoveArea(byte[] bytes, string password, PdfContentEditOperation operation, bool removeContent)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.Width is not > 0 || operation.Height is not > 0)
        {
            throw new PdfToolException("An area needs 'page', 'x', 'y', 'width' and 'height' in points from the top-left.");
        }

        PdfBox area;

        using (var pdf = PdfFiles.OpenForReading(bytes, password))
        {
            var number = operation.Page ?? 1;

            if (number < 1 || number > pdf.NumberOfPages)
            {
                throw new PdfToolException($"Page {number} does not exist; the document has {pdf.NumberOfPages} page(s).");
            }

            area = PdfBox.FromTopLeft(pdf.GetPage(number), operation.X ?? 0, operation.Y ?? 0, operation.Width.Value, operation.Height.Value);
        }

        using var document = PdfFiles.OpenForEditing(bytes, password);

        var page = PageOf(document, operation.Page);
        var glyphs = 0;
        var images = 0;

        if (removeContent)
        {
            var rewriter = PdfContentRewriter.Rewrite(page, new PdfContentRewriteOptions { Areas = [area] });

            glyphs = rewriter.GlyphsRemoved;
            images = rewriter.ImagesRemoved;
        }

        var fill = operation.Fill?.Trim().ToLowerInvariant();

        if (!removeContent || (!string.IsNullOrEmpty(fill) && fill != "none"))
        {
            var color = PdfColor.Parse(fill is null or "none" ? "white" : fill, new PdfColor(255, 255, 255));

            PdfLowLevel.AppendIsolated(page, string.Create(
                CultureInfo.InvariantCulture,
                $"q {PdfLowLevel.Format(color.Red / 255d)} {PdfLowLevel.Format(color.Green / 255d)} {PdfLowLevel.Format(color.Blue / 255d)} rg {PdfLowLevel.Format(area.Left)} {PdfLowLevel.Format(area.Bottom)} {PdfLowLevel.Format(area.Width)} {PdfLowLevel.Format(area.Height)} re f Q"));
        }

        return (PdfFiles.Save(document), glyphs, images);
    }

    private static void DrawReplacement(PdfCanvas canvas, PdfPage page, PdfTextMatch match, PdfContentEditOperation operation, List<string> warnings)
    {
        if (string.IsNullOrEmpty(operation.Replace) || match.Letters.Count == 0)
        {
            return;
        }

        var first = match.Letters[0];

        if (first.TextOrientation != TextOrientation.Horizontal)
        {
            warnings.Add($"\"{match.Text}\" on page {match.PageNumber} is not horizontal; it was removed but the replacement was not drawn.");

            return;
        }

        var fontName = first.FontName ?? string.Empty;
        var bold = operation.Bold ?? (first.FontDetails?.IsBold == true || fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase));
        var italic = operation.Italic ?? (first.FontDetails?.IsItalic == true || fontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) || fontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase));
        var family = operation.FontFamily ?? (fontName.Contains("Times", StringComparison.OrdinalIgnoreCase) || fontName.Contains("Serif", StringComparison.OrdinalIgnoreCase) && !fontName.Contains("Sans", StringComparison.OrdinalIgnoreCase)
            ? "Times New Roman"
            : fontName.Contains("Courier", StringComparison.OrdinalIgnoreCase) || fontName.Contains("Mono", StringComparison.OrdinalIgnoreCase)
                ? "Courier New"
                : PdfFontFamilies.Default);

        var size = Size(operation.FontSize ?? Math.Max(first.PointSize, 1));
        var font = Font(family, bold, italic);
        var color = ReadColor(first, operation.Color);
        var (x, y) = ToGraphics(page, first.StartBaseLine.X, first.StartBaseLine.Y, fromUserSpace: true);

        canvas.DrawText(operation.Replace, font, size, x, y, new PdfBrush(color));

        var oldWidth = match.Boxes.Sum(box => box.Width);
        var newWidth = font.Measure(operation.Replace, size);

        if (oldWidth > 0 && newWidth > oldWidth * 1.15)
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture, $"On page {match.PageNumber} \"{operation.Replace}\" is {Math.Round((newWidth / oldWidth - 1) * 100)}% wider than \"{match.Text}\" and may run into the text after it; preview the page."));
        }
    }

    private static PdfColor ReadColor(Letter letter, string requested)
    {
        if (PdfColor.TryParse(requested, out var color))
        {
            return color;
        }

        try
        {
            var (red, green, blue) = letter.FillColor?.ToRGBValues() ?? (0, 0, 0);

            return new PdfColor((byte)Math.Round(red * 255), (byte)Math.Round(green * 255), (byte)Math.Round(blue * 255));
        }
        catch (Exception)
        {
            return new PdfColor(0, 0, 0);
        }
    }

    private static PdfFont Font(string family, bool bold, bool italic)
    {
        return PdfFontLibrary.Get(PdfFontFamilies.Resolve(family, PdfFontFamilies.Default, null), bold, italic);
    }

    private static double Size(double size)
    {
        return Math.Clamp(size, 1, 200);
    }

    /// <summary>
    /// Writes text into a box, wrapping it at word boundaries and dropping the lines that do not fit its height.
    /// </summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="text">The text; a line break starts a new paragraph.</param>
    /// <param name="font">The font.</param>
    /// <param name="size">The font size.</param>
    /// <param name="brush">The colour.</param>
    /// <param name="left">The box's left edge.</param>
    /// <param name="top">The box's top edge.</param>
    /// <param name="width">The box's width.</param>
    /// <param name="height">The box's height.</param>
    /// <param name="align">left, center, right or justify.</param>
    private static void DrawWrapped(PdfCanvas canvas, string text, PdfFont font, double size, PdfBrush brush, double left, double top, double width, double height, string align)
    {
        var lineHeight = font.LineSpacing * size;
        var space = font.Measure(" ", size);
        var y = top;

        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var start = 0;

            do
            {
                if (y + lineHeight > top + height + 0.01)
                {
                    return;
                }

                // As many words as fit go on the line; a word wider than the box goes on a line of its own.
                var end = start;
                var used = 0d;

                while (end < words.Length)
                {
                    var next = used + (end > start ? space : 0) + font.Measure(words[end], size);

                    if (end > start && next > width)
                    {
                        break;
                    }

                    used = next;
                    end++;
                }

                var last = end >= words.Length;
                var gap = align == "justify" && !last && end - start > 1
                    ? space + ((width - used) / (end - start - 1))
                    : space;

                var x = align switch
                {
                    "center" => left + ((width - used) / 2),
                    "right" => left + width - used,
                    _ => left,
                };

                for (var index = start; index < end; index++)
                {
                    canvas.DrawText(words[index], font, size, x, y + (font.Ascent * size), brush);
                    x += font.Measure(words[index], size) + gap;
                }

                y += lineHeight;
                start = end;
            }
            while (start < words.Length);
        }
    }

    /// <summary>
    /// Converts a position to the coordinates a page is drawn in: points from the top-left of the media box.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="x">The x coordinate: points from the left of the visible area, or user space.</param>
    /// <param name="y">The y coordinate: points from the top of the visible area, or user space.</param>
    /// <param name="fromUserSpace">Whether the position is in user space rather than from the top-left.</param>
    /// <returns>The drawing coordinates.</returns>
    private static (double X, double Y) ToGraphics(PdfPage page, double x, double y, bool fromUserSpace = false)
    {
        var media = page.MediaBox;
        var visible = page.CropBox;

        if (fromUserSpace)
        {
            return (x - media.X1, media.Y2 - y);
        }

        return (visible.X1 - media.X1 + x, media.Y2 - visible.Y2 + y);
    }

    private static PdfPage PageOf(PdfDocument document, int? page)
    {
        var number = page ?? 1;

        if (number < 1 || number > document.PageCount)
        {
            throw new PdfToolException($"Page {number} does not exist; the document has {document.PageCount} page(s).");
        }

        return document.Pages[number - 1];
    }

    private static List<int> PdfPageRangeParser(string pages, int count)
    {
        return Tools.PdfPageRange.Parse(pages, count);
    }
}
