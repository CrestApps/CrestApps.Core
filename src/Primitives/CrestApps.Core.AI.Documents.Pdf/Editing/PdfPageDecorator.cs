using CrestApps.Core.AI.Documents.Pdf.Composition;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

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

        // A picture cannot be made translucent through a brush, so an image watermark goes behind the content
        // unless it was explicitly asked to go on top; drawn over the page at full strength it would hide it.
        var behind = watermark.Behind ?? image is not null;
        var opacity = Math.Clamp(watermark.Opacity ?? (image is null ? 0.18 : 0.12), 0.02, 1);

        using var graphics = XGraphics.FromPdfPage(page, behind ? XGraphicsPdfPageOptions.Prepend : XGraphicsPdfPageOptions.Append);

        var width = page.Width.Point;
        var height = page.Height.Point;
        var centerY = (watermark.Position?.Trim().ToLowerInvariant()) switch
        {
            "top" => height * 0.18,
            "bottom" => height * 0.82,
            _ => height / 2,
        };

        var diagonal = -Math.Atan2(height, width) * 180 / Math.PI;
        var rotation = -(watermark.Rotation ?? -diagonal);

        graphics.TranslateTransform(width / 2, centerY);
        graphics.RotateTransform(rotation);

        if (image is not null && PdfImageInfo.TryRead(image.Bytes, out _, out var pixelWidth, out var pixelHeight))
        {
            using var stream = new MemoryStream(image.Bytes);
            using var picture = XImage.FromStream(stream);

            var targetWidth = Math.Min(width, height) * 0.6;
            var targetHeight = targetWidth * pixelHeight / pixelWidth;

            graphics.DrawImage(picture, -targetWidth / 2, -targetHeight / 2, targetWidth, targetHeight);

            return;
        }

        if (string.IsNullOrWhiteSpace(watermark.Text))
        {
            return;
        }

        var color = PdfColor.Parse(watermark.Color, new PdfColor(0x80, 0x80, 0x80));
        var span = Math.Sqrt((width * width) + (height * height)) * 0.7;
        var fontSize = watermark.FontSize ?? Math.Clamp(span / Math.Max(watermark.Text.Length, 1) * 1.45, 18, 160);
        var font = new XFont(fontFamily ?? PdfFontFamilies.Default, fontSize, XFontStyleEx.Bold);
        var brush = new XSolidBrush(color.ToXColor(opacity));

        graphics.DrawString(watermark.Text, font, brush, 0, 0, XStringFormats.Center);
    }

    /// <summary>
    /// Draws one line of text at a named position on a page.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="text">The text.</param>
    /// <param name="position">The position: <c>top-left</c>, <c>top-center</c>, <c>top-right</c>, <c>center</c>, <c>bottom-left</c>, <c>bottom-center</c> or <c>bottom-right</c>.</param>
    /// <param name="style">How the text is drawn.</param>
    public static void DrawText(PdfPage page, string text, string position, PdfTextStampStyle style)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(style);

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

        var font = new XFont(style.FontFamily ?? PdfFontFamilies.Default, style.FontSize, style.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        var size = graphics.MeasureString(text, font);
        var width = page.Width.Point;
        var height = page.Height.Point;
        var margin = style.Margin;
        var key = (position ?? "bottom-center").Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');

        var x = key.EndsWith("left", StringComparison.Ordinal)
            ? margin
            : key.EndsWith("right", StringComparison.Ordinal)
                ? width - margin - size.Width
                : (width - size.Width) / 2;

        var y = key.StartsWith("top", StringComparison.Ordinal) || key.StartsWith("header", StringComparison.Ordinal)
            ? margin
            : key is "center" or "middle"
                ? (height - size.Height) / 2
                : height - margin - size.Height;

        if (style.Box)
        {
            var padding = style.FontSize * 0.35;
            var border = new XPen(style.Color.ToXColor(style.Opacity), Math.Max(1, style.FontSize / 10));
            graphics.DrawRectangle(border, x - padding, y - padding, size.Width + (padding * 2), size.Height + (padding * 2));
        }

        graphics.DrawString(text, font, new XSolidBrush(style.Color.ToXColor(style.Opacity)), new XRect(x, y, size.Width, size.Height), XStringFormats.TopLeft);
    }
}
