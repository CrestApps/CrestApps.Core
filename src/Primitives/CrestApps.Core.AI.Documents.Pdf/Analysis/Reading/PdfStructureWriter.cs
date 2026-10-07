using System.Globalization;
using System.Net;
using System.Text;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Writes a document's structure out as an outline, as Markdown, as HTML or as a JSON model.
/// </summary>
internal static class PdfStructureWriter
{
    private const string HtmlStyle =
        "body{font-family:system-ui,-apple-system,'Segoe UI',Arial,sans-serif;line-height:1.5;max-width:52rem;margin:2rem auto;padding:0 1rem;color:#1f2933;background:#fff}" +
        "table{border-collapse:collapse;margin:1rem 0}th,td{border:1px solid #c8d0d8;padding:.3rem .6rem;text-align:left;vertical-align:top}th{background:#eef1f5}" +
        "figure{margin:1rem 0}figcaption{font-style:italic;color:#52606d}img{max-width:100%;height:auto}" +
        ".page{color:#8a96a3;font-size:.8rem;border-top:1px solid #e4e7eb;margin-top:2rem;padding-top:.3rem}";

    /// <summary>
    /// Writes the structure as an outline: one line per element, indented under its heading.
    /// </summary>
    /// <param name="elements">The elements.</param>
    /// <param name="maxParagraphCharacters">The most characters of a paragraph shown; zero shows all of it.</param>
    /// <returns>The outline lines.</returns>
    public static List<string> ToOutline(IReadOnlyList<PdfStructureElement> elements, int maxParagraphCharacters)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var lines = new List<string>(elements.Count);
        var depth = 0;

        foreach (var element in elements)
        {
            var prefix = "p" + element.Page.ToString(CultureInfo.InvariantCulture) + "  ";

            if (element.Type == PdfStructureElement.HeadingType)
            {
                var level = element.Level ?? 1;

                depth = level;
                lines.Add(prefix + new string(' ', (level - 1) * 2) + "heading " + level.ToString(CultureInfo.InvariantCulture) + ": " + element.Text);

                continue;
            }

            var indent = new string(' ', depth * 2);

            lines.Add(prefix + indent + element.Type switch
            {
                PdfStructureElement.ListItemType => "list item: " + PdfTextPatterns.Truncate(element.Text, maxParagraphCharacters),
                PdfStructureElement.TableType => DescribeTable(element.Region),
                PdfStructureElement.FigureType => DescribeFigure(element.Region),
                PdfStructureElement.CaptionType => "caption: " + PdfTextPatterns.Truncate(element.Text, maxParagraphCharacters),
                _ => "paragraph: " + PdfTextPatterns.Truncate(element.Text, maxParagraphCharacters),
            });
        }

        return lines;
    }

    /// <summary>
    /// Writes the structure as Markdown: headings, paragraphs, lists, tables and figure placeholders.
    /// </summary>
    /// <param name="elements">The elements.</param>
    /// <param name="pageMarkers">Whether a comment marks where each page starts.</param>
    /// <param name="maxParagraphCharacters">The most characters of a paragraph written; zero writes all of it.</param>
    /// <param name="maxTableRows">The most rows of a table written; zero writes all of them.</param>
    /// <returns>The Markdown.</returns>
    public static string ToMarkdown(IReadOnlyList<PdfStructureElement> elements, bool pageMarkers, int maxParagraphCharacters, int maxTableRows)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var builder = new StringBuilder();
        var page = 0;
        string previousType = null;

        foreach (var element in elements)
        {
            if (pageMarkers && element.Page != page)
            {
                Separate(builder, "\n\n");
                builder.Append("<!-- page ").Append(element.Page.ToString(CultureInfo.InvariantCulture)).Append(" -->");
                page = element.Page;
                previousType = null;
            }

            var listContinues = element.Type == PdfStructureElement.ListItemType && previousType == PdfStructureElement.ListItemType;

            Separate(builder, listContinues ? "\n" : "\n\n");

            switch (element.Type)
            {
                case PdfStructureElement.HeadingType:
                    builder.Append('#', Math.Clamp(element.Level ?? 1, 1, 6)).Append(' ').Append(EscapeInline(element.Text));

                    break;

                case PdfStructureElement.ListItemType:
                    builder.Append(IsNumbered(element.Marker) ? NormalizeNumber(element.Marker) : "-")
                        .Append(' ')
                        .Append(EscapeInline(PdfTextPatterns.Truncate(element.Text, maxParagraphCharacters)));

                    break;

                case PdfStructureElement.TableType:
                    AppendMarkdownTable(builder, element.Region?.Rows ?? [], maxTableRows);

                    if (!string.IsNullOrWhiteSpace(element.Text))
                    {
                        builder.Append("\n\n*").Append(EscapeInline(element.Text)).Append('*');
                    }

                    break;

                case PdfStructureElement.FigureType:
                    builder.Append("*[")
                        .Append(string.IsNullOrWhiteSpace(element.Text) ? "Figure" : EscapeInline(element.Text))
                        .Append(" — page ").Append(element.Page.ToString(CultureInfo.InvariantCulture)).Append("]*");

                    break;

                case PdfStructureElement.CaptionType:
                    builder.Append('*').Append(EscapeInline(element.Text)).Append('*');

                    break;

                default:
                    builder.Append(EscapeLineStart(EscapeInline(PdfTextPatterns.Truncate(element.Text, maxParagraphCharacters))));

                    break;
            }

            previousType = element.Type;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes one table as Markdown, its first row as the header.
    /// </summary>
    /// <param name="builder">The builder written to.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="maxRows">The most body rows written; zero writes all of them.</param>
    public static void AppendMarkdownTable(StringBuilder builder, IReadOnlyList<List<string>> rows, int maxRows)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            return;
        }

        var columns = rows.Max(row => row.Count);

        AppendMarkdownRow(builder, rows[0], columns);
        builder.Append('\n').Append('|');

        for (var column = 0; column < columns; column++)
        {
            builder.Append(" --- |");
        }

        var shown = maxRows > 0
            ? Math.Min(rows.Count - 1, maxRows)
            : rows.Count - 1;

        for (var row = 1; row <= shown; row++)
        {
            builder.Append('\n');
            AppendMarkdownRow(builder, rows[row], columns);
        }

        if (shown < rows.Count - 1)
        {
            builder.Append('\n').Append(FormattableString.Invariant($"({rows.Count - 1 - shown} more rows not shown)"));
        }
    }

    /// <summary>
    /// Writes the structure as a complete HTML document: every value is encoded, and the document runs no
    /// script and loads nothing from elsewhere.
    /// </summary>
    /// <param name="elements">The elements.</param>
    /// <param name="title">The document title.</param>
    /// <param name="language">The document language, or <see langword="null"/>.</param>
    /// <param name="maxImageBytes">The most bytes of pictures embedded; pictures past it are described instead.</param>
    /// <returns>The HTML.</returns>
    public static string ToHtml(IReadOnlyList<PdfStructureElement> elements, string title, string language, long maxImageBytes)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var builder = new StringBuilder();
        var lang = IsLanguageTag(language)
            ? language.Trim()
            : null;
        var budget = Math.Max(0, maxImageBytes);

        builder.Append("<!DOCTYPE html>\n<html");

        if (lang is not null)
        {
            builder.Append(" lang=\"").Append(lang).Append('"');
        }

        builder.Append(">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<title>")
            .Append(Encode(string.IsNullOrWhiteSpace(title) ? "Document" : title))
            .Append("</title>\n<style>").Append(HtmlStyle).Append("</style>\n</head>\n<body>\n");

        var page = 0;
        string openList = null;

        foreach (var element in elements)
        {
            var isItem = element.Type == PdfStructureElement.ListItemType;
            var listTag = isItem && IsNumbered(element.Marker)
                ? "ol"
                : "ul";

            if (openList is not null && (!isItem || listTag != openList || element.Page != page))
            {
                builder.Append("</").Append(openList).Append(">\n");
                openList = null;
            }

            if (element.Page != page)
            {
                page = element.Page;

                var number = page.ToString(CultureInfo.InvariantCulture);

                builder.Append("<div class=\"page\" id=\"page-").Append(number).Append("\">Page ").Append(number).Append("</div>\n");
            }

            switch (element.Type)
            {
                case PdfStructureElement.HeadingType:
                    {
                        var level = Math.Clamp(element.Level ?? 1, 1, 6).ToString(CultureInfo.InvariantCulture);

                        builder.Append("<h").Append(level).Append('>').Append(Encode(element.Text)).Append("</h").Append(level).Append(">\n");

                        break;
                    }

                case PdfStructureElement.ListItemType:
                    if (openList is null)
                    {
                        openList = listTag;
                        builder.Append('<').Append(openList).Append(">\n");
                    }

                    builder.Append("<li>").Append(Encode(element.Text)).Append("</li>\n");

                    break;

                case PdfStructureElement.TableType:
                    AppendHtmlTable(builder, element.Region?.Rows ?? [], element.Text);

                    break;

                case PdfStructureElement.FigureType:
                    budget = AppendHtmlFigure(builder, element, budget);

                    break;

                case PdfStructureElement.CaptionType:
                    builder.Append("<p><em>").Append(Encode(element.Text)).Append("</em></p>\n");

                    break;

                default:
                    builder.Append("<p>").Append(Encode(element.Text)).Append("</p>\n");

                    break;
            }
        }

        if (openList is not null)
        {
            builder.Append("</").Append(openList).Append(">\n");
        }

        builder.Append("</body>\n</html>\n");

        return builder.ToString();
    }

    /// <summary>
    /// Builds the JSON model of the structure.
    /// </summary>
    /// <param name="elements">The elements.</param>
    /// <param name="maxParagraphCharacters">The most characters of a paragraph kept; zero keeps all of it.</param>
    /// <param name="includeTableRows">Whether tables carry every row, rather than their size and header.</param>
    /// <returns>One object per element.</returns>
    public static List<object> ToJsonModel(IReadOnlyList<PdfStructureElement> elements, int maxParagraphCharacters, bool includeTableRows)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var model = new List<object>(elements.Count);

        foreach (var element in elements)
        {
            switch (element.Type)
            {
                case PdfStructureElement.TableType:
                    {
                        var rows = element.Region?.Rows ?? [];

                        model.Add(new
                        {
                            type = element.Type,
                            page = element.Page,
                            rows = rows.Count,
                            columns = element.Region?.ColumnCount ?? 0,
                            header = rows.Count > 0 ? rows[0] : null,
                            data = includeTableRows ? rows : null,
                            caption = element.Text,
                            box = element.Box,
                        });

                        break;
                    }

                case PdfStructureElement.FigureType:
                    model.Add(new
                    {
                        type = element.Type,
                        page = element.Page,
                        kind = element.Region?.Kind,
                        pixel_width = PositiveOrNull(element.Region?.PixelWidth),
                        pixel_height = PositiveOrNull(element.Region?.PixelHeight),
                        caption = element.Text,
                        box = element.Box,
                    });

                    break;

                default:
                    model.Add(new
                    {
                        type = element.Type,
                        page = element.Page,
                        level = element.Level,
                        marker = string.IsNullOrEmpty(element.Marker) ? null : element.Marker,
                        text = PdfTextPatterns.Truncate(element.Text, maxParagraphCharacters),
                        box = element.Box,
                    });

                    break;
            }
        }

        return model;
    }

    /// <summary>
    /// Describes a table in a few words: its size and header.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <returns>For example <c>table 4 rows × 3 columns, header: Region | Revenue | Share</c>.</returns>
    public static string DescribeTable(PdfRegion table)
    {
        if (table?.Rows is not { Count: > 0 } rows)
        {
            return "table";
        }

        var text = FormattableString.Invariant($"table {rows.Count} rows × {table.ColumnCount} columns, header: ") +
            PdfTextPatterns.Truncate(string.Join(" | ", rows[0]), 160);

        return string.IsNullOrWhiteSpace(table.Caption)
            ? text
            : text + " — caption: " + table.Caption;
    }

    /// <summary>
    /// Describes a figure in a few words: what it is, its size and its caption.
    /// </summary>
    /// <param name="figure">The figure.</param>
    /// <returns>For example <c>figure (image 64×48 px): Figure 1: Company logo</c>.</returns>
    public static string DescribeFigure(PdfRegion figure)
    {
        if (figure is null)
        {
            return "figure";
        }

        var kind = "image";

        if (figure.Kind == PdfRegion.DrawingKind)
        {
            kind = "drawing";
        }
        else if (figure.PixelWidth > 0)
        {
            kind = FormattableString.Invariant($"image {figure.PixelWidth}×{figure.PixelHeight} px");
        }

        return string.IsNullOrWhiteSpace(figure.Caption)
            ? "figure (" + kind + ")"
            : "figure (" + kind + "): " + figure.Caption;
    }

    private static int? PositiveOrNull(int? value)
    {
        return value > 0
            ? value
            : null;
    }

    private static void Separate(StringBuilder builder, string separator)
    {
        if (builder.Length > 0)
        {
            builder.Append(separator);
        }
    }

    private static void AppendMarkdownRow(StringBuilder builder, List<string> row, int columns)
    {
        builder.Append('|');

        for (var column = 0; column < columns; column++)
        {
            var value = column < row.Count
                ? row[column]
                : string.Empty;

            builder.Append(' ').Append(EscapeCell(value)).Append(" |");
        }
    }

    private static void AppendHtmlTable(StringBuilder builder, List<List<string>> rows, string caption)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var columns = rows.Max(row => row.Count);

        builder.Append("<table>\n");

        if (!string.IsNullOrWhiteSpace(caption))
        {
            builder.Append("<caption>").Append(Encode(caption)).Append("</caption>\n");
        }

        for (var row = 0; row < rows.Count; row++)
        {
            var cell = row == 0
                ? "th"
                : "td";

            builder.Append(row == 0 ? "<thead><tr>" : "<tr>");

            for (var column = 0; column < columns; column++)
            {
                builder.Append('<').Append(cell).Append('>')
                    .Append(Encode(column < rows[row].Count ? rows[row][column] : string.Empty))
                    .Append("</").Append(cell).Append('>');
            }

            builder.Append(row == 0 ? "</tr></thead>\n<tbody>\n" : "</tr>\n");
        }

        builder.Append("</tbody>\n</table>\n");
    }

    private static long AppendHtmlFigure(StringBuilder builder, PdfStructureElement element, long budget)
    {
        var region = element.Region;
        var caption = string.IsNullOrWhiteSpace(element.Text)
            ? null
            : element.Text;

        builder.Append("<figure>");

        // Only pictures this code encoded itself are embedded, under a media type it chose.
        if (region?.Content is { Length: > 0 } content &&
            content.Length <= budget &&
            region.MediaType is "image/png" or "image/jpeg")
        {
            budget -= content.Length;

            builder.Append("<img alt=\"").Append(Encode(caption ?? "Figure")).Append("\" src=\"data:").Append(region.MediaType)
                .Append(";base64,").Append(Convert.ToBase64String(content)).Append("\">");
        }
        else
        {
            builder.Append("<p>[").Append(Encode(DescribeFigure(region))).Append("]</p>");
        }

        if (caption is not null)
        {
            builder.Append("<figcaption>").Append(Encode(caption)).Append("</figcaption>");
        }

        builder.Append("</figure>\n");

        return budget;
    }

    private static string Encode(string value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }

    private static bool IsLanguageTag(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 35)
        {
            return false;
        }

        foreach (var character in value.Trim())
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumbered(string marker)
    {
        return !string.IsNullOrEmpty(marker) && char.IsAsciiDigit(marker.TrimStart('(')[0]);
    }

    private static string NormalizeNumber(string marker)
    {
        // Markdown numbers a list "1." — "1)" and "(1)" are written that way.
        var digits = new string([.. marker.Where(char.IsAsciiDigit)]);

        return digits.Length == 0
            ? "1."
            : digits + ".";
    }

    private static string EscapeInline(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length + 8);

        foreach (var character in text)
        {
            if (character is '\\' or '*' or '_' or '`' or '[' or ']' or '~' or '<')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string EscapeLineStart(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Text that happens to start the way a Markdown block does is kept as text.
        return text[0] is '#' or '>' or '-' or '+' || PdfTextPatterns.IsListItem(text)
            ? "\\" + text
            : text;
    }

    private static string EscapeCell(string value)
    {
        return EscapeInline(PdfTextPatterns.OneLine(value)).Replace("|", "\\|", StringComparison.Ordinal);
    }
}
