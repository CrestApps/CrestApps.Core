using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf;
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

            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

            foreach (var (_, match) in group)
            {
                DrawReplacement(graphics, page, match, operation, warnings);
            }
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

        using (var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
        {
            var font = Font(operation.FontFamily, operation.FontSize ?? 11, operation.Bold == true, operation.Italic == true);
            var brush = new XSolidBrush(PdfColor.Parse(operation.Color, new PdfColor(0, 0, 0)).ToXColor());
            var (x, y) = ToGraphics(page, operation.X ?? 72, operation.Y ?? 72);

            if (operation.Width is > 0)
            {
                var height = operation.Height ?? Math.Max(font.Size * 1.3, page.Height.Point - y);
                var formatter = new XTextFormatter(graphics)
                {
                    Alignment = (operation.Align?.Trim().ToLowerInvariant()) switch
                    {
                        "center" => XParagraphAlignment.Center,
                        "right" => XParagraphAlignment.Right,
                        "justify" => XParagraphAlignment.Justify,
                        _ => XParagraphAlignment.Left,
                    },
                };

                formatter.DrawString(operation.Text, font, brush, new XRect(x, y, operation.Width.Value, height), XStringFormats.TopLeft);
            }
            else
            {
                var lineHeight = font.Size * 1.2;
                var lines = operation.Text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');

                for (var index = 0; index < lines.Length; index++)
                {
                    graphics.DrawString(lines[index], font, brush, x, y + (index * lineHeight), XStringFormats.TopLeft);
                }
            }
        }

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

        if (!PdfImageInfo.TryRead(image.Bytes, out _, out var pixelWidth, out var pixelHeight))
        {
            throw new PdfToolException("The image is not a JPEG, PNG, GIF or BMP.");
        }

        using var document = PdfFiles.OpenForEditing(bytes, password);

        var page = PageOf(document, operation.Page);
        var aspect = (double)pixelWidth / pixelHeight;
        var width = operation.Width ?? (operation.Height is > 0 ? operation.Height.Value * aspect : Math.Min(pixelWidth * 0.75, page.Width.Point / 2));
        var height = operation.Height ?? (width / aspect);

        using (var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
        {
            using var stream = new MemoryStream(image.Bytes);
            using var picture = XImage.FromStream(stream);
            var (x, y) = ToGraphics(page, operation.X ?? 72, operation.Y ?? 72);

            graphics.DrawImage(picture, x, y, width, height);
        }

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

    private static void DrawReplacement(XGraphics graphics, PdfPage page, PdfTextMatch match, PdfContentEditOperation operation, List<string> warnings)
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

        var size = operation.FontSize ?? Math.Max(first.PointSize, 1);
        var font = Font(family, size, bold, italic);
        var color = ReadColor(first, operation.Color);
        var (x, y) = ToGraphics(page, first.StartBaseLine.X, first.StartBaseLine.Y, fromUserSpace: true);

        graphics.DrawString(operation.Replace, font, new XSolidBrush(color.ToXColor()), x, y, XStringFormats.BaseLineLeft);

        var oldWidth = match.Boxes.Sum(box => box.Width);
        var newWidth = graphics.MeasureString(operation.Replace, font).Width;

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

    private static XFont Font(string family, double size, bool bold, bool italic)
    {
        var style = (bold, italic) switch
        {
            (true, true) => XFontStyleEx.BoldItalic,
            (true, false) => XFontStyleEx.Bold,
            (false, true) => XFontStyleEx.Italic,
            _ => XFontStyleEx.Regular,
        };

        return new XFont(PdfFontFamilies.Resolve(family, PdfFontFamilies.Default, null), Math.Clamp(size, 1, 200), style);
    }

    /// <summary>
    /// Converts a position to the coordinates PDFsharp draws a page in: points from the top-left of the media box.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="x">The x coordinate: points from the left of the visible area, or user space.</param>
    /// <param name="y">The y coordinate: points from the top of the visible area, or user space.</param>
    /// <param name="fromUserSpace">Whether the position is in user space rather than from the top-left.</param>
    /// <returns>The drawing coordinates.</returns>
    private static (double X, double Y) ToGraphics(PdfPage page, double x, double y, bool fromUserSpace = false)
    {
        var media = page.MediaBox;
        var visible = page.EffectiveCropBoxReadOnly;

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
