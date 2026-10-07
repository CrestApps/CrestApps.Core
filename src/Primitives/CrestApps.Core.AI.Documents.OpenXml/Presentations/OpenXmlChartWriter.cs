using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Writes a chart part and the workbook it embeds.
/// </summary>
/// <remarks>
/// The chart caches every value it plots, so it draws in any viewer, and it points those values at a small
/// workbook embedded beside it, so PowerPoint's Edit Data opens the numbers in Excel rather than reporting a
/// missing link. The cached values and the workbook are written from the same definition and cannot disagree.
/// </remarks>
internal static class OpenXmlChartWriter
{
    /// <summary>
    /// The name of the embedded workbook's sheet, which every series formula refers to.
    /// </summary>
    public const string SheetName = "Sheet1";

    private const string CategoryAxisId = "111111111";
    private const string ValueAxisId = "222222222";

    /// <summary>
    /// Writes the chart part markup.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="workbookRelationshipId">The relationship identifier of the embedded workbook.</param>
    /// <returns>The markup.</returns>
    public static string Build(OpenXmlChartDefinition chart, string workbookRelationshipId)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var builder = new StringBuilder(8192);
        builder.Append("<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        builder.Append("<c:date1904 val=\"0\"/><c:lang val=\"en-US\"/><c:roundedCorners val=\"0\"/>");
        builder.Append("<c:chart>");

        if (!string.IsNullOrWhiteSpace(chart.Title))
        {
            builder.Append(Title(chart.Title, chart, chart.FontSize + 4, rotated: false));
            builder.Append("<c:autoTitleDeleted val=\"0\"/>");
        }
        else
        {
            builder.Append("<c:autoTitleDeleted val=\"1\"/>");
        }

        builder.Append("<c:plotArea><c:layout/>");
        AppendPlot(builder, chart);

        if (chart.Plot is not "pie" and not "doughnut")
        {
            AppendAxes(builder, chart);
        }

        builder.Append("<c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr>");
        builder.Append("</c:plotArea>");

        if (chart.LegendPosition is not null)
        {
            builder.Append("<c:legend><c:legendPos val=\"").Append(chart.LegendPosition).Append("\"/><c:overlay val=\"0\"/>");
            builder.Append(TextProperties(chart, chart.FontSize, bold: false));
            builder.Append("</c:legend>");
        }

        builder.Append("<c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/>");
        builder.Append("</c:chart>");
        builder.Append("<c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr>");
        builder.Append(TextProperties(chart, chart.FontSize, bold: false));

        if (!string.IsNullOrEmpty(workbookRelationshipId))
        {
            builder.Append("<c:externalData r:id=\"").Append(workbookRelationshipId).Append("\"><c:autoUpdate val=\"0\"/></c:externalData>");
        }

        builder.Append("</c:chartSpace>");

        return builder.ToString();
    }

    /// <summary>
    /// Writes the workbook a chart embeds: categories down the first column, one column per series.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <returns>The workbook bytes.</returns>
    public static byte[] BuildWorkbook(OpenXmlChartDefinition chart)
    {
        var header = new List<string> { chart.Plot == "scatter" ? "X" : string.Empty };
        header.AddRange(chart.Series.Select((series, index) => string.IsNullOrWhiteSpace(series.Name) ? "Series " + (index + 1).ToString(CultureInfo.InvariantCulture) : series.Name));

        var rows = new List<IReadOnlyList<string>>();
        var count = PointCount(chart);

        for (var index = 0; index < count; index++)
        {
            var row = new List<string>
            {
                chart.Plot == "scatter"
                    ? Format(chart.Series.Count > 0 && index < chart.Series[0].XValues.Count ? chart.Series[0].XValues[index] : index + 1)
                    : index < chart.Categories.Count ? chart.Categories[index] ?? string.Empty : string.Empty,
            };

            foreach (var series in chart.Series)
            {
                row.Add(index < series.Values.Count ? Format(series.Values[index]) : string.Empty);
            }

            rows.Add(row);
        }

        var content = new GeneratedFileContent
        {
            Header = header,
            Rows = rows,
            SpreadsheetFormatting = new SpreadsheetFormatting { SheetName = SheetName },
        };

        using var stream = new MemoryStream();
        new SpreadsheetGeneratedFileWriter().WriteAsync(content, stream).GetAwaiter().GetResult();

        return stream.ToArray();
    }

    /// <summary>
    /// Returns the number of points the chart plots.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <returns>The number of points.</returns>
    public static int PointCount(OpenXmlChartDefinition chart)
    {
        var values = chart.Series.Count == 0 ? 0 : chart.Series.Max(series => Math.Max(series.Values.Count, series.XValues.Count));

        return Math.Max(chart.Categories.Count, values);
    }

    /// <summary>
    /// Returns the spreadsheet column letter of a zero-based column.
    /// </summary>
    /// <param name="index">The column.</param>
    /// <returns>The letter, such as <c>B</c>.</returns>
    public static string ColumnName(int index)
    {
        return SpreadsheetFormula.ToColumnName(index);
    }

    /// <summary>
    /// Writes a colour as DrawingML markup.
    /// </summary>
    /// <param name="color">The colour as the caller wrote it.</param>
    /// <returns>The <c>a:schemeClr</c> or <c>a:srgbClr</c> markup.</returns>
    public static string ColorXml(string color)
    {
        if (!PresentationColor.TryParse(color, out var parsed) || parsed.IsNone)
        {
            parsed = OpenXmlDrawingWriter.ParseColor("accent1", "chart");
        }

        if (!parsed.IsTheme)
        {
            return "<a:srgbClr val=\"" + parsed.Hex + "\"/>";
        }

        var value = parsed.ThemeSlot switch
        {
            "dk1" => "tx1",
            "lt1" => "bg1",
            "dk2" => "tx2",
            "lt2" => "bg2",
            var slot => slot,
        };

        if (parsed.Brightness > 0)
        {
            return "<a:schemeClr val=\"" + value + "\"><a:lumMod val=\"" + Percent(1 - parsed.Brightness) + "\"/><a:lumOff val=\"" + Percent(parsed.Brightness) + "\"/></a:schemeClr>";
        }

        if (parsed.Brightness < 0)
        {
            return "<a:schemeClr val=\"" + value + "\"><a:lumMod val=\"" + Percent(1 + parsed.Brightness) + "\"/></a:schemeClr>";
        }

        return "<a:schemeClr val=\"" + value + "\"/>";
    }

    /// <summary>
    /// Returns the colour a series is drawn in.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="index">The series or point index.</param>
    /// <param name="own">The colour the series names itself, if any.</param>
    /// <returns>The colour.</returns>
    public static string SeriesColor(OpenXmlChartDefinition chart, int index, string own)
    {
        if (!string.IsNullOrWhiteSpace(own))
        {
            return own;
        }

        if (chart.Palette.Count > 0)
        {
            return chart.Palette[index % chart.Palette.Count];
        }

        var accent = "accent" + ((index % 6) + 1).ToString(CultureInfo.InvariantCulture);

        return (index / 6) switch
        {
            0 => accent,
            1 => accent + " darker 40%",
            _ => accent + " lighter 40%",
        };
    }

    private static void AppendPlot(StringBuilder builder, OpenXmlChartDefinition chart)
    {
        switch (chart.Plot)
        {
            case "line":
                builder.Append("<c:lineChart><c:grouping val=\"").Append(chart.Grouping == "clustered" ? "standard" : chart.Grouping).Append("\"/><c:varyColors val=\"0\"/>");
                AppendSeries(builder, chart);
                AppendDataLabels(builder, chart);
                builder.Append("<c:marker val=\"1\"/>");
                AppendAxisIds(builder);
                builder.Append("</c:lineChart>");
                break;

            case "area":
                builder.Append("<c:areaChart><c:grouping val=\"").Append(chart.Grouping == "clustered" ? "standard" : chart.Grouping).Append("\"/><c:varyColors val=\"0\"/>");
                AppendSeries(builder, chart);
                AppendDataLabels(builder, chart);
                AppendAxisIds(builder);
                builder.Append("</c:areaChart>");
                break;

            case "pie":
                builder.Append("<c:pieChart><c:varyColors val=\"1\"/>");
                AppendSeries(builder, chart);
                AppendDataLabels(builder, chart);
                builder.Append("<c:firstSliceAng val=\"0\"/></c:pieChart>");
                break;

            case "doughnut":
                builder.Append("<c:doughnutChart><c:varyColors val=\"1\"/>");
                AppendSeries(builder, chart);
                AppendDataLabels(builder, chart);
                builder.Append("<c:firstSliceAng val=\"0\"/><c:holeSize val=\"60\"/></c:doughnutChart>");
                break;

            case "scatter":
                builder.Append("<c:scatterChart><c:scatterStyle val=\"lineMarker\"/><c:varyColors val=\"0\"/>");
                AppendSeries(builder, chart);
                AppendDataLabels(builder, chart);
                AppendAxisIds(builder);
                builder.Append("</c:scatterChart>");
                break;

            case "radar":
                builder.Append("<c:radarChart><c:radarStyle val=\"marker\"/><c:varyColors val=\"0\"/>");
                AppendSeries(builder, chart);
                AppendDataLabels(builder, chart);
                AppendAxisIds(builder);
                builder.Append("</c:radarChart>");
                break;

            default:
                builder.Append("<c:barChart><c:barDir val=\"").Append(chart.Horizontal ? "bar" : "col").Append("\"/><c:grouping val=\"").Append(chart.Grouping).Append("\"/><c:varyColors val=\"0\"/>");
                AppendSeries(builder, chart);
                AppendDataLabels(builder, chart);
                builder.Append("<c:gapWidth val=\"80\"/>");

                if (chart.Grouping is "stacked" or "percentStacked")
                {
                    builder.Append("<c:overlap val=\"100\"/>");
                }

                AppendAxisIds(builder);
                builder.Append("</c:barChart>");
                break;
        }
    }

    private static void AppendSeries(StringBuilder builder, OpenXmlChartDefinition chart)
    {
        var count = PointCount(chart);

        for (var index = 0; index < chart.Series.Count; index++)
        {
            var series = chart.Series[index];
            var column = ColumnName(index + 1);
            var color = ColorXml(SeriesColor(chart, index, series.Color));
            var name = string.IsNullOrWhiteSpace(series.Name) ? "Series " + (index + 1).ToString(CultureInfo.InvariantCulture) : series.Name;

            builder.Append("<c:ser><c:idx val=\"").Append(index).Append("\"/><c:order val=\"").Append(index).Append("\"/>");
            builder.Append("<c:tx><c:strRef><c:f>").Append(SheetName).Append("!$").Append(column).Append("$1</c:f><c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>").Append(Escape(name)).Append("</c:v></c:pt></c:strCache></c:strRef></c:tx>");

            switch (chart.Plot)
            {
                case "line":
                case "radar":
                    builder.Append("<c:spPr><a:ln w=\"28575\" cap=\"rnd\"><a:solidFill>").Append(color).Append("</a:solidFill><a:round/></a:ln></c:spPr>");
                    builder.Append(chart.Markers || chart.Plot == "radar"
                        ? "<c:marker><c:symbol val=\"circle\"/><c:size val=\"6\"/><c:spPr><a:solidFill>" + color + "</a:solidFill><a:ln><a:noFill/></a:ln></c:spPr></c:marker>"
                        : "<c:marker><c:symbol val=\"none\"/></c:marker>");
                    break;

                case "scatter":
                    builder.Append("<c:spPr><a:ln w=\"25400\" cap=\"rnd\"><a:noFill/></a:ln></c:spPr>");
                    builder.Append("<c:marker><c:symbol val=\"circle\"/><c:size val=\"7\"/><c:spPr><a:solidFill>").Append(color).Append("</a:solidFill><a:ln><a:noFill/></a:ln></c:spPr></c:marker>");
                    break;

                case "pie":
                case "doughnut":
                    builder.Append("<c:spPr><a:ln w=\"19050\"><a:solidFill><a:schemeClr val=\"bg1\"/></a:solidFill></a:ln></c:spPr>");

                    for (var point = 0; point < count; point++)
                    {
                        builder.Append("<c:dPt><c:idx val=\"").Append(point).Append("\"/><c:bubble3D val=\"0\"/><c:spPr><a:solidFill>")
                            .Append(ColorXml(SeriesColor(chart, point, null)))
                            .Append("</a:solidFill><a:ln w=\"19050\"><a:solidFill><a:schemeClr val=\"bg1\"/></a:solidFill></a:ln></c:spPr></c:dPt>");
                    }

                    break;

                default:
                    builder.Append("<c:spPr><a:solidFill>").Append(color).Append("</a:solidFill><a:ln><a:noFill/></a:ln></c:spPr>");

                    if (chart.Plot == "bar")
                    {
                        builder.Append("<c:invertIfNegative val=\"0\"/>");
                    }

                    break;
            }

            if (chart.Plot == "scatter")
            {
                builder.Append("<c:xVal>").Append(NumberReference(0, series.XValues.Count > 0 ? series.XValues : Enumerable.Range(1, count).Select(value => (double?)value).ToList(), count, null)).Append("</c:xVal>");
                builder.Append("<c:yVal>").Append(NumberReference(index + 1, series.Values, count, chart.NumberFormat)).Append("</c:yVal>");
                builder.Append("<c:smooth val=\"0\"/>");
            }
            else
            {
                builder.Append("<c:cat>").Append(StringReference(chart.Categories, count)).Append("</c:cat>");
                builder.Append("<c:val>").Append(NumberReference(index + 1, series.Values, count, chart.NumberFormat)).Append("</c:val>");

                if (chart.Plot == "line")
                {
                    builder.Append("<c:smooth val=\"0\"/>");
                }
            }

            builder.Append("</c:ser>");
        }
    }

    /// <summary>
    /// Writes a string reference to the category column, with its cache.
    /// </summary>
    /// <param name="categories">The categories.</param>
    /// <param name="count">The number of points.</param>
    /// <returns>The <c>c:strRef</c> markup.</returns>
    public static string StringReference(IList<string> categories, int count)
    {
        var builder = new StringBuilder();
        builder.Append("<c:strRef><c:f>").Append(SheetName).Append("!$A$2:$A$").Append(Math.Max(count + 1, 2)).Append("</c:f><c:strCache><c:ptCount val=\"").Append(count).Append("\"/>");

        for (var index = 0; index < count; index++)
        {
            var value = index < categories.Count ? categories[index] : string.Empty;
            builder.Append("<c:pt idx=\"").Append(index).Append("\"><c:v>").Append(Escape(value)).Append("</c:v></c:pt>");
        }

        builder.Append("</c:strCache></c:strRef>");

        return builder.ToString();
    }

    /// <summary>
    /// Writes a number reference to a column, with its cache.
    /// </summary>
    /// <param name="column">The zero-based column.</param>
    /// <param name="values">The values.</param>
    /// <param name="count">The number of points.</param>
    /// <param name="format">The number format, or <see langword="null"/> for General.</param>
    /// <returns>The <c>c:numRef</c> markup.</returns>
    public static string NumberReference(int column, IList<double?> values, int count, string format)
    {
        var name = ColumnName(column);
        var builder = new StringBuilder();
        builder.Append("<c:numRef><c:f>").Append(SheetName).Append("!$").Append(name).Append("$2:$").Append(name).Append('$').Append(Math.Max(count + 1, 2)).Append("</c:f><c:numCache><c:formatCode>")
            .Append(Escape(string.IsNullOrWhiteSpace(format) ? "General" : format)).Append("</c:formatCode><c:ptCount val=\"").Append(count).Append("\"/>");

        for (var index = 0; index < count; index++)
        {
            if (index < values.Count && values[index] is { } number)
            {
                builder.Append("<c:pt idx=\"").Append(index).Append("\"><c:v>").Append(Format(number)).Append("</c:v></c:pt>");
            }
        }

        builder.Append("</c:numCache></c:numRef>");

        return builder.ToString();
    }

    private static void AppendDataLabels(StringBuilder builder, OpenXmlChartDefinition chart)
    {
        if (!chart.DataLabels)
        {
            return;
        }

        builder.Append("<c:dLbls>");

        if (!string.IsNullOrWhiteSpace(chart.NumberFormat))
        {
            builder.Append("<c:numFmt formatCode=\"").Append(Escape(chart.NumberFormat)).Append("\" sourceLinked=\"0\"/>");
        }

        builder.Append("<c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr>");
        builder.Append(TextProperties(chart, Math.Max(8, chart.FontSize - 1), bold: false));

        if (chart.Plot is "pie" or "doughnut")
        {
            builder.Append("<c:showLegendKey val=\"0\"/><c:showVal val=\"1\"/><c:showCatName val=\"0\"/><c:showSerName val=\"0\"/><c:showPercent val=\"0\"/><c:showBubbleSize val=\"0\"/><c:showLeaderLines val=\"1\"/>");
        }
        else
        {
            builder.Append("<c:showLegendKey val=\"0\"/><c:showVal val=\"1\"/><c:showCatName val=\"0\"/><c:showSerName val=\"0\"/><c:showPercent val=\"0\"/><c:showBubbleSize val=\"0\"/>");
        }

        builder.Append("</c:dLbls>");
    }

    private static void AppendAxisIds(StringBuilder builder)
    {
        builder.Append("<c:axId val=\"").Append(CategoryAxisId).Append("\"/><c:axId val=\"").Append(ValueAxisId).Append("\"/>");
    }

    private static void AppendAxes(StringBuilder builder, OpenXmlChartDefinition chart)
    {
        var categoryPosition = chart.Horizontal ? "l" : "b";
        var valuePosition = chart.Horizontal ? "b" : "l";
        var axisLine = "<c:spPr><a:noFill/><a:ln w=\"9525\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"tx1\"><a:lumMod val=\"25000\"/><a:lumOff val=\"75000\"/></a:schemeClr></a:solidFill><a:round/></a:ln></c:spPr>";
        var gridlines = chart.Gridlines
            ? "<c:majorGridlines><c:spPr><a:ln w=\"9525\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"tx1\"><a:lumMod val=\"15000\"/><a:lumOff val=\"85000\"/></a:schemeClr></a:solidFill><a:round/></a:ln></c:spPr></c:majorGridlines>"
            : string.Empty;

        if (chart.Plot == "scatter")
        {
            // A scatter chart has two value axes: the horizontal one plays the part of the categories.
            builder.Append("<c:valAx><c:axId val=\"").Append(CategoryAxisId).Append("\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"b\"/>");
            AppendAxisTitle(builder, chart.CategoryAxisTitle, chart, rotated: false);
            builder.Append("<c:numFmt formatCode=\"General\" sourceLinked=\"1\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/>").Append(axisLine).Append(TextProperties(chart, chart.FontSize, bold: false));
            builder.Append("<c:crossAx val=\"").Append(ValueAxisId).Append("\"/><c:crosses val=\"autoZero\"/><c:crossBetween val=\"midCat\"/></c:valAx>");
        }
        else
        {
            builder.Append("<c:catAx><c:axId val=\"").Append(CategoryAxisId).Append("\"/><c:scaling><c:orientation val=\"").Append(chart.Horizontal ? "maxMin" : "minMax").Append("\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"").Append(categoryPosition).Append("\"/>");
            AppendAxisTitle(builder, chart.CategoryAxisTitle, chart, rotated: chart.Horizontal);
            builder.Append("<c:numFmt formatCode=\"General\" sourceLinked=\"1\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/>").Append(axisLine).Append(TextProperties(chart, chart.FontSize, bold: false));
            builder.Append("<c:crossAx val=\"").Append(ValueAxisId).Append("\"/><c:crosses val=\"autoZero\"/><c:auto val=\"1\"/><c:lblAlgn val=\"ctr\"/><c:lblOffset val=\"100\"/><c:noMultiLvlLbl val=\"0\"/></c:catAx>");
        }

        builder.Append("<c:valAx><c:axId val=\"").Append(ValueAxisId).Append("\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"").Append(valuePosition).Append("\"/>").Append(gridlines);
        AppendAxisTitle(builder, chart.ValueAxisTitle, chart, rotated: !chart.Horizontal);
        builder.Append(string.IsNullOrWhiteSpace(chart.NumberFormat)
            ? "<c:numFmt formatCode=\"General\" sourceLinked=\"1\"/>"
            : "<c:numFmt formatCode=\"" + Escape(chart.NumberFormat) + "\" sourceLinked=\"0\"/>");
        builder.Append("<c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/><c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr>").Append(TextProperties(chart, chart.FontSize, bold: false));
        // Categories of a bar chart run top to bottom, so the value axis crosses at the last one to stay at the bottom.
        builder.Append("<c:crossAx val=\"").Append(CategoryAxisId).Append("\"/><c:crosses val=\"").Append(chart.Horizontal ? "max" : "autoZero").Append("\"/><c:crossBetween val=\"").Append(chart.Plot == "scatter" ? "midCat" : "between").Append("\"/></c:valAx>");
    }

    private static void AppendAxisTitle(StringBuilder builder, string title, OpenXmlChartDefinition chart, bool rotated)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.Append(Title(title, chart, chart.FontSize, rotated));
        }
    }

    /// <summary>
    /// Writes a chart or axis title.
    /// </summary>
    /// <param name="text">The title text.</param>
    /// <param name="chart">The chart, for its text style.</param>
    /// <param name="size">The size in points.</param>
    /// <param name="rotated">Whether the title runs up the side, as a vertical axis title does.</param>
    /// <returns>The <c>c:title</c> markup.</returns>
    public static string Title(string text, OpenXmlChartDefinition chart, double size, bool rotated)
    {
        var properties = RunProperties(chart, size, bold: false, element: "defRPr");

        return
            "<c:title><c:tx><c:rich><a:bodyPr" + (rotated ? " rot=\"-5400000\" vert=\"horz\"" : string.Empty) + "/><a:lstStyle/><a:p><a:pPr>" + properties + "</a:pPr>" +
            "<a:r>" + RunProperties(chart, size, bold: false, element: "rPr") + "<a:t>" + Escape(text) + "</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title>";
    }

    /// <summary>
    /// Writes chart text properties.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="size">The size in points.</param>
    /// <param name="bold">Whether the text is bold.</param>
    /// <returns>The <c>c:txPr</c> markup.</returns>
    public static string TextProperties(OpenXmlChartDefinition chart, double size, bool bold)
    {
        return "<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr>" + RunProperties(chart, size, bold, "defRPr") + "</a:pPr><a:endParaRPr lang=\"en-US\"/></a:p></c:txPr>";
    }

    private static string RunProperties(OpenXmlChartDefinition chart, double size, bool bold, string element)
    {
        var builder = new StringBuilder();
        builder.Append("<a:").Append(element).Append(element == "rPr" ? " lang=\"en-US\"" : string.Empty).Append(" sz=\"").Append((int)Math.Round(Math.Clamp(size, 6, 72) * 100)).Append("\" b=\"").Append(bold ? '1' : '0').Append("\">");
        builder.Append("<a:solidFill>").Append(ColorXml(chart.TextColor ?? "text1 lighter 35%")).Append("</a:solidFill>");

        if (!string.IsNullOrWhiteSpace(chart.Font))
        {
            builder.Append("<a:latin typeface=\"").Append(Escape(chart.Font)).Append("\"/>");
        }

        builder.Append("</a:").Append(element).Append('>');

        return builder.ToString();
    }

    private static string Percent(double fraction)
    {
        return ((int)Math.Round(Math.Clamp(fraction, 0, 1) * 100_000)).ToString(CultureInfo.InvariantCulture);
    }

    private static string Format(double? value)
    {
        return value is null ? string.Empty : value.Value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Escape(string value)
    {
        return PresentationTemplateXml.Escape(value);
    }
}
