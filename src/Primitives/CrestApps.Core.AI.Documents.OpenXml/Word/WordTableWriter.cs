using System.Globalization;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.Tabular;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.OpenXml.Word;

/// <summary>
/// Writes tables: a header row that repeats on every page, banded rows, column widths, merged cells, and
/// values presented with the same number formats the spreadsheet export and the tabular preview use.
/// </summary>
internal static class WordTableWriter
{
    /// <summary>
    /// The full width of a table, in fiftieths of a percent.
    /// </summary>
    public const int FullWidthPercent = 5000;

    /// <summary>
    /// Builds a table.
    /// </summary>
    /// <param name="mainPart">The main document part, whose styles the table uses.</param>
    /// <param name="part">The part the table is placed in, which owns any hyperlink relationship.</param>
    /// <param name="spec">The table.</param>
    /// <param name="design">The design styles are added with when the document lacks them.</param>
    /// <param name="textWidthTwips">The width between the margins, in twips.</param>
    /// <returns>The table.</returns>
    public static Table Create(MainDocumentPart mainPart, OpenXmlPart part, WordTableSpec spec, WordDesign design, int textWidthTwips)
    {
        ArgumentNullException.ThrowIfNull(mainPart);
        ArgumentNullException.ThrowIfNull(spec);

        design ??= new WordDesign();

        var columnCount = CountColumns(spec);
        var columns = Enumerable.Range(0, columnCount)
            .Select(index => index < spec.Columns.Count ? spec.Columns[index] ?? new WordTableColumn() : new WordTableColumn())
            .ToList();

        var hasHeader = spec.HeaderRow && columns.Any(column => !string.IsNullOrWhiteSpace(column.Header));
        var tableWidth = ResolveTableWidth(spec.Width, textWidthTwips, out var tableWidthElement);
        var gridWidths = ResolveColumnWidths(columns, tableWidth);
        var look = (spec.Style ?? "data").Trim().ToLowerInvariant();

        var table = new Table();
        var properties = new TableProperties
        {
            TableWidth = tableWidthElement,
        };

        var banded = spec.Banded ?? (look is "data" or "striped" or "banded");

        switch (look)
        {
            case "data":
            case "striped":
            case "banded":
            case "default":
                properties.TableStyle = new TableStyle { Val = WordStyleSheet.Ensure(mainPart, WordStyleSheet.DataTable, design) };

                break;

            case "grid":
            case "bordered":
                properties.TableStyle = new TableStyle { Val = WordStyleSheet.Ensure(mainPart, WordStyleSheet.TableGrid, design) };

                break;

            case "light":
            case "minimal":
            case "lines":
                properties.TableStyle = new TableStyle { Val = WordStyleSheet.Ensure(mainPart, WordStyleSheet.TableGrid, design) };
                properties.TableBorders = CreateBorders(spec.BorderColor ?? design.TableBorderColor, verticals: false, outerSides: false);

                break;

            case "plain":
            case "none":
            case "borderless":
                properties.TableStyle = new TableStyle { Val = WordStyleSheet.Ensure(mainPart, WordStyleSheet.TableGrid, design) };
                properties.TableBorders = CreateNoBorders();

                break;

            default:
                properties.TableStyle = new TableStyle
                {
                    Val = WordStyleSheet.Find(mainPart, spec.Style, StyleValues.Table) ?? WordStyleSheet.Ensure(mainPart, WordStyleSheet.DataTable, design),
                };

                break;
        }

        if (!string.IsNullOrWhiteSpace(spec.BorderColor) && properties.TableBorders is null && WordColor.TryParse(spec.BorderColor, out var borderColor))
        {
            properties.TableBorders = CreateBorders(borderColor, verticals: true, outerSides: true);
        }

        if (ReadAlignment(spec.Alignment) is { } alignment)
        {
            properties.TableJustification = new TableJustification
            {
                Val = alignment == JustificationValues.Center
                    ? TableRowAlignmentValues.Center
                    : alignment == JustificationValues.Right ? TableRowAlignmentValues.Right : TableRowAlignmentValues.Left,
            };
        }

        var explicitBandFill = !string.IsNullOrWhiteSpace(spec.BandFill) && WordColor.TryParse(spec.BandFill, out _);

        // The look is written as the original bit mask (first row 0x20, no horizontal band 0x200, no vertical
        // band 0x400), which every version reads; the named attributes are a later addition. A band color given
        // explicitly is painted on the cells, so the style's own band is switched off.
        var lookMask = 0x0400 | (hasHeader ? 0x0020 : 0) | (!banded || explicitBandFill ? 0x0200 : 0);

        properties.TableLook = new TableLook { Val = lookMask.ToString("X4", CultureInfo.InvariantCulture) };

        table.Append(properties);

        var grid = new TableGrid();

        foreach (var width in gridWidths)
        {
            grid.Append(new GridColumn { Width = width.ToString(CultureInfo.InvariantCulture) });
        }

        table.Append(grid);

        if (hasHeader)
        {
            var headerRow = new TableRow(new TableRowProperties(new CantSplit()));

            if (spec.RepeatHeaderRow)
            {
                headerRow.TableRowProperties.Append(new TableHeader());
            }

            for (var index = 0; index < columnCount; index++)
            {
                var cell = new WordTableCell
                {
                    Text = columns[index].Header ?? string.Empty,
                    Bold = look is "data" or "striped" or "banded" or "default" ? null : true,
                    Fill = spec.HeaderFill ?? (look is "grid" or "bordered" ? "F2F2F2" : null),
                    Color = spec.HeaderTextColor,
                    Alignment = columns[index].Alignment ?? (IsNumeric(columns[index].Format) ? "right" : null),
                };

                headerRow.Append(CreateCell(part, cell, gridWidths[index], spec, columnFormat: null, defaultAlignment: null, boldOverride: false));
            }

            table.Append(headerRow);
        }

        var pendingRowSpans = new int[columnCount];

        for (var rowIndex = 0; rowIndex < spec.Rows.Count; rowIndex++)
        {
            var source = spec.Rows[rowIndex] ?? [];
            var row = new TableRow();
            var bandFill = explicitBandFill && banded && rowIndex % 2 == 1 ? spec.BandFill : null;
            var sourceIndex = 0;
            var column = 0;

            while (column < columnCount)
            {
                if (pendingRowSpans[column] > 0)
                {
                    pendingRowSpans[column]--;

                    var continuation = new TableCell(
                        new TableCellProperties
                        {
                            TableCellWidth = new TableCellWidth { Width = gridWidths[column].ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
                            VerticalMerge = new VerticalMerge(),
                        },
                        new Paragraph());

                    row.Append(continuation);
                    column++;

                    continue;
                }

                var cell = sourceIndex < source.Count ? source[sourceIndex] ?? new WordTableCell() : new WordTableCell();

                sourceIndex++;

                var span = Math.Clamp(cell.ColumnSpan, 1, columnCount - column);
                var width = gridWidths.Skip(column).Take(span).Sum();
                var columnSpec = columns[column];

                if (bandFill is not null && string.IsNullOrWhiteSpace(cell.Fill))
                {
                    cell.Fill = bandFill;
                }

                var element = CreateCell(
                    part,
                    cell,
                    width,
                    spec,
                    columnSpec.Format,
                    columnSpec.Alignment ?? (IsNumeric(columnSpec.Format) ? "right" : null),
                    boldOverride: spec.BoldFirstColumn && column == 0);

                if (span > 1)
                {
                    element.TableCellProperties.GridSpan = new GridSpan { Val = span };
                }

                if (cell.RowSpan > 1)
                {
                    element.TableCellProperties.VerticalMerge = new VerticalMerge { Val = MergedCellValues.Restart };

                    for (var spanned = column; spanned < column + span; spanned++)
                    {
                        pendingRowSpans[spanned] = Math.Max(pendingRowSpans[spanned], cell.RowSpan - 1);
                    }

                    // A merged cell covering several grid columns continues as one cell below, so the extra
                    // columns are skipped there rather than given cells of their own.
                    for (var spanned = column + 1; spanned < column + span; spanned++)
                    {
                        pendingRowSpans[spanned] = 0;
                    }
                }

                row.Append(element);
                column += span;
            }

            table.Append(row);
        }

        return table;
    }

    /// <summary>
    /// Presents a value under a column format, the way the spreadsheet export and the preview present it.
    /// </summary>
    /// <param name="value">The value, or text holding one.</param>
    /// <param name="format">The column format, or <see langword="null"/>.</param>
    /// <returns>The text to show.</returns>
    public static string FormatValue(object value, SpreadsheetColumnFormat format)
    {
        if (value is string text && (format is null || string.IsNullOrWhiteSpace(text)))
        {
            return text;
        }

        return TabularPreviewValueFormat.Format(value, format);
    }

    private static TableCell CreateCell(
        OpenXmlPart part,
        WordTableCell cell,
        int widthTwips,
        WordTableSpec spec,
        SpreadsheetColumnFormat columnFormat,
        string defaultAlignment,
        bool boldOverride)
    {
        var properties = new TableCellProperties
        {
            TableCellWidth = new TableCellWidth { Width = widthTwips.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
        };

        if (!string.IsNullOrWhiteSpace(cell.Fill) && WordColor.TryParse(cell.Fill, out var fill))
        {
            properties.Shading = new Shading { Val = ShadingPatternValues.Clear, Fill = fill, Color = "auto" };
        }

        var vertical = (cell.VerticalAlignment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "center" or "middle" => TableVerticalAlignmentValues.Center,
            "bottom" => TableVerticalAlignmentValues.Bottom,
            "top" => TableVerticalAlignmentValues.Top,
            _ => (TableVerticalAlignmentValues?)null,
        };

        if (vertical is not null)
        {
            properties.TableCellVerticalAlignment = new TableCellVerticalAlignment { Val = vertical.Value };
        }

        var paragraph = new Paragraph();

        if (ReadAlignment(cell.Alignment ?? defaultAlignment) is { } justification)
        {
            paragraph.ParagraphProperties = new ParagraphProperties { Justification = new Justification { Val = justification } };
        }

        var format = new WordRunFormat
        {
            Bold = cell.Bold ?? (boldOverride ? true : null),
            Italic = cell.Italic,
            Color = string.IsNullOrWhiteSpace(cell.Color) ? null : cell.Color,
            Size = spec.FontSize,
        };

        var text = cell.Value is not null
            ? FormatValue(cell.Value, columnFormat)
            : columnFormat is not null && LooksLikeValue(cell.Text) ? FormatValue(cell.Text.Trim(), columnFormat) : cell.Text;

        WordInlineWriter.AppendMarkdown(paragraph, text ?? string.Empty, part, format.IsEmpty ? null : format);

        return new TableCell(properties, paragraph);
    }

    private static bool LooksLikeValue(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ||
            DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _);
    }

    private static bool IsNumeric(SpreadsheetColumnFormat format)
    {
        return format is not null && SpreadsheetNumberFormatCode.IsNumeric(format);
    }

    private static int CountColumns(WordTableSpec spec)
    {
        var count = spec.Columns.Count;

        foreach (var row in spec.Rows)
        {
            if (row is null)
            {
                continue;
            }

            count = Math.Max(count, row.Sum(cell => Math.Max(1, cell?.ColumnSpan ?? 1)));
        }

        return Math.Max(1, count);
    }

    private static int ResolveTableWidth(string width, int textWidthTwips, out TableWidth element)
    {
        var text = width?.Trim();

        if (string.Equals(text, "auto", StringComparison.OrdinalIgnoreCase))
        {
            element = new TableWidth { Width = "0", Type = TableWidthUnitValues.Auto };

            return textWidthTwips;
        }

        if (!string.IsNullOrEmpty(text) && text.EndsWith('%') &&
            double.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            percent = Math.Clamp(percent, 5, 100);
            element = new TableWidth { Width = ((int)Math.Round(percent * 50)).ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Pct };

            return (int)Math.Round(textWidthTwips * percent / 100);
        }

        if (WordUnits.TryParseLength(text, WordUnits.FromTwips(textWidthTwips), out var points) && points > 0)
        {
            var twips = Math.Min(WordUnits.ToTwips(points), textWidthTwips * 2);

            element = new TableWidth { Width = twips.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa };

            return twips;
        }

        element = new TableWidth { Width = FullWidthPercent.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Pct };

        return textWidthTwips;
    }

    private static int[] ResolveColumnWidths(List<WordTableColumn> columns, int tableWidth)
    {
        var widths = new double[columns.Count];
        var weights = new double[columns.Count];
        var fixedTotal = 0d;
        var weightTotal = 0d;

        for (var index = 0; index < columns.Count; index++)
        {
            var text = columns[index].Width?.Trim();

            if (string.IsNullOrEmpty(text))
            {
                weights[index] = 1;
            }
            else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
            {
                weights[index] = Math.Max(weight, 0.1);
            }
            else if (WordUnits.TryParseLength(text, WordUnits.FromTwips(tableWidth), out var points) && points > 0)
            {
                widths[index] = WordUnits.ToTwips(points);
                fixedTotal += widths[index];
            }
            else
            {
                weights[index] = 1;
            }

            weightTotal += weights[index];
        }

        var remaining = Math.Max(0, tableWidth - fixedTotal);

        if (fixedTotal > tableWidth && fixedTotal > 0)
        {
            // Widths that add up to more than the table are scaled down rather than pushed past the margin.
            for (var index = 0; index < widths.Length; index++)
            {
                widths[index] = widths[index] * tableWidth / fixedTotal;
            }

            remaining = 0;
        }

        for (var index = 0; index < widths.Length; index++)
        {
            if (weights[index] > 0)
            {
                widths[index] = weightTotal > 0 ? remaining * weights[index] / weightTotal : 0;
            }
        }

        return [.. widths.Select(width => Math.Max(360, (int)Math.Round(width)))];
    }

    private static TableBorders CreateBorders(string color, bool verticals, bool outerSides)
    {
        var value = WordColor.ParseOrDefault(color, "BFBFBF");

        return new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 6U, Space = 0U, Color = value },
            Border<LeftBorder>(outerSides ? BorderValues.Single : BorderValues.Nil, value),
            new BottomBorder { Val = BorderValues.Single, Size = 6U, Space = 0U, Color = value },
            Border<RightBorder>(outerSides ? BorderValues.Single : BorderValues.Nil, value),
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = value },
            new InsideVerticalBorder { Val = verticals ? BorderValues.Single : BorderValues.Nil, Size = 4U, Space = 0U, Color = value });
    }

    private static TableBorders CreateNoBorders()
    {
        return new TableBorders(
            new TopBorder { Val = BorderValues.Nil },
            new LeftBorder { Val = BorderValues.Nil },
            new BottomBorder { Val = BorderValues.Nil },
            new RightBorder { Val = BorderValues.Nil },
            new InsideHorizontalBorder { Val = BorderValues.Nil },
            new InsideVerticalBorder { Val = BorderValues.Nil });
    }

    private static T Border<T>(BorderValues value, string color)
        where T : BorderType, new()
    {
        return new T { Val = value, Size = 6U, Space = 0U, Color = color };
    }

    /// <summary>
    /// Reads an alignment a model wrote.
    /// </summary>
    /// <param name="value">The alignment, such as <c>left</c>, <c>center</c>, <c>right</c> or <c>justify</c>.</param>
    /// <returns>The alignment, or <see langword="null"/> when none was given.</returns>
    public static JustificationValues? ReadAlignment(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "center" or "centre" or "middle" => JustificationValues.Center,
            "right" or "end" => JustificationValues.Right,
            "justify" or "justified" or "both" => JustificationValues.Both,
            "left" or "start" => JustificationValues.Left,
            _ => null,
        };
    }
}
