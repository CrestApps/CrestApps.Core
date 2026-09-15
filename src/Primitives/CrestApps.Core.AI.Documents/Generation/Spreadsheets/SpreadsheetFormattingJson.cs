using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// Converts a <see cref="SpreadsheetFormatting"/> specification to and from JSON.
/// <para>
/// <see cref="Parse(JsonElement)"/> reads the snake-case shape a language model supplies and is
/// deliberately forgiving: unknown members are ignored, a number may arrive as a string, and an enum
/// may arrive in any casing or with either a dash or an underscore. A model that spells one member
/// differently should lose that one setting, never have the whole formatting request rejected.
/// <see cref="Serialize"/> and <see cref="Deserialize"/> handle the workspace's own storage, where the
/// shape is under this library's control.
/// </para>
/// </summary>
public static class SpreadsheetFormattingJson
{
    private static readonly JsonSerializerOptions _storageOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Serializes a specification for storage.
    /// </summary>
    /// <param name="formatting">The specification.</param>
    /// <returns>The serialized specification.</returns>
    public static string Serialize(SpreadsheetFormatting formatting)
    {
        ArgumentNullException.ThrowIfNull(formatting);

        return JsonSerializer.Serialize(formatting, _storageOptions);
    }

    /// <summary>
    /// Deserializes a stored specification.
    /// </summary>
    /// <param name="json">The serialized specification.</param>
    /// <returns>The specification, or <see langword="null"/> when the stored value cannot be read.</returns>
    public static SpreadsheetFormatting Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SpreadsheetFormatting>(json, _storageOptions);
        }
        catch (JsonException)
        {
            // Stored formatting is a presentation detail derived from an earlier request. If it cannot
            // be read back, the export should still produce the data rather than fail outright.
            return null;
        }
    }

    /// <summary>
    /// Reads the model-facing shape of a formatting request.
    /// </summary>
    /// <param name="element">The JSON object supplied by the caller.</param>
    /// <returns>The parsed specification.</returns>
    public static SpreadsheetFormatting Parse(JsonElement element)
    {
        var formatting = new SpreadsheetFormatting();

        if (element.ValueKind != JsonValueKind.Object)
        {
            return formatting;
        }

        if (TryGetString(element, out var sheetName, "sheet_name", "sheetName", "sheet"))
        {
            formatting.SheetName = sheetName;
        }

        if (TryGetBoolean(element, out var bandedRows, "banded_rows", "bandedRows", "zebra_stripes"))
        {
            formatting.BandedRows = bandedRows;
        }

        if (TryGetString(element, out var bandColor, "band_color", "bandColor"))
        {
            formatting.BandColor = bandColor;
        }

        ParseHeader(element, formatting);
        ParseColumns(element, formatting);
        ParseConditionalFormats(element, formatting);
        ParseTotalRow(element, formatting);
        ParseCharts(element, formatting);

        return formatting;
    }

    private static void ParseHeader(JsonElement element, SpreadsheetFormatting formatting)
    {
        if (TryGetBoolean(element, out var freezeHeader, "freeze_header", "freezeHeader", "freeze"))
        {
            formatting.FreezeHeader = freezeHeader;
        }

        if (TryGetBoolean(element, out var autoFilter, "auto_filter", "autoFilter", "filter", "filters"))
        {
            formatting.AutoFilter = autoFilter;
        }

        if (!TryGetProperty(element, out var header, "header", "header_style", "headerStyle"))
        {
            return;
        }

        if (header.ValueKind == JsonValueKind.False)
        {
            formatting.StyleHeader = false;

            return;
        }

        if (header.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        // The header object doubles as the place to put the sheet-level header switches, because a
        // model asked to style a header naturally groups freezing and filtering with it.
        if (TryGetBoolean(header, out var nestedFreeze, "freeze", "freeze_header", "freezeHeader"))
        {
            formatting.FreezeHeader = nestedFreeze;
        }

        if (TryGetBoolean(header, out var nestedFilter, "filter", "filters", "auto_filter", "autoFilter"))
        {
            formatting.AutoFilter = nestedFilter;
        }

        if (TryGetBoolean(header, out var styleHeader, "style", "styled", "enabled"))
        {
            formatting.StyleHeader = styleHeader;
        }

        var style = ParseStyle(header);

        if (style is not null)
        {
            formatting.HeaderStyle = style;
        }
    }

    private static void ParseColumns(JsonElement element, SpreadsheetFormatting formatting)
    {
        if (!TryGetProperty(element, out var columns, "columns", "column_formats", "columnFormats") ||
            columns.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in columns.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !TryGetString(item, out var name, "column", "name", "column_name", "columnName"))
            {
                continue;
            }

            var column = new SpreadsheetColumnFormat
            {
                Column = name,
                Style = ParseStyle(item),
            };

            if (TryGetString(item, out var format, "format", "number_format", "numberFormat"))
            {
                column.NumberFormat = ParseNumberFormat(format);
            }

            if (TryGetString(item, out var formatCode, "format_code", "formatCode", "custom_format"))
            {
                column.FormatCode = formatCode;
            }

            if (TryGetString(item, out var dataKind, "data_kind", "dataKind", "type", "store_as", "storeAs"))
            {
                column.DataKind = ParseDataKind(dataKind);
            }

            if (TryGetInt32(item, out var decimals, "decimals", "decimal_places", "decimalPlaces"))
            {
                column.Decimals = decimals;
            }

            if (TryGetString(item, out var symbol, "currency_symbol", "currencySymbol", "symbol"))
            {
                column.CurrencySymbol = symbol;
            }

            if (TryGetBoolean(item, out var negativesInRed, "negatives_in_red", "negativesInRed", "red_negatives"))
            {
                column.NegativesInRed = negativesInRed;
            }

            if (TryGetDouble(item, out var width, "width", "column_width", "columnWidth"))
            {
                column.Width = width;
            }

            if (TryGetString(item, out var formula, "formula", "expression"))
            {
                column.Formula = formula;
            }

            formatting.Columns.Add(column);
        }
    }

    private static void ParseConditionalFormats(JsonElement element, SpreadsheetFormatting formatting)
    {
        if (!TryGetProperty(
                element,
                out var conditionals,
                "conditional_formats",
                "conditionalFormats",
                "conditional_formatting",
                "conditionalFormatting") ||
            conditionals.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in conditionals.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !TryGetString(item, out var name, "column", "name", "column_name", "columnName"))
            {
                continue;
            }

            var conditional = new SpreadsheetConditionalFormat
            {
                Column = name,
                Style = ParseStyle(item),
            };

            if (TryGetString(item, out var rule, "rule", "when", "type", "condition", "operator"))
            {
                conditional.Rule = ParseConditionalRule(rule);
            }

            if (TryGetScalarAsString(item, out var value, "value", "threshold", "minimum", "text"))
            {
                conditional.Value = value;
            }

            if (TryGetScalarAsString(item, out var secondValue, "second_value", "secondValue", "maximum", "upper"))
            {
                conditional.SecondValue = secondValue;
            }

            if (TryGetString(item, out var minimumColor, "min_color", "minColor", "minimum_color", "low_color"))
            {
                conditional.MinimumColor = minimumColor;
            }

            if (TryGetString(item, out var midpointColor, "mid_color", "midColor", "midpoint_color", "middle_color"))
            {
                conditional.MidpointColor = midpointColor;
            }

            if (TryGetString(item, out var maximumColor, "max_color", "maxColor", "maximum_color", "high_color"))
            {
                conditional.MaximumColor = maximumColor;
            }

            if (TryGetString(item, out var barColor, "bar_color", "barColor"))
            {
                conditional.BarColor = barColor;
            }

            if (TryGetString(item, out var iconSet, "icon_set", "iconSet", "icons"))
            {
                conditional.IconSet = iconSet;
            }

            formatting.ConditionalFormats.Add(conditional);
        }
    }

    private static void ParseTotalRow(JsonElement element, SpreadsheetFormatting formatting)
    {
        if (!TryGetProperty(element, out var totalRow, "total_row", "totalRow", "totals") ||
            totalRow.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var result = new SpreadsheetTotalRow
        {
            Style = ParseStyle(totalRow),
        };

        if (TryGetString(totalRow, out var label, "label", "title", "text"))
        {
            result.Label = label;
        }

        if (TryGetProperty(totalRow, out var columns, "columns", "aggregates") &&
            columns.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in columns.EnumerateArray())
            {
                // A bare column name is accepted alongside the full object, because a model asked for
                // "a total on Amount" often writes just the name.
                if (item.ValueKind == JsonValueKind.String)
                {
                    result.Columns.Add(new SpreadsheetTotalColumn
                    {
                        Column = item.GetString(),
                        Function = SpreadsheetAggregateFunction.Sum,
                    });

                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object ||
                    !TryGetString(item, out var name, "column", "name", "column_name", "columnName"))
                {
                    continue;
                }

                var total = new SpreadsheetTotalColumn { Column = name };

                if (TryGetString(item, out var function, "function", "aggregate", "operation"))
                {
                    total.Function = ParseAggregate(function);
                }

                result.Columns.Add(total);
            }
        }

        formatting.TotalRow = result;
    }

    private static void ParseCharts(JsonElement element, SpreadsheetFormatting formatting)
    {
        if (!TryGetProperty(element, out var charts, "charts", "chart") ||
            charts.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
        {
            return;
        }

        if (charts.ValueKind == JsonValueKind.Object)
        {
            AddChart(charts, formatting);

            return;
        }

        foreach (var item in charts.EnumerateArray())
        {
            AddChart(item, formatting);
        }
    }

    private static void AddChart(JsonElement item, SpreadsheetFormatting formatting)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var chart = new SpreadsheetChart();

        if (TryGetString(item, out var kind, "type", "kind", "chart_type", "chartType"))
        {
            chart.Kind = ParseChartKind(kind);
        }

        if (TryGetString(item, out var title, "title", "name", "label"))
        {
            chart.Title = title;
        }

        if (TryGetString(item, out var category, "category_column", "categoryColumn", "labels", "label_column", "x"))
        {
            chart.CategoryColumn = category;
        }

        if (TryGetProperty(item, out var values, "value_columns", "valueColumns", "series", "values", "y"))
        {
            if (values.ValueKind == JsonValueKind.String)
            {
                chart.ValueColumns.Add(values.GetString());
            }
            else if (values.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in values.EnumerateArray())
                {
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        chart.ValueColumns.Add(value.GetString());
                    }
                    else if (value.ValueKind == JsonValueKind.Object &&
                        TryGetString(value, out var seriesName, "column", "name"))
                    {
                        chart.ValueColumns.Add(seriesName);
                    }
                }
            }
        }

        if (TryGetInt32(item, out var maxCategories, "max_categories", "maxCategories", "limit", "top"))
        {
            chart.MaxCategories = maxCategories;
        }

        if (TryGetInt32(item, out var width, "width"))
        {
            chart.Width = width;
        }

        if (TryGetInt32(item, out var height, "height"))
        {
            chart.Height = height;
        }

        formatting.Charts.Add(chart);
    }

    private static SpreadsheetCellStyle ParseStyle(JsonElement element)
    {
        // A style may be written inline on the object or nested under a "style" member; both shapes
        // are read so the caller does not have to guess which one this library expects.
        var source = TryGetProperty(element, out var nested, "style", "format_style", "cell_style") &&
            nested.ValueKind == JsonValueKind.Object
            ? nested
            : element;

        var style = new SpreadsheetCellStyle();

        if (TryGetBoolean(source, out var bold, "bold"))
        {
            style.Bold = bold;
        }

        if (TryGetBoolean(source, out var italic, "italic"))
        {
            style.Italic = italic;
        }

        if (TryGetBoolean(source, out var underline, "underline"))
        {
            style.Underline = underline;
        }

        if (TryGetDouble(source, out var fontSize, "font_size", "fontSize", "size"))
        {
            style.FontSize = fontSize;
        }

        if (TryGetString(source, out var fontName, "font_name", "fontName", "font"))
        {
            style.FontName = fontName;
        }

        if (TryGetString(source, out var fontColor, "font_color", "fontColor", "text_color", "color", "foreground"))
        {
            style.FontColor = fontColor;
        }

        if (TryGetString(
            source,
            out var background,
            "background_color",
            "backgroundColor",
            "fill",
            "fill_color",
            "highlight"))
        {
            style.BackgroundColor = background;
        }

        if (TryGetString(source, out var alignment, "align", "alignment", "horizontal_alignment"))
        {
            style.Alignment = ParseAlignment(alignment);
        }

        if (TryGetBoolean(source, out var wrap, "wrap", "wrap_text", "wrapText"))
        {
            style.WrapText = wrap;
        }

        if (TryGetBoolean(source, out var border, "border", "borders", "bordered"))
        {
            style.Border = border;
        }

        return style.IsEmpty
            ? null
            : style;
    }

    private static SpreadsheetNumberFormat ParseNumberFormat(string value)
    {
        return Normalize(value) switch
        {
            "text" or "string" => SpreadsheetNumberFormat.Text,
            "number" or "numeric" or "decimal" or "integer" or "int" => SpreadsheetNumberFormat.Number,
            "currency" or "money" or "dollar" or "dollars" => SpreadsheetNumberFormat.Currency,
            "accounting" => SpreadsheetNumberFormat.Accounting,
            "percent" or "percentage" => SpreadsheetNumberFormat.Percent,
            "scientific" or "exponential" => SpreadsheetNumberFormat.Scientific,
            "date" => SpreadsheetNumberFormat.Date,
            "datetime" or "timestamp" => SpreadsheetNumberFormat.DateTime,
            "time" => SpreadsheetNumberFormat.Time,
            "duration" or "elapsed" => SpreadsheetNumberFormat.Duration,
            _ => SpreadsheetNumberFormat.General,
        };
    }

    private static SpreadsheetDataKind ParseDataKind(string value)
    {
        return Normalize(value) switch
        {
            "text" or "string" => SpreadsheetDataKind.Text,
            "number" or "numeric" or "decimal" or "integer" or "int" or "currency" or "percent" => SpreadsheetDataKind.Number,
            "date" or "datetime" or "time" => SpreadsheetDataKind.Date,
            "boolean" or "bool" => SpreadsheetDataKind.Boolean,
            _ => SpreadsheetDataKind.Auto,
        };
    }

    private static SpreadsheetHorizontalAlignment ParseAlignment(string value)
    {
        return Normalize(value) switch
        {
            "left" or "start" => SpreadsheetHorizontalAlignment.Left,
            "center" or "centre" or "middle" => SpreadsheetHorizontalAlignment.Center,
            "right" or "end" => SpreadsheetHorizontalAlignment.Right,
            _ => SpreadsheetHorizontalAlignment.General,
        };
    }

    private static SpreadsheetConditionalRule ParseConditionalRule(string value)
    {
        return Normalize(value) switch
        {
            "lessthan" or "less" or "below" or "lt" => SpreadsheetConditionalRule.LessThan,
            "equalto" or "equals" or "equal" or "eq" => SpreadsheetConditionalRule.EqualTo,
            "between" or "inrange" => SpreadsheetConditionalRule.Between,
            "containstext" or "contains" => SpreadsheetConditionalRule.ContainsText,
            "duplicatevalues" or "duplicates" or "duplicate" => SpreadsheetConditionalRule.DuplicateValues,
            "colorscale" or "gradient" or "heatmap" or "colourscale" => SpreadsheetConditionalRule.ColorScale,
            "databar" or "bars" or "bar" => SpreadsheetConditionalRule.DataBar,
            "iconset" or "icons" => SpreadsheetConditionalRule.IconSet,
            _ => SpreadsheetConditionalRule.GreaterThan,
        };
    }

    private static SpreadsheetAggregateFunction ParseAggregate(string value)
    {
        return Normalize(value) switch
        {
            "average" or "avg" or "mean" => SpreadsheetAggregateFunction.Average,
            "count" => SpreadsheetAggregateFunction.Count,
            "min" or "minimum" => SpreadsheetAggregateFunction.Min,
            "max" or "maximum" => SpreadsheetAggregateFunction.Max,
            _ => SpreadsheetAggregateFunction.Sum,
        };
    }

    private static SpreadsheetChartKind ParseChartKind(string value)
    {
        return Normalize(value) switch
        {
            "bar" or "horizontalbar" => SpreadsheetChartKind.Bar,
            "line" => SpreadsheetChartKind.Line,
            "pie" or "donut" or "doughnut" => SpreadsheetChartKind.Pie,
            "area" => SpreadsheetChartKind.Area,
            _ => SpreadsheetChartKind.Column,
        };
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
            }
        }

        return new string(buffer[..length]);
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null)
            {
                return true;
            }
        }

        value = default;

        return false;
    }

    private static bool TryGetString(JsonElement element, out string value, params string[] names)
    {
        value = null;

        if (!TryGetProperty(element, out var property, names) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();

        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetScalarAsString(JsonElement element, out string value, params string[] names)
    {
        value = null;

        if (!TryGetProperty(element, out var property, names))
        {
            return false;
        }

        value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetBoolean(JsonElement element, out bool value, params string[] names)
    {
        value = false;

        if (!TryGetProperty(element, out var property, names))
        {
            return false;
        }

        switch (property.ValueKind)
        {
            case JsonValueKind.True:
                value = true;

                return true;

            case JsonValueKind.False:
                return true;

            case JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed):
                value = parsed;

                return true;

            default:
                return false;
        }
    }

    private static bool TryGetInt32(JsonElement element, out int value, params string[] names)
    {
        value = 0;

        if (!TryGetProperty(element, out var property, names))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    private static bool TryGetDouble(JsonElement element, out double value, params string[] names)
    {
        value = 0;

        if (!TryGetProperty(element, out var property, names))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }
}
