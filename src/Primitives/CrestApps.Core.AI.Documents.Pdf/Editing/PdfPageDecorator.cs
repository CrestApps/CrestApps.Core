using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Graphics;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Images;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Draws on top of (or underneath) existing pages: watermarks, stamps, page numbers and running text.
/// </summary>
/// <remarks>
/// Used both after a composed document is laid out and when an uploaded PDF is edited, so a watermark reads
/// the same whichever way the document came to be.
/// </remarks>
internal static class PdfPageDecorator
{
    /// <summary>
    /// Draws a watermark across a page.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="watermark">The watermark.</param>
    /// <param name="image">The watermark picture, when it is an image watermark.</param>
    /// <param name="fontFamily">The font family text is drawn in.</param>
    public static void DrawWatermark(PdfPage page, PdfWatermarkDefinition watermark, PdfImageData image, string fontFamily)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(watermark);

        var picture = image?.Bytes is { Length: > 0 } bytes
            ? PdfImage.TryLoad(bytes)
            : null;

        if (picture is null && string.IsNullOrWhiteSpace(watermark.Text))
        {
            return;
        }

        // A picture goes behind the content unless it was explicitly asked to go on top; drawn over the page
        // at full strength it would hide it.
        var behind = watermark.Behind ?? picture is not null;
        var opacity = Math.Clamp(watermark.Opacity ?? (picture is null ? 0.18 : 0.12), 0.02, 1);
        var canvas = PdfCanvas.ForPage(page, behind);
        var width = page.Width;
        var height = page.Height;
        var centerY = (watermark.Position?.Trim().ToLowerInvariant()) switch
        {
            "top" => height * 0.18,
            "bottom" => height * 0.82,
            _ => height / 2,
        };

        var diagonal = -Math.Atan2(height, width) * 180 / Math.PI;
        var rotation = -(watermark.Rotation ?? -diagonal);

        canvas.Translate(width / 2, centerY);
        canvas.RotateAt(rotation, 0, 0);

        if (picture is not null)
        {
            var targetWidth = Math.Min(width, height) * 0.6;
            var targetHeight = targetWidth * picture.Height / picture.Width;

            canvas.DrawImage(picture, -targetWidth / 2, -targetHeight / 2, targetWidth, targetHeight);
            canvas.Commit();

            return;
        }

        var color = PdfColor.Parse(watermark.Color, new PdfColor(0x80, 0x80, 0x80));
        var span = Math.Sqrt((width * width) + (height * height)) * 0.7;
        var fontSize = watermark.FontSize ?? Math.Clamp(span / Math.Max(watermark.Text.Length, 1) * 1.45, 18, 160);
        var font = PdfFontLibrary.Get(fontFamily ?? PdfFontFamilies.Default, bold: true);
        var textWidth = font.Measure(watermark.Text, fontSize);

        // The text is centred on the point both ways: the middle between its ascent and descent is on it.
        canvas.DrawText(watermark.Text, font, fontSize, -textWidth / 2, (font.Ascent + font.Descent) * fontSize / 2, new PdfBrush(color, opacity));
        canvas.Commit();
    }

    /// <summary>
    /// Writes a line of text at a position on a page: a page number, a stamp, a running head.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="text">The text.</param>
    /// <param name="position">Where: <c>top-left</c>, <c>bottom-center</c>, <c>center</c> and the like.</param>
    /// <param name="style">How the text is drawn.</param>
    public static void DrawText(PdfPage page, string text, string position, PdfTextStampStyle style)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(style);

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var canvas = PdfCanvas.ForPage(page);
        var font = PdfFontLibrary.Get(style.FontFamily ?? PdfFontFamilies.Default, style.Bold);
        var textWidth = font.Measure(text, style.FontSize);
        var textHeight = font.LineSpacing * style.FontSize;
        var width = page.Width;
        var height = page.Height;
        var margin = style.Margin;
        var key = (position ?? "bottom-center").Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');

        var x = key.EndsWith("left", StringComparison.Ordinal)
            ? margin
            : key.EndsWith("right", StringComparison.Ordinal)
                ? width - margin - textWidth
                : (width - textWidth) / 2;

        var y = key.StartsWith("top", StringComparison.Ordinal) || key.StartsWith("header", StringComparison.Ordinal)
            ? margin
            : key is "center" or "middle"
                ? (height - textHeight) / 2
                : height - margin - textHeight;

        if (style.Box)
        {
            var padding = style.FontSize * 0.35;

            canvas.DrawRectangle(x - padding, y - padding, textWidth + (padding * 2), textHeight + (padding * 2), null, new PdfPen(style.Color, Math.Max(1, style.FontSize / 10), style.Opacity));
        }

        canvas.DrawText(text, font, style.FontSize, x, y + (font.Ascent * style.FontSize), new PdfBrush(style.Color, style.Opacity));
        canvas.Commit();
    }
}
