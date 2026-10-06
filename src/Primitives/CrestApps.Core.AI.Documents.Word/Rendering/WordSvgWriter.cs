using System.Globalization;
using System.Security;
using System.Text;

namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <summary>
/// Draws a laid-out page as an SVG picture: the page the preview shows in the conversation.
/// </summary>
/// <remarks>
/// Every run of text is given the width the layout measured it at, so the browser stretches or squeezes its own
/// glyphs to fit: line breaks, alignment and justification then look the way the layout placed them, whatever
/// typeface the viewer actually has.
/// </remarks>
internal static class WordSvgWriter
{
    /// <summary>
    /// Draws a page.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="pixelWidth">The width the picture is sized to, in pixels.</param>
    /// <param name="highlights">Boxes to outline, such as the element a preview of content is about.</param>
    /// <returns>The SVG document.</returns>
    public static string Write(WordLayoutPage page, int pixelWidth, IEnumerable<(double X, double Y, double Width, double Height)> highlights = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        var scale = Math.Max(100, pixelWidth) / page.Width;
        var builder = new StringBuilder(64 * 1024);

        builder.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Math.Round(page.Width * scale)}\" height=\"{Math.Round(page.Height * scale)}\" viewBox=\"0 0 {N(page.Width)} {N(page.Height)}\">");
        builder.Append(CultureInfo.InvariantCulture, $"<rect x=\"0\" y=\"0\" width=\"{N(page.Width)}\" height=\"{N(page.Height)}\" fill=\"{Paint(page.Background, "FFFFFF")}\"/>");

        foreach (var item in page.Items)
        {
            switch (item)
            {
                case WordTextItem text:
                    WriteText(builder, text);

                    break;

                case WordRectItem rect:
                    WriteRect(builder, rect);

                    break;

                case WordLineItem line:
                    builder.Append(CultureInfo.InvariantCulture, $"<line x1=\"{N(line.X1)}\" y1=\"{N(line.Y1)}\" x2=\"{N(line.X2)}\" y2=\"{N(line.Y2)}\" stroke=\"{Paint(line.Color, "000000")}\" stroke-width=\"{N(Math.Max(0.25, line.Width))}\"");

                    if (line.Dotted)
                    {
                        builder.Append(" stroke-dasharray=\"1,2\"");
                    }

                    builder.Append("/>");

                    break;

                case WordImageItem image:
                    WriteImage(builder, image);

                    break;

                case WordPolygonItem polygon:
                    WritePolygon(builder, polygon);

                    break;
            }
        }

        foreach (var (x, y, width, height) in highlights ?? [])
        {
            builder.Append(CultureInfo.InvariantCulture, $"<rect x=\"{N(x - 3)}\" y=\"{N(y - 3)}\" width=\"{N(width + 6)}\" height=\"{N(height + 6)}\" fill=\"#FFC000\" fill-opacity=\"0.12\" stroke=\"#ED7D31\" stroke-width=\"1.5\" stroke-dasharray=\"4,2\"/>");
        }

        builder.Append("</svg>");

        return builder.ToString();
    }

    /// <summary>
    /// Returns the font list a typeface is drawn with: itself, then a metric-compatible substitute, then a
    /// generic family of the same kind.
    /// </summary>
    /// <param name="font">The typeface.</param>
    /// <returns>The font list, for a <c>font-family</c> attribute.</returns>
    public static string FontFamily(string font)
    {
        var name = string.IsNullOrWhiteSpace(font) ? "Calibri" : font.Trim();
        var fallback = name switch
        {
            _ when WordTextMeasurer.IsMonospace(name) => "'Courier New', monospace",
            "Calibri" or "Calibri Light" => "Carlito, 'Segoe UI', Arial, sans-serif",
            "Cambria" or "Cambria Math" => "Caladea, Georgia, 'Times New Roman', serif",
            "Times New Roman" or "Times" or "Garamond" or "Book Antiqua" or "Palatino Linotype" or "Georgia" or "Constantia" => "'Liberation Serif', 'Times New Roman', serif",
            "Aptos" or "Aptos Display" => "'Segoe UI', Calibri, Carlito, Arial, sans-serif",
            _ => "'Liberation Sans', Arial, sans-serif",
        };

        return "'" + name.Replace("'", string.Empty, StringComparison.Ordinal) + "', " + fallback;
    }

    private static void WriteText(StringBuilder builder, WordTextItem text)
    {
        if (string.IsNullOrEmpty(text.Text))
        {
            return;
        }

        var format = text.Format;
        var size = format.SmallCaps ? format.DrawnSize * 0.8 : format.DrawnSize;
        var color = Paint(text.MarkupColor ?? format.Color, "000000");

        builder.Append(CultureInfo.InvariantCulture, $"<text x=\"{N(text.X)}\" y=\"{N(text.Baseline)}\" font-family=\"{Escape(FontFamily(format.Font))}\" font-size=\"{N(size)}\" fill=\"{color}\"");

        if (format.Bold)
        {
            builder.Append(" font-weight=\"bold\"");
        }

        if (format.Italic)
        {
            builder.Append(" font-style=\"italic\"");
        }

        if (text.Text.Length > 1 && text.Width > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $" textLength=\"{N(text.Width)}\" lengthAdjust=\"spacingAndGlyphs\"");
        }

        builder.Append(" xml:space=\"preserve\">").Append(Escape(text.Text)).Append("</text>");

        if (format.Underline)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<line x1=\"{N(text.X)}\" y1=\"{N(text.Baseline + (size * 0.12))}\" x2=\"{N(text.X + text.Width)}\" y2=\"{N(text.Baseline + (size * 0.12))}\" stroke=\"{color}\" stroke-width=\"{N(Math.Max(0.5, size / 16))}\"/>");
        }

        if (format.Strike)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<line x1=\"{N(text.X)}\" y1=\"{N(text.Baseline - (size * 0.3))}\" x2=\"{N(text.X + text.Width)}\" y2=\"{N(text.Baseline - (size * 0.3))}\" stroke=\"{color}\" stroke-width=\"{N(Math.Max(0.5, size / 16))}\"/>");
        }
    }

    private static void WriteRect(StringBuilder builder, WordRectItem rect)
    {
        var fill = rect.Fill is null ? "none" : Paint(rect.Fill, "none");
        var stroke = rect.Stroke is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" stroke=\"{Paint(rect.Stroke, "000000")}\" stroke-width=\"{N(rect.StrokeWidth)}\"");
        var dash = rect.Dashed ? " stroke-dasharray=\"4,3\"" : string.Empty;

        if (rect.Ellipse)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<ellipse cx=\"{N(rect.X + (rect.Width / 2))}\" cy=\"{N(rect.Y + (rect.Height / 2))}\" rx=\"{N(rect.Width / 2)}\" ry=\"{N(rect.Height / 2)}\" fill=\"{fill}\"{stroke}{dash}/>");

            return;
        }

        var radius = rect.Rounded ? string.Create(CultureInfo.InvariantCulture, $" rx=\"{N(Math.Min(rect.Width, rect.Height) * 0.15)}\"") : string.Empty;

        builder.Append(CultureInfo.InvariantCulture, $"<rect x=\"{N(rect.X)}\" y=\"{N(rect.Y)}\" width=\"{N(Math.Max(0, rect.Width))}\" height=\"{N(Math.Max(0, rect.Height))}\" fill=\"{fill}\"{stroke}{radius}{dash}/>");
    }

    private static void WriteImage(StringBuilder builder, WordImageItem image)
    {
        if (image.Bytes is { Length: > 0 } && image.MediaType is "image/png" or "image/jpeg" or "image/gif" or "image/bmp")
        {
            builder.Append(CultureInfo.InvariantCulture, $"<image x=\"{N(image.X)}\" y=\"{N(image.Y)}\" width=\"{N(image.Width)}\" height=\"{N(image.Height)}\" preserveAspectRatio=\"none\" href=\"data:{image.MediaType};base64,");
            builder.Append(Convert.ToBase64String(image.Bytes));
            builder.Append("\"/>");

            return;
        }

        builder.Append(CultureInfo.InvariantCulture, $"<rect x=\"{N(image.X)}\" y=\"{N(image.Y)}\" width=\"{N(image.Width)}\" height=\"{N(image.Height)}\" fill=\"#F2F2F2\" stroke=\"#A6A6A6\" stroke-width=\"0.75\" stroke-dasharray=\"4,3\"/>");

        var label = string.IsNullOrWhiteSpace(image.Label) ? "Picture" : image.Label;
        var size = Math.Clamp(Math.Min(image.Height / 4, 10), 5, 10);
        var maxCharacters = Math.Max(4, (int)(image.Width / (size * 0.5)));

        if (label.Length > maxCharacters)
        {
            label = label[..(maxCharacters - 1)] + "…";
        }

        builder.Append(CultureInfo.InvariantCulture, $"<text x=\"{N(image.X + (image.Width / 2))}\" y=\"{N(image.Y + (image.Height / 2) + (size / 3))}\" font-family=\"Arial, sans-serif\" font-size=\"{N(size)}\" fill=\"#595959\" text-anchor=\"middle\">{Escape(label)}</text>");
    }

    private static void WritePolygon(StringBuilder builder, WordPolygonItem polygon)
    {
        if (polygon.Points.Count < 2)
        {
            return;
        }

        var points = string.Join(' ', polygon.Points.Select(point => N(point.X) + "," + N(point.Y)));
        var fill = polygon.Fill is null || polygon.Open ? "none" : Paint(polygon.Fill, "none");
        var stroke = polygon.Stroke is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" stroke=\"{Paint(polygon.Stroke, "000000")}\" stroke-width=\"{N(polygon.StrokeWidth)}\" stroke-linejoin=\"round\"");

        builder.Append(polygon.Open ? "<polyline" : "<polygon").Append(" points=\"").Append(points).Append("\" fill=\"").Append(fill).Append('"').Append(stroke).Append("/>");
    }

    /// <summary>
    /// Writes a color for a paint attribute. Colors come from the document, which the user uploaded, so only a
    /// strict six-digit hexadecimal value is ever written; anything else becomes the fallback rather than text
    /// that could close the attribute and add markup to a picture this host serves.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="fallback">The six-digit color, or <c>none</c>, used when the color is not one.</param>
    /// <returns>The attribute value.</returns>
    public static string Paint(string color, string fallback)
    {
        if (color is { Length: 6 } && color.All(char.IsAsciiHexDigit))
        {
            return "#" + color;
        }

        return fallback == "none" ? "none" : "#" + fallback;
    }

    private static string N(double value)
    {
        return Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string Escape(string text)
    {
        // Characters XML does not allow are dropped, so a stray control character cannot break the picture.
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (character is '\t' or '\n' or '\r' || (character >= 0x20 && character != '￾' && character != '￿'))
            {
                builder.Append(character);
            }
        }

        return SecurityElement.Escape(builder.ToString());
    }
}
