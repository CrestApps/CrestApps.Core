using System.Globalization;
using System.Text;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Draws a <see cref="TabularPreviewGrid"/> as an SVG picture of a spreadsheet.
/// </summary>
/// <remarks>
/// SVG rather than a raster image because nothing in this process can rasterize one. A vector picture needs
/// no drawing library and no fonts shipped with the host: the browser that shows it already has both, so the
/// preview is a few kilobytes of markup that stays sharp at whatever size the chat surface gives it.
/// <para>
/// It is drawn to look like the sheet the reader uploaded — the lettered column band, the numbered rows, the
/// filled header, numbers on the right — because the point of a preview is recognition. A reader who cannot
/// tell at a glance that this is their file has not been shown anything.
/// </para>
/// </remarks>
internal static class TabularPreviewSvgRenderer
{
    private const double Padding = 16;
    private const double TitleFontSize = 14;
    private const double SubtitleFontSize = 11;
    private const double CaptionFontSize = 11;
    private const double CellFontSize = 11.5;
    private const double BandFontSize = 10;
    private const double RowHeight = 22;
    private const double BandHeight = 18;
    private const double CellPadding = 8;
    private const double MinColumnWidth = 52;
    private const double MaxColumnWidth = 230;
    private const double MinGutterWidth = 34;

    private const string FontFamily = "Segoe UI, Helvetica Neue, Helvetica, Arial, sans-serif";
    private const string PageFill = "#ffffff";
    private const string BandFill = "#eef1f5";
    private const string BandText = "#5b6673";
    // The writer's own defaults, so a table nobody formatted is still drawn the way it will be written.
    // These are the values SpreadsheetGeneratedFileWriter applies when no header style is recorded.
    private const string DefaultHeaderFill = "#1F4E79";
    private const string DefaultHeaderText = "#FFFFFF";
    private const string DefaultBandFill = "#F2F2F2";
    private const string GridLine = "#d5dbe3";
    private const string OuterLine = "#b9c2cd";
    private const string BodyText = "#1f2933";

    /// <summary>
    /// Takes a recorded colour, or the default when none was recorded.
    /// </summary>
    /// <remarks>
    /// A recorded colour is written the way a workbook writes one, which may carry a leading alpha pair
    /// (<c>FF1F4E79</c>) and may omit the hash. Both are normalised to what SVG accepts; anything that is
    /// not a colour at all is ignored rather than drawn as a broken fill.
    /// </remarks>
    private static string ResolveColor(string recorded, string fallback)
    {
        if (string.IsNullOrWhiteSpace(recorded))
        {
            return fallback;
        }

        var value = recorded.Trim().TrimStart('#');

        // A workbook writes ARGB; SVG wants RGB, and the alpha a spreadsheet fill carries is always opaque.
        if (value.Length == 8)
        {
            value = value[2..];
        }

        if (value.Length is not (3 or 6))
        {
            return fallback;
        }

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return fallback;
            }
        }

        return "#" + value;
    }
    private const string MutedText = "#57606a";

    /// <summary>
    /// Draws the grid.
    /// </summary>
    /// <param name="grid">The shaped grid.</param>
    /// <param name="options">The preview limits, which bound how wide the picture may get.</param>
    /// <returns>The SVG markup and the number of columns it was able to draw.</returns>
    public static (string Markup, int ShownColumnCount) Render(TabularPreviewGrid grid, TabularPreviewOptions options)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(options);

        var widths = MeasureColumns(grid);
        var gutterWidth = MeasureGutter(grid);
        var shownColumnCount = FitColumns(widths, gutterWidth, options.MaxImageWidth);
        var gridWidth = gutterWidth;

        for (var index = 0; index < shownColumnCount; index++)
        {
            gridWidth += widths[index];
        }

        // A narrow table must not be given a heading wider than its own picture. The file name a subtitle
        // carries is routinely longer than two columns of data, and text drawn past the edge of the canvas
        // is cut off at whatever character the viewport lands on. So the canvas grows to fit the heading,
        // and a heading too long even for the widest allowed canvas is truncated to end deliberately.
        var caption = grid.BuildCaption(shownColumnCount);
        var contentWidth = Math.Min(
            Math.Max(gridWidth, Math.Max(
                MeasureText(grid.Title, TitleFontSize, bold: true),
                Math.Max(
                    MeasureText(grid.Subtitle, SubtitleFontSize, bold: false),
                    MeasureText(caption, CaptionFontSize, bold: false)))),
            Math.Max(gridWidth, options.MaxImageWidth - (Padding * 2)));

        var title = Fit(grid.Title, TitleFontSize, bold: true, contentWidth);
        var subtitle = Fit(grid.Subtitle, SubtitleFontSize, bold: false, contentWidth);
        caption = Fit(caption, CaptionFontSize, bold: false, contentWidth);

        var titleHeight = string.IsNullOrWhiteSpace(title) ? 0 : TitleFontSize + 8;
        var subtitleHeight = string.IsNullOrWhiteSpace(subtitle) ? 0 : SubtitleFontSize + 6;

        // The header takes a row of its own, and an empty result still gets one so the reader is shown an
        // empty sheet rather than a header floating over nothing.
        var bodyRowCount = Math.Max(grid.Rows.Count, 1);
        var gridHeight = BandHeight + ((bodyRowCount + 1) * RowHeight);
        var gridTop = Padding + titleHeight + subtitleHeight;
        var width = Math.Ceiling(contentWidth + (Padding * 2));
        var height = Math.Ceiling(gridTop + gridHeight + CaptionFontSize + 14 + Padding);

        var builder = new StringBuilder(4096);

        builder
            .Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"")
            .Append(Number(width))
            .Append("\" height=\"")
            .Append(Number(height))
            .Append("\" viewBox=\"0 0 ")
            .Append(Number(width))
            .Append(' ')
            .Append(Number(height))
            .Append("\" font-family=\"")
            .Append(FontFamily)
            .Append("\">");

        Rect(builder, 0, 0, width, height, PageFill, null);

        AppendClipPaths(builder, widths, shownColumnCount, Padding + gutterWidth, height);
        AppendHeadings(builder, title, subtitle);
        AppendColumnBand(builder, widths, shownColumnCount, gutterWidth, gridTop, gridWidth);
        AppendRows(builder, grid, widths, shownColumnCount, gutterWidth, gridTop, gridWidth, bodyRowCount);
        AppendGridLines(builder, widths, shownColumnCount, gutterWidth, gridTop, gridWidth, gridHeight, bodyRowCount);

        Text(
            builder,
            Padding,
            gridTop + gridHeight + CaptionFontSize + 6,
            caption,
            CaptionFontSize,
            MutedText,
            bold: false,
            anchor: null,
            clipId: -1);

        builder.Append("</svg>");

        return (builder.ToString(), shownColumnCount);
    }

    /// <summary>
    /// Sizes every column to its widest value, within bounds that keep one long cell from crowding out the
    /// rest of the sheet.
    /// </summary>
    /// <param name="grid">The grid.</param>
    /// <returns>The column widths, in order.</returns>
    private static double[] MeasureColumns(TabularPreviewGrid grid)
    {
        var widths = new double[grid.Columns.Count];

        for (var index = 0; index < grid.Columns.Count; index++)
        {
            widths[index] = MeasureText(grid.Columns[index].Header, CellFontSize, bold: true);
        }

        foreach (var row in grid.Rows)
        {
            for (var index = 0; index < widths.Length && index < row.Length; index++)
            {
                var measured = MeasureText(row[index], CellFontSize, bold: false);

                if (measured > widths[index])
                {
                    widths[index] = measured;
                }
            }
        }

        for (var index = 0; index < widths.Length; index++)
        {
            widths[index] = Math.Clamp(Math.Ceiling(widths[index] + (CellPadding * 2)), MinColumnWidth, MaxColumnWidth);
        }

        return widths;
    }

    private static double MeasureGutter(TabularPreviewGrid grid)
    {
        var lastRowNumber = (grid.Rows.Count + 1).ToString(CultureInfo.InvariantCulture);

        return Math.Max(MinGutterWidth, Math.Ceiling(MeasureText(lastRowNumber, BandFontSize, bold: false) + 16));
    }

    /// <summary>
    /// Returns how many columns fit inside the allowed width.
    /// </summary>
    /// <remarks>
    /// A picture wider than this is scaled down by the chat surface to fit its column, and past a certain
    /// width that scaling makes every cell unreadable — so the columns that do not fit are left out and
    /// counted in the caption instead of being drawn too small to read.
    /// </remarks>
    /// <param name="widths">The measured column widths.</param>
    /// <param name="gutterWidth">The width of the row-number gutter.</param>
    /// <param name="maxImageWidth">The widest the picture may be.</param>
    /// <returns>The number of leading columns to draw; always at least one.</returns>
    private static int FitColumns(double[] widths, double gutterWidth, int maxImageWidth)
    {
        var budget = Math.Max(MinColumnWidth + gutterWidth, maxImageWidth - (Padding * 2));
        var used = gutterWidth;

        for (var index = 0; index < widths.Length; index++)
        {
            used += widths[index];

            if (used > budget)
            {
                return Math.Max(1, index);
            }
        }

        return Math.Max(1, widths.Length);
    }

    /// <summary>
    /// Declares one clip region per column, so a value the width estimate got wrong is cut off at its own
    /// column edge rather than printed across the next one.
    /// </summary>
    private static void AppendClipPaths(StringBuilder builder, double[] widths, int shownColumnCount, double firstColumnX, double height)
    {
        builder.Append("<defs>");

        var x = firstColumnX;

        for (var index = 0; index < shownColumnCount; index++)
        {
            builder
                .Append("<clipPath id=\"c")
                .Append(index.ToString(CultureInfo.InvariantCulture))
                .Append("\"><rect x=\"")
                .Append(Number(x + 3))
                .Append("\" y=\"0\" width=\"")
                .Append(Number(Math.Max(1, widths[index] - 6)))
                .Append("\" height=\"")
                .Append(Number(height))
                .Append("\"/></clipPath>");

            x += widths[index];
        }

        builder.Append("</defs>");
    }

    private static void AppendHeadings(StringBuilder builder, string title, string subtitle)
    {
        var y = Padding;

        if (!string.IsNullOrWhiteSpace(title))
        {
            Text(builder, Padding, y + TitleFontSize, title, TitleFontSize, BodyText, bold: true, anchor: null, clipId: -1);
            y += TitleFontSize + 8;
        }

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            Text(builder, Padding, y + SubtitleFontSize - 2, subtitle, SubtitleFontSize, MutedText, bold: false, anchor: null, clipId: -1);
        }
    }

    /// <summary>
    /// Shortens a heading until it fits the width available to it.
    /// </summary>
    /// <param name="text">The heading.</param>
    /// <param name="fontSize">The font size it is drawn at.</param>
    /// <param name="bold">Whether it is drawn bold.</param>
    /// <param name="maxWidth">The width it has to fit into.</param>
    /// <returns>The heading, or as much of it as fits followed by an ellipsis.</returns>
    private static string Fit(string text, double fontSize, bool bold, double maxWidth)
    {
        if (string.IsNullOrEmpty(text) || MeasureText(text, fontSize, bold) <= maxWidth)
        {
            return text;
        }

        var length = text.Length;

        while (length > 1 && MeasureText(string.Concat(text.AsSpan(0, length), "…"), fontSize, bold) > maxWidth)
        {
            length--;
        }

        return string.Concat(text.AsSpan(0, length).TrimEnd(), "…");
    }

    /// <summary>
    /// Draws the lettered band across the top — one of the two things that make a grid read as a
    /// spreadsheet rather than as a table.
    /// </summary>
    private static void AppendColumnBand(
        StringBuilder builder,
        double[] widths,
        int shownColumnCount,
        double gutterWidth,
        double gridTop,
        double gridWidth)
    {
        Rect(builder, Padding, gridTop, gridWidth, BandHeight, BandFill, null);

        var x = Padding + gutterWidth;

        for (var index = 0; index < shownColumnCount; index++)
        {
            Text(
                builder,
                x + (widths[index] / 2),
                gridTop + BandHeight - 5,
                ToColumnLetter(index),
                BandFontSize,
                BandText,
                bold: false,
                anchor: "middle",
                clipId: -1);

            x += widths[index];
        }
    }

    private static void AppendRows(
        StringBuilder builder,
        TabularPreviewGrid grid,
        double[] widths,
        int shownColumnCount,
        double gutterWidth,
        double gridTop,
        double gridWidth,
        int bodyRowCount)
    {
        var headerTop = gridTop + BandHeight;

        // The gutter is painted for the whole grid in one go, so the row numbers sit on an unbroken band the
        // way they do in a spreadsheet instead of on a stripe that changes colour with the rows.
        // The header's colours and the row banding are taken from the formatting the workbook is written
        // with, so the picture is of the file the reader downloads rather than of a palette only the
        // preview knows about. Where nothing is recorded these fall back to the writer's own defaults.
        var headerFill = ResolveColor(grid.Formatting?.HeaderStyle?.BackgroundColor, DefaultHeaderFill);
        var headerText = ResolveColor(grid.Formatting?.HeaderStyle?.FontColor, DefaultHeaderText);
        var banded = grid.Formatting?.BandedRows == true;
        var bandFill = ResolveColor(grid.Formatting?.BandColor, DefaultBandFill);

        Rect(builder, Padding, headerTop, gutterWidth, (bodyRowCount + 1) * RowHeight, BandFill, null);
        Rect(builder, Padding + gutterWidth, headerTop, gridWidth - gutterWidth, RowHeight, headerFill, null);

        AppendRowNumber(builder, 1, gutterWidth, headerTop);

        var x = Padding + gutterWidth;

        for (var index = 0; index < shownColumnCount; index++)
        {
            AppendCell(builder, grid.Columns[index].Header, x, widths[index], headerTop, headerText, bold: true, rightAlign: false, clipId: index);
            x += widths[index];
        }

        for (var rowIndex = 0; rowIndex < grid.Rows.Count; rowIndex++)
        {
            var top = headerTop + ((rowIndex + 1) * RowHeight);
            var row = grid.Rows[rowIndex];

            if (banded && rowIndex % 2 == 1)
            {
                Rect(builder, Padding + gutterWidth, top, gridWidth - gutterWidth, RowHeight, bandFill, null);
            }

            AppendRowNumber(builder, rowIndex + 2, gutterWidth, top);

            x = Padding + gutterWidth;

            for (var index = 0; index < shownColumnCount; index++)
            {
                var value = index < row.Length ? row[index] : string.Empty;

                AppendCell(builder, value, x, widths[index], top, BodyText, bold: false, rightAlign: grid.Columns[index].IsNumeric, clipId: index);
                x += widths[index];
            }
        }

        if (grid.Rows.Count == 0)
        {
            Text(
                builder,
                Padding + gutterWidth + CellPadding,
                headerTop + RowHeight + (RowHeight / 2) + 4,
                "This table has no rows.",
                CellFontSize,
                MutedText,
                bold: false,
                anchor: null,
                clipId: -1);
        }
    }

    private static void AppendRowNumber(StringBuilder builder, int number, double gutterWidth, double top)
    {
        Text(
            builder,
            Padding + gutterWidth - 6,
            top + (RowHeight / 2) + 3.5,
            number.ToString(CultureInfo.InvariantCulture),
            BandFontSize,
            BandText,
            bold: false,
            anchor: "end",
            clipId: -1);
    }

    private static void AppendCell(
        StringBuilder builder,
        string value,
        double x,
        double columnWidth,
        double top,
        string fill,
        bool bold,
        bool rightAlign,
        int clipId)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        Text(
            builder,
            rightAlign ? x + columnWidth - CellPadding : x + CellPadding,
            top + (RowHeight / 2) + 4,
            value,
            CellFontSize,
            fill,
            bold,
            rightAlign ? "end" : null,
            clipId);
    }

    private static void AppendGridLines(
        StringBuilder builder,
        double[] widths,
        int shownColumnCount,
        double gutterWidth,
        double gridTop,
        double gridWidth,
        double gridHeight,
        int bodyRowCount)
    {
        builder
            .Append("<g stroke=\"")
            .Append(GridLine)
            .Append("\" stroke-width=\"1\" shape-rendering=\"crispEdges\">");

        var bodyTop = gridTop + BandHeight + RowHeight;

        for (var rowIndex = 0; rowIndex <= bodyRowCount; rowIndex++)
        {
            var y = bodyTop + (rowIndex * RowHeight);

            Line(builder, Padding, y, Padding + gridWidth, y, null);
        }

        var x = Padding + gutterWidth;

        Line(builder, x, gridTop, x, gridTop + gridHeight, null);

        for (var index = 0; index < shownColumnCount; index++)
        {
            x += widths[index];

            Line(builder, x, gridTop, x, gridTop + gridHeight, null);
        }

        builder.Append("</g>");

        Rect(builder, Padding, gridTop, gridWidth, gridHeight, "none", OuterLine);
        Line(builder, Padding, gridTop + BandHeight, Padding + gridWidth, gridTop + BandHeight, OuterLine);
    }

    private static void Rect(StringBuilder builder, double x, double y, double width, double height, string fill, string stroke)
    {
        builder
            .Append("<rect x=\"")
            .Append(Number(x))
            .Append("\" y=\"")
            .Append(Number(y))
            .Append("\" width=\"")
            .Append(Number(width))
            .Append("\" height=\"")
            .Append(Number(height))
            .Append("\" fill=\"")
            .Append(fill);

        if (!string.IsNullOrEmpty(stroke))
        {
            builder
                .Append("\" stroke=\"")
                .Append(stroke)
                .Append("\" stroke-width=\"1");
        }

        builder.Append("\" shape-rendering=\"crispEdges\"/>");
    }

    private static void Line(StringBuilder builder, double x1, double y1, double x2, double y2, string stroke)
    {
        builder
            .Append("<line x1=\"")
            .Append(Number(x1))
            .Append("\" y1=\"")
            .Append(Number(y1))
            .Append("\" x2=\"")
            .Append(Number(x2))
            .Append("\" y2=\"")
            .Append(Number(y2));

        if (!string.IsNullOrEmpty(stroke))
        {
            builder
                .Append("\" stroke=\"")
                .Append(stroke)
                .Append("\" stroke-width=\"1\" shape-rendering=\"crispEdges");
        }

        builder.Append("\"/>");
    }

    private static void Text(
        StringBuilder builder,
        double x,
        double y,
        string value,
        double fontSize,
        string fill,
        bool bold,
        string anchor,
        int clipId)
    {
        builder
            .Append("<text x=\"")
            .Append(Number(x))
            .Append("\" y=\"")
            .Append(Number(y))
            .Append("\" font-size=\"")
            .Append(Number(fontSize))
            .Append("\" fill=\"")
            .Append(fill);

        if (bold)
        {
            builder.Append("\" font-weight=\"600");
        }

        if (!string.IsNullOrEmpty(anchor))
        {
            builder
                .Append("\" text-anchor=\"")
                .Append(anchor);
        }

        if (clipId >= 0)
        {
            builder
                .Append("\" clip-path=\"url(#c")
                .Append(clipId.ToString(CultureInfo.InvariantCulture))
                .Append(')');
        }

        builder.Append("\">");
        AppendEscaped(builder, value);
        builder.Append("</text>");
    }

    /// <summary>
    /// Writes a number the way SVG reads it: invariant, and without a trailing run of zeros on every
    /// coordinate in the file.
    /// </summary>
    private static string Number(double value)
    {
        return Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Escapes the characters that would otherwise close an element or an attribute early.
    /// </summary>
    /// <remarks>
    /// Cell values are other people's data. A value containing <c>&lt;/text&gt;</c> written straight into
    /// the markup would not merely break the picture: it is served from this host's own origin, so it has
    /// to be markup this host wrote all of.
    /// </remarks>
    private static void AppendEscaped(StringBuilder builder, string value)
    {
        foreach (var character in value)
        {
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
                    // A control character is not legal XML content and would make the whole document
                    // unparseable, so it is dropped rather than carried into the file.
                    if (character >= ' ')
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Returns the spreadsheet letter for a column position: A, B, … Z, AA, AB, and so on.
    /// </summary>
    /// <param name="index">The zero-based column position.</param>
    /// <returns>The column letter.</returns>
    private static string ToColumnLetter(int index)
    {
        Span<char> letters = stackalloc char[8];
        var position = letters.Length;
        var remaining = index;

        do
        {
            letters[--position] = (char)('A' + (remaining % 26));
            remaining = (remaining / 26) - 1;
        }
        while (remaining >= 0 && position > 0);

        return new string(letters[position..]);
    }

    /// <summary>
    /// Estimates how wide a string will be drawn.
    /// </summary>
    /// <remarks>
    /// There is no font to measure against here, so the widths are per-character approximations for a
    /// typical UI sans-serif. They only decide column widths, and every cell is clipped to its column
    /// anyway, so an estimate that runs a little wide costs some whitespace and an estimate that runs a
    /// little narrow costs the last character or two — neither can spill one column into the next.
    /// </remarks>
    /// <param name="text">The text.</param>
    /// <param name="fontSize">The font size it is drawn at.</param>
    /// <param name="bold">Whether it is drawn bold.</param>
    /// <returns>The estimated width in pixels.</returns>
    private static double MeasureText(string text, double fontSize, bool bold)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var units = 0d;

        foreach (var character in text)
        {
            units += CharacterWidth(character);
        }

        return units * fontSize * (bold ? 1.06 : 1);
    }

    private static double CharacterWidth(char character)
    {
        if (char.IsAsciiDigit(character))
        {
            return 0.56;
        }

        return character switch
        {
            ' ' => 0.27,
            'i' or 'j' or 'l' or 'I' or '.' or ',' or ':' or ';' or '\'' or '|' or '!' or '`' => 0.28,
            'f' or 't' or 'r' or '(' or ')' or '[' or ']' or '{' or '}' or '/' or '\\' or '-' => 0.36,
            'm' or 'w' => 0.85,
            'M' or 'W' => 0.92,
            '@' or '%' => 0.95,
            >= 'A' and <= 'Z' => 0.66,
            >= 'a' and <= 'z' => 0.53,
            _ => 0.56,
        };
    }
}
