using System.Globalization;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

internal sealed partial class PdfMigraDocBuilder
{
    private void AddTable(Section section, PdfTableDefinition definition, PdfBlockDefinition block)
    {
        var sourceRows = definition.Rows ?? [];
        var columnCount = Math.Max(definition.Columns?.Count ?? 0, sourceRows.Count == 0 ? 0 : sourceRows.Max(row => row?.Count ?? 0));

        if (columnCount == 0)
        {
            _warnings.Add($"Table block {block?.Id} has no columns.");

            return;
        }

        var rows = sourceRows;

        if (rows.Count > _options.MaxTableRows)
        {
            _warnings.Add($"A table held {rows.Count:N0} rows; the first {_options.MaxTableRows:N0} were laid out.");
            rows = [.. sourceRows.Take(_options.MaxTableRows)];
        }

        var readOnlyRows = rows.Select(row => (IReadOnlyList<string>)(row ?? [])).ToList();
        var columns = new PdfTableColumnDefinition[columnCount];
        var formats = new SpreadsheetColumnFormat[columnCount];
        var numeric = new bool[columnCount];

        for (var index = 0; index < columnCount; index++)
        {
            columns[index] = index < (definition.Columns?.Count ?? 0) && definition.Columns[index] is not null
                ? definition.Columns[index]
                : new PdfTableColumnDefinition();

            formats[index] = PdfCellValues.ToSpreadsheetFormat(columns[index]);
            numeric[index] = (formats[index] is not null && SpreadsheetNumberFormatCode.IsNumeric(formats[index])) ||
                PdfCellValues.IsNumericColumn(readOnlyRows, index);
        }

        var widths = MeasureColumns(columns, readOnlyRows, _page.UsableWidth);
        var fontSize = definition.FontSize is > 0
            ? Math.Clamp(definition.FontSize.Value, 4, 18)
            : _theme.TableFontSize ?? AutoTableFontSize(columnCount);

        var headerBackground = PdfResolvedTheme.ReadColor(definition.HeaderBackground, _theme.TableHeaderBackground, "header_background", _warnings);
        var headerText = PdfResolvedTheme.ReadColor(
            definition.HeaderTextColor,
            definition.HeaderBackground is null ? _theme.TableHeaderText : headerBackground.IsLight ? _theme.Text : _white,
            "header_text_color",
            _warnings);
        var borderColor = PdfResolvedTheme.ReadColor(definition.BorderColor, _theme.TableBorder, "border_color", _warnings);
        var bandColor = PdfResolvedTheme.ReadColor(definition.BandColor, _theme.TableBand, "band_color", _warnings);
        var banded = definition.Banded ?? _theme.TableBanded;
        var borders = (definition.Borders ?? _theme.TableBorders)?.Trim().ToLowerInvariant();
        var rules = BuildHighlightRules(definition.HighlightRules, columns, readOnlyRows);

        var table = section.AddTable();

        table.Format.Font.Size = fontSize;
        table.Format.SpaceBefore = 0;
        table.Format.SpaceAfter = 0;
        table.Format.LineSpacingRule = LineSpacingRule.Single;
        table.Rows.LeftIndent = 0;
        table.LeftPadding = Unit.FromPoint(Math.Max(2, fontSize * 0.45));
        table.RightPadding = Unit.FromPoint(Math.Max(2, fontSize * 0.45));
        table.TopPadding = Unit.FromPoint(Math.Max(1.5, fontSize * 0.25));
        table.BottomPadding = Unit.FromPoint(Math.Max(1.5, fontSize * 0.25));

        if (borders == "all")
        {
            table.Borders.Width = 0.5;
            table.Borders.Color = borderColor.ToMigraDoc();
        }
        else
        {
            table.Borders.Visible = false;
        }

        for (var index = 0; index < columnCount; index++)
        {
            var column = table.AddColumn(Unit.FromPoint(widths[index]));

            column.Format.Alignment = ReadAlignment(columns[index].Align, numeric[index] ? ParagraphAlignment.Right : ParagraphAlignment.Left);
        }

        if (columns.Any(column => !string.IsNullOrWhiteSpace(column.Header)))
        {
            var header = table.AddRow();

            header.HeadingFormat = definition.RepeatHeader != false;
            header.Shading.Color = headerBackground.ToMigraDoc();
            header.Format.Font.Bold = true;
            header.Format.Font.Color = headerText.ToMigraDoc();
            header.VerticalAlignment = VerticalAlignment.Center;

            if (borders == "horizontal")
            {
                header.Borders.Bottom.Width = 1;
                header.Borders.Bottom.Color = headerBackground.ToMigraDoc();
            }

            for (var index = 0; index < columnCount; index++)
            {
                AddInline(header.Cells[index].AddParagraph(), columns[index].Header ?? string.Empty, null, null);
            }
        }

        for (var rowIndex = 0; rowIndex < readOnlyRows.Count; rowIndex++)
        {
            var source = readOnlyRows[rowIndex];
            var row = table.AddRow();

            row.VerticalAlignment = VerticalAlignment.Center;

            if (banded && rowIndex % 2 == 1)
            {
                row.Shading.Color = bandColor.ToMigraDoc();
            }

            if (borders == "horizontal")
            {
                row.Borders.Bottom.Width = 0.5;
                row.Borders.Bottom.Color = borderColor.ToMigraDoc();
            }

            for (var index = 0; index < columnCount; index++)
            {
                var raw = index < source.Count ? source[index] : string.Empty;
                var cell = row.Cells[index];
                var paragraph = cell.AddParagraph(PdfCellValues.Format(raw, formats[index]));

                ApplyColumnStyle(cell, paragraph, columns[index], formats[index], raw);

                foreach (var rule in rules)
                {
                    if (rule.ColumnIndex == index)
                    {
                        rule.Apply(cell, paragraph, raw);
                    }
                }
            }
        }

        if (definition.TotalRow is not null && readOnlyRows.Count > 0)
        {
            AddTotalRow(table, definition.TotalRow, columns, formats, readOnlyRows, borders);
        }

        if (definition.SourceRowCount is > 0 && definition.SourceRowCount > readOnlyRows.Count)
        {
            var note = section.AddParagraph();
            note.Style = CaptionStyle;
            note.Format.Alignment = ParagraphAlignment.Left;
            note.AddText(string.Create(CultureInfo.InvariantCulture, $"Showing {readOnlyRows.Count:N0} of {definition.SourceRowCount:N0} rows."));
        }

        if (string.IsNullOrWhiteSpace(definition.Caption))
        {
            AddSpacer(section, block?.SpaceAfter ?? 8);
        }
        else
        {
            AddCaption(section, definition.Caption);
        }
    }

    private void ApplyColumnStyle(Cell cell, Paragraph paragraph, PdfTableColumnDefinition column, SpreadsheetColumnFormat format, string raw)
    {
        if (column.Bold == true)
        {
            paragraph.Format.Font.Bold = true;
        }

        if (!string.IsNullOrWhiteSpace(column.Color))
        {
            paragraph.Format.Font.Color = PdfResolvedTheme.ReadColor(column.Color, _theme.Text, "color", _warnings).ToMigraDoc();
        }

        if (!string.IsNullOrWhiteSpace(column.BackgroundColor))
        {
            cell.Shading.Color = PdfResolvedTheme.ReadColor(column.BackgroundColor, _white, "background_color", _warnings).ToMigraDoc();
        }

        if ((column.NegativesInRed == true || format?.NegativesInRed == true) &&
            PdfCellValues.TryParseNumber(raw, percentAsFraction: false, out var number) &&
            number < 0)
        {
            paragraph.Format.Font.Color = _negativeRed.ToMigraDoc();
        }
    }

    private void AddTotalRow(
        Table table,
        PdfTotalRowDefinition totalRow,
        PdfTableColumnDefinition[] columns,
        SpreadsheetColumnFormat[] formats,
        List<IReadOnlyList<string>> rows,
        string borders)
    {
        var row = table.AddRow();

        row.Format.Font.Bold = true;
        row.Shading.Color = _theme.Primary.Blend(_white, 0.88).ToMigraDoc();
        row.Borders.Top.Visible = true;
        row.Borders.Top.Width = 1.25;
        row.Borders.Top.Color = _theme.Primary.ToMigraDoc();
        row.VerticalAlignment = VerticalAlignment.Center;

        if (borders != "all")
        {
            row.Borders.Bottom.Visible = false;
        }

        var labelIndex = -1;

        for (var index = 0; index < columns.Length; index++)
        {
            var function = FindFunction(totalRow.Functions, columns[index].Header);

            if (function is null)
            {
                if (labelIndex < 0)
                {
                    labelIndex = index;
                }

                continue;
            }

            var isPercent = formats[index]?.NumberFormat == SpreadsheetNumberFormat.Percent;
            var value = PdfCellValues.Aggregate(rows, index, function, isPercent);

            if (value is null)
            {
                continue;
            }

            var text = function.Equals("count", StringComparison.OrdinalIgnoreCase)
                ? value.Value.ToString("N0", CultureInfo.InvariantCulture)
                : PdfCellValues.Format(PdfCellValues.ToInvariant(value.Value), formats[index] ?? new SpreadsheetColumnFormat
                {
                    NumberFormat = SpreadsheetNumberFormat.Number,
                    Decimals = rows.Any(source => index < source.Count && (source[index]?.Contains('.', StringComparison.Ordinal) ?? false)) ? 2 : 0,
                });

            row.Cells[index].AddParagraph(text);
        }

        row.Cells[Math.Max(labelIndex, 0)].AddParagraph(string.IsNullOrWhiteSpace(totalRow.Label) ? "Total" : totalRow.Label);
    }

    private static string FindFunction(Dictionary<string, string> functions, string header)
    {
        if (functions is null || string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        foreach (var (column, function) in functions)
        {
            if (SpreadsheetFormatting.NameMatches(column, header) && !string.IsNullOrWhiteSpace(function))
            {
                return function.Trim();
            }
        }

        return null;
    }

    private static double AutoTableFontSize(int columnCount)
    {
        return columnCount switch
        {
            <= 5 => 9.5,
            <= 8 => 8.5,
            <= 11 => 7.5,
            <= 15 => 6.5,
            _ => 5.5,
        };
    }

    /// <summary>
    /// Shares the table width out between the columns: as the definition asks, or by how much each column
    /// holds, so a narrow code column does not take the same width as a long description.
    /// </summary>
    private static double[] MeasureColumns(PdfTableColumnDefinition[] columns, List<IReadOnlyList<string>> rows, double tableWidth)
    {
        var weights = new double[columns.Length];

        for (var index = 0; index < columns.Length; index++)
        {
            if (columns[index].Width is > 0)
            {
                weights[index] = -columns[index].Width.Value;

                continue;
            }

            var longest = Math.Max(6, (columns[index].Header ?? string.Empty).Length * 0.8);
            var sampled = 0;
            var total = 0d;

            foreach (var row in rows)
            {
                if (index < row.Count && row[index] is { Length: > 0 } value)
                {
                    longest = Math.Max(longest, Math.Min(value.Length, 60));
                    total += Math.Min(value.Length, 60);
                    sampled++;
                }

                if (sampled >= 200)
                {
                    break;
                }
            }

            var average = sampled == 0 ? longest : total / sampled;

            weights[index] = Math.Clamp((longest * 0.6) + (average * 0.4), 4, 45);
        }

        var fixedPercent = weights.Where(weight => weight < 0).Sum(weight => -weight);
        var flexibleTotal = weights.Where(weight => weight > 0).Sum();
        var remainingPercent = Math.Max(0, 100 - Math.Min(fixedPercent, 100));
        var widths = new double[columns.Length];

        for (var index = 0; index < columns.Length; index++)
        {
            var percent = weights[index] < 0
                ? -weights[index] * (fixedPercent > 100 ? 100 / fixedPercent : 1)
                : flexibleTotal > 0 ? weights[index] / flexibleTotal * remainingPercent : 0;

            widths[index] = Math.Max(18, tableWidth * percent / 100);
        }

        // Rounding and the minimum width can push the sum past the text width, which MigraDoc answers by
        // running the table off the right edge of the page.
        var sum = widths.Sum();

        if (sum > tableWidth)
        {
            for (var index = 0; index < widths.Length; index++)
            {
                widths[index] *= tableWidth / sum;
            }
        }

        return widths;
    }

    private List<HighlightRule> BuildHighlightRules(
        List<PdfHighlightRuleDefinition> definitions,
        PdfTableColumnDefinition[] columns,
        List<IReadOnlyList<string>> rows)
    {
        var rules = new List<HighlightRule>();

        foreach (var definition in definitions ?? [])
        {
            if (definition is null || string.IsNullOrWhiteSpace(definition.Column))
            {
                continue;
            }

            var index = Array.FindIndex(columns, column => SpreadsheetFormatting.NameMatches(column.Header, definition.Column));

            if (index < 0)
            {
                _warnings.Add($"A highlight rule names the column \"{definition.Column}\", which the table does not have. Columns: {string.Join(", ", columns.Select(column => column.Header))}.");

                continue;
            }

            rules.Add(HighlightRule.Create(definition, index, rows, _theme, _warnings));
        }

        return rules;
    }

    /// <summary>
    /// One highlight rule, bound to a column and ready to colour its cells.
    /// </summary>
    private sealed class HighlightRule
    {
        private string _operator;
        private double? _number;
        private double? _second;
        private string _text;
        private PdfColor? _background;
        private PdfColor? _color;
        private bool _bold;
        private PdfColor _low;
        private PdfColor _high;
        private double _minimum;
        private double _maximum;
        private HashSet<string> _duplicates;

        public int ColumnIndex { get; private init; }

        public static HighlightRule Create(
            PdfHighlightRuleDefinition definition,
            int columnIndex,
            List<IReadOnlyList<string>> rows,
            PdfResolvedTheme theme,
            List<string> warnings)
        {
            var rule = new HighlightRule
            {
                ColumnIndex = columnIndex,
            };

            rule._operator = (definition.Operator ?? "gt").Trim().ToLowerInvariant() switch
            {
                ">" or "greater_than" or "greaterthan" => "gt",
                ">=" or "greater_than_or_equal" => "gte",
                "<" or "less_than" or "lessthan" => "lt",
                "<=" or "less_than_or_equal" => "lte",
                "=" or "==" or "equals" or "equal" or "equal_to" => "eq",
                "!=" or "<>" or "not_equal" => "ne",
                "contains_text" or "text" => "contains",
                "color_scale" or "gradient" or "heatmap" or "heat_map" or "data_bar" => "scale",
                "duplicates" or "duplicate_values" => "duplicate",
                var other => other,
            };

            rule._text = definition.Value;
            rule._number = PdfCellValues.TryParseNumber(definition.Value, percentAsFraction: false, out var number) ? number : null;
            rule._second = PdfCellValues.TryParseNumber(definition.Value2, percentAsFraction: false, out var second) ? second : null;
            rule._bold = definition.Bold == true;

            if (!string.IsNullOrWhiteSpace(definition.Color))
            {
                rule._color = PdfResolvedTheme.ReadColor(definition.Color, theme.Text, "color", warnings);
            }

            if (rule._operator == "scale")
            {
                rule._high = PdfResolvedTheme.ReadColor(definition.BackgroundColor, new PdfColor(0x63, 0xBE, 0x7B), "background_color", warnings);
                rule._low = PdfResolvedTheme.ReadColor(definition.MinimumColor, new PdfColor(0xF8, 0x69, 0x6B), "minimum_color", warnings);

                var values = new List<double>();

                foreach (var row in rows)
                {
                    if (columnIndex < row.Count && PdfCellValues.TryParseNumber(row[columnIndex], percentAsFraction: false, out var value))
                    {
                        values.Add(value);
                    }
                }

                rule._minimum = values.Count == 0 ? 0 : values.Min();
                rule._maximum = values.Count == 0 ? 0 : values.Max();
            }
            else
            {
                rule._background = PdfResolvedTheme.ReadColor(
                    definition.BackgroundColor,
                    rule._color is null && !rule._bold ? new PdfColor(0xFF, 0xEB, 0x9C) : new PdfColor(255, 255, 255),
                    "background_color",
                    warnings);

                if (string.IsNullOrWhiteSpace(definition.BackgroundColor) && (rule._color is not null || rule._bold))
                {
                    rule._background = null;
                }
            }

            if (rule._operator == "duplicate")
            {
                rule._duplicates = rows
                    .Where(row => columnIndex < row.Count && !string.IsNullOrWhiteSpace(row[columnIndex]))
                    .GroupBy(row => row[columnIndex].Trim(), StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            return rule;
        }

        public void Apply(Cell cell, Paragraph paragraph, string raw)
        {
            if (_operator == "scale")
            {
                if (PdfCellValues.TryParseNumber(raw, percentAsFraction: false, out var value) && _maximum > _minimum)
                {
                    var position = (value - _minimum) / (_maximum - _minimum);

                    // Blended towards white so the text on the cell stays readable at both ends of the scale.
                    cell.Shading.Color = _low.Blend(_high, position).Blend(new PdfColor(255, 255, 255), 0.35).ToMigraDoc();
                }

                return;
            }

            if (!Matches(raw))
            {
                return;
            }

            if (_background.HasValue)
            {
                cell.Shading.Color = _background.Value.ToMigraDoc();
            }

            if (_color.HasValue)
            {
                paragraph.Format.Font.Color = _color.Value.ToMigraDoc();
            }

            if (_bold)
            {
                paragraph.Format.Font.Bold = true;
            }
        }

        private bool Matches(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            if (_operator == "contains")
            {
                return !string.IsNullOrEmpty(_text) && raw.Contains(_text, StringComparison.OrdinalIgnoreCase);
            }

            if (_operator == "duplicate")
            {
                return _duplicates?.Contains(raw.Trim()) == true;
            }

            var isNumber = PdfCellValues.TryParseNumber(raw, percentAsFraction: false, out var value);

            if (_operator == "negative")
            {
                return isNumber && value < 0;
            }

            if (_operator == "positive")
            {
                return isNumber && value > 0;
            }

            if (_operator is "eq" or "ne" && (!isNumber || !_number.HasValue))
            {
                var equal = string.Equals(raw.Trim(), _text?.Trim(), StringComparison.OrdinalIgnoreCase);

                return _operator == "eq" ? equal : !equal;
            }

            if (!isNumber || !_number.HasValue)
            {
                return false;
            }

            return _operator switch
            {
                "gt" => value > _number,
                "gte" => value >= _number,
                "lt" => value < _number,
                "lte" => value <= _number,
                "eq" => Math.Abs(value - _number.Value) < 1e-9,
                "ne" => Math.Abs(value - _number.Value) >= 1e-9,
                "between" => _second.HasValue && value >= Math.Min(_number.Value, _second.Value) && value <= Math.Max(_number.Value, _second.Value),
                _ => false,
            };
        }
    }
}
