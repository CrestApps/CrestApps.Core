using System.Globalization;
using System.Xml.Linq;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Charts;

/// <summary>
/// Writes charts into a document as native Word charts: a chart part with the data cached in it, and the
/// workbook it was drawn from embedded beside it, so the chart opens, prints and stays editable in Word.
/// </summary>
internal static class WordChartWriter
{
    /// <summary>
    /// The colors series are drawn in when none are given.
    /// </summary>
    public static readonly IReadOnlyList<string> Palette =
    [
        "2F5496", "ED7D31", "A5A5A5", "FFC000", "5B9BD5", "70AD47", "264478", "9E480E", "636363", "997300",
    ];

    private static readonly XNamespace _c = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace _a = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace _r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// Builds a paragraph holding a chart.
    /// </summary>
    /// <param name="owner">The main document part.</param>
    /// <param name="spec">The chart.</param>
    /// <param name="id">The drawing id.</param>
    /// <param name="width">The width in points.</param>
    /// <param name="height">The height in points.</param>
    /// <param name="altText">The alternative text, or <see langword="null"/> to describe the chart from its title.</param>
    /// <returns>The paragraph.</returns>
    public static Paragraph CreateParagraph(MainDocumentPart owner, WordChartSpec spec, uint id, double width, double height, string altText)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(spec);

        Validate(spec);

        var chartPart = owner.AddNewPart<ChartPart>(WordImageWriter.NewRelationshipId(owner));

        Write(chartPart, spec);

        var relationshipId = owner.GetIdOfPart(chartPart);
        var description = string.IsNullOrWhiteSpace(altText)
            ? (string.IsNullOrWhiteSpace(spec.Title) ? "Chart" : spec.Title.Trim()) + " (" + spec.Type.Replace('_', ' ') + " chart)"
            : altText.Trim();

        var drawing = new DocumentFormat.OpenXml.Wordprocessing.Drawing(new DW.Inline(
            new DW.Extent { Cx = WordUnits.ToEmus(width), Cy = WordUnits.ToEmus(height) },
            new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
            new DW.DocProperties { Id = id, Name = "Chart " + id.ToString(CultureInfo.InvariantCulture), Description = description },
            new DW.NonVisualGraphicFrameDrawingProperties(),
            new A.Graphic(new A.GraphicData(new C.ChartReference { Id = relationshipId })
            {
                Uri = WordDrawingReader.ChartUri,
            }))
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 0U,
            DistanceFromRight = 0U,
        });

        return new Paragraph(
            new ParagraphProperties { Justification = new Justification { Val = JustificationValues.Center } },
            new DocumentFormat.OpenXml.Wordprocessing.Run(drawing));
    }

    /// <summary>
    /// Writes a chart's content into its part, replacing what was there, and embeds its data as a workbook.
    /// </summary>
    /// <param name="chartPart">The chart part.</param>
    /// <param name="spec">The chart.</param>
    public static void Write(ChartPart chartPart, WordChartSpec spec)
    {
        ArgumentNullException.ThrowIfNull(chartPart);
        ArgumentNullException.ThrowIfNull(spec);

        Validate(spec);

        if (chartPart.EmbeddedPackagePart is { } existing)
        {
            chartPart.DeletePart(existing);
        }

        var package = chartPart.AddEmbeddedPackagePart(EmbeddedPackagePartType.Xlsx);

        using (var workbook = new MemoryStream(BuildWorkbook(spec)))
        {
            package.FeedData(workbook);
        }

        var document = new XDocument(BuildChartSpace(spec, chartPart.GetIdOfPart(package)));

        using var stream = new MemoryStream();

        document.Save(stream);
        stream.Position = 0;
        chartPart.FeedData(stream);
    }

    /// <summary>
    /// Checks that a chart has data a chart can be drawn from.
    /// </summary>
    /// <param name="spec">The chart.</param>
    /// <exception cref="Workspace.WordToolException">The chart has no series, or no values.</exception>
    public static void Validate(WordChartSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        spec.Type = WordChartSpec.Normalize(spec.Type);
        spec.Series = [.. spec.Series.Where(series => series is not null && series.Values.Count > 0)];

        if (spec.Series.Count == 0)
        {
            throw new Workspace.WordToolException("A chart needs at least one series with values: pass 'series' as [{ \"name\": \"Sales\", \"values\": [1, 2, 3] }] and 'labels' for the categories.");
        }

        var points = spec.Series.Max(series => series.Values.Count);

        if (spec.Kind != "scatter")
        {
            while (spec.Labels.Count < points)
            {
                spec.Labels.Add((spec.Labels.Count + 1).ToString(CultureInfo.InvariantCulture));
            }
        }

        if (spec.Kind is "pie" or "doughnut" && spec.Series.Count > 1)
        {
            spec.Series = [spec.Series[0]];
        }
    }

    private static XElement BuildChartSpace(WordChartSpec spec, string workbookRelationshipId)
    {
        var chart = new XElement(_c + "chart");

        if (!string.IsNullOrWhiteSpace(spec.Title))
        {
            chart.Add(Title(spec.Title, 1400));
            chart.Add(Val("autoTitleDeleted", "0"));
        }
        else
        {
            chart.Add(Val("autoTitleDeleted", "1"));
        }

        chart.Add(new XElement(_c + "plotArea", new XElement(_c + "layout"), BuildPlot(spec), BuildAxes(spec)));

        if (!string.Equals(spec.Legend, "none", StringComparison.OrdinalIgnoreCase))
        {
            chart.Add(new XElement(
                _c + "legend",
                Val("legendPos", (spec.Legend ?? "bottom").Trim().ToLowerInvariant() switch
                {
                    "right" => "r",
                    "left" => "l",
                    "top" => "t",
                    _ => "b",
                }),
                Val("overlay", "0")));
        }

        chart.Add(Val("plotVisOnly", "1"));
        chart.Add(Val("dispBlanksAs", "gap"));

        return new XElement(
            _c + "chartSpace",
            new XAttribute(XNamespace.Xmlns + "c", _c),
            new XAttribute(XNamespace.Xmlns + "a", _a),
            new XAttribute(XNamespace.Xmlns + "r", _r),
            Val("date1904", "0"),
            Val("roundedCorners", "0"),
            chart,
            new XElement(
                _c + "txPr",
                new XElement(_a + "bodyPr"),
                new XElement(_a + "lstStyle"),
                new XElement(_a + "p", new XElement(_a + "pPr", new XElement(_a + "defRPr", new XAttribute("sz", "900"))), new XElement(_a + "endParaRPr", new XAttribute("lang", "en-US")))),
            new XElement(_c + "externalData", new XAttribute(_r + "id", workbookRelationshipId), Val("autoUpdate", "0")));
    }

    private static XElement BuildPlot(WordChartSpec spec)
    {
        var kind = spec.Kind;
        var plot = kind switch
        {
            "line" => new XElement(_c + "lineChart", Val("grouping", spec.Grouping), Val("varyColors", "0")),
            "pie" => new XElement(_c + "pieChart", Val("varyColors", "1")),
            "doughnut" => new XElement(_c + "doughnutChart", Val("varyColors", "1")),
            "area" => new XElement(_c + "areaChart", Val("grouping", spec.Grouping), Val("varyColors", "0")),
            "scatter" => new XElement(_c + "scatterChart", Val("scatterStyle", "lineMarker"), Val("varyColors", "0")),
            _ => new XElement(_c + "barChart", Val("barDir", spec.IsHorizontal ? "bar" : "col"), Val("grouping", spec.Grouping), Val("varyColors", "0")),
        };

        for (var index = 0; index < spec.Series.Count; index++)
        {
            plot.Add(BuildSeries(spec, index));
        }

        if (spec.DataLabels)
        {
            plot.Add(new XElement(
                _c + "dLbls",
                Val("showLegendKey", "0"),
                Val("showVal", kind is "pie" or "doughnut" ? "0" : "1"),
                Val("showCatName", "0"),
                Val("showSerName", "0"),
                Val("showPercent", kind is "pie" or "doughnut" ? "1" : "0"),
                Val("showBubbleSize", "0")));
        }

        switch (kind)
        {
            case "bar":
                plot.Add(Val("gapWidth", "80"));

                if (spec.Grouping != "clustered")
                {
                    plot.Add(Val("overlap", "100"));
                }

                plot.Add(Val("axId", "500000001"), Val("axId", "500000002"));

                break;

            case "line":
                plot.Add(Val("marker", "1"), Val("axId", "500000001"), Val("axId", "500000002"));

                break;

            case "area":
            case "scatter":
                plot.Add(Val("axId", "500000001"), Val("axId", "500000002"));

                break;

            case "pie":
                plot.Add(Val("firstSliceAng", "0"));

                break;

            case "doughnut":
                plot.Add(Val("firstSliceAng", "0"), Val("holeSize", "55"));

                break;
        }

        return plot;
    }

    private static XElement BuildSeries(WordChartSpec spec, int index)
    {
        var series = spec.Series[index];
        var kind = spec.Kind;
        var column = ColumnName(index + 1);
        var count = kind == "scatter" ? series.Values.Count : spec.Labels.Count;
        var color = SeriesColor(spec, index);
        var element = new XElement(
            _c + "ser",
            Val("idx", index.ToString(CultureInfo.InvariantCulture)),
            Val("order", index.ToString(CultureInfo.InvariantCulture)),
            new XElement(_c + "tx", new XElement(
                _c + "strRef",
                new XElement(_c + "f", $"Sheet1!${column}$1"),
                new XElement(_c + "strCache", Val("ptCount", "1"), Point(0, string.IsNullOrWhiteSpace(series.Name) ? "Series " + (index + 1).ToString(CultureInfo.InvariantCulture) : series.Name)))));

        if (kind is "line" or "scatter")
        {
            element.Add(new XElement(_c + "spPr", new XElement(_a + "ln", new XAttribute("w", "28575"), new XAttribute("cap", "rnd"), Solid(color))));
            element.Add(new XElement(_c + "marker", Val("symbol", "circle"), Val("size", "5"), new XElement(_c + "spPr", Solid(color))));
        }
        else if (kind is "pie" or "doughnut")
        {
            for (var point = 0; point < count; point++)
            {
                element.Add(new XElement(
                    _c + "dPt",
                    Val("idx", point.ToString(CultureInfo.InvariantCulture)),
                    Val("bubble3D", "0"),
                    new XElement(_c + "spPr", Solid(PointColor(spec, point)), new XElement(_a + "ln", new XAttribute("w", "12700"), Solid("FFFFFF")))));
            }
        }
        else
        {
            element.Add(new XElement(_c + "spPr", Solid(color)));

            if (kind == "bar")
            {
                element.Add(Val("invertIfNegative", "0"));
            }
        }

        var valueFormat = string.IsNullOrWhiteSpace(spec.NumberFormat) ? "General" : spec.NumberFormat.Trim();

        if (kind == "scatter")
        {
            element.Add(new XElement(_c + "xVal", NumberReference($"Sheet1!$A$2:$A${count + 1}", series.XValues.Count > 0 ? series.XValues : [.. Enumerable.Range(1, count).Select(number => (double?)number)], "General")));
            element.Add(new XElement(_c + "yVal", NumberReference($"Sheet1!${column}$2:${column}${count + 1}", series.Values, valueFormat)));
            element.Add(Val("smooth", spec.Smooth ? "1" : "0"));

            return element;
        }

        var categories = new XElement(_c + "strCache", Val("ptCount", count.ToString(CultureInfo.InvariantCulture)));

        for (var point = 0; point < count; point++)
        {
            categories.Add(Point(point, spec.Labels[point] ?? string.Empty));
        }

        element.Add(new XElement(_c + "cat", new XElement(_c + "strRef", new XElement(_c + "f", $"Sheet1!$A$2:$A${count + 1}"), categories)));
        element.Add(new XElement(_c + "val", NumberReference($"Sheet1!${column}$2:${column}${count + 1}", series.Values, valueFormat)));

        if (kind == "line")
        {
            element.Add(Val("smooth", spec.Smooth ? "1" : "0"));
        }

        return element;
    }

    private static XElement NumberReference(string formula, List<double?> values, string format)
    {
        var cache = new XElement(_c + "numCache", new XElement(_c + "formatCode", format), Val("ptCount", values.Count.ToString(CultureInfo.InvariantCulture)));

        for (var point = 0; point < values.Count; point++)
        {
            if (values[point] is { } value && double.IsFinite(value))
            {
                cache.Add(Point(point, value.ToString("R", CultureInfo.InvariantCulture)));
            }
        }

        return new XElement(_c + "numRef", new XElement(_c + "f", formula), cache);
    }

    private static IEnumerable<XElement> BuildAxes(WordChartSpec spec)
    {
        if (spec.Kind is "pie" or "doughnut")
        {
            yield break;
        }

        var horizontal = spec.IsHorizontal;
        var categoryAxis = spec.Kind == "scatter"
            ? ValueAxis("500000001", "500000002", horizontal ? "l" : "b", spec.XAxisTitle, "General", gridlines: false)
            : new XElement(
                _c + "catAx",
                Val("axId", "500000001"),
                new XElement(_c + "scaling", Val("orientation", horizontal ? "maxMin" : "minMax")),
                Val("delete", "0"),
                Val("axPos", horizontal ? "l" : "b"),
                string.IsNullOrWhiteSpace(spec.XAxisTitle) ? null : Title(spec.XAxisTitle, 1000),
                new XElement(_c + "numFmt", new XAttribute("formatCode", "General"), new XAttribute("sourceLinked", "1")),
                Val("majorTickMark", "none"),
                Val("minorTickMark", "none"),
                Val("tickLblPos", "nextTo"),
                new XElement(_c + "spPr", new XElement(_a + "ln", new XAttribute("w", "9525"), Solid("BFBFBF"))),
                Val("crossAx", "500000002"),
                Val("crosses", "autoZero"),
                Val("auto", "1"),
                Val("lblAlgn", "ctr"),
                Val("lblOffset", "100"),
                Val("noMultiLvlLbl", "0"));

        yield return categoryAxis;

        var format = string.IsNullOrWhiteSpace(spec.NumberFormat)
            ? spec.Grouping == "percentStacked" ? "0%" : "General"
            : spec.NumberFormat.Trim();

        yield return ValueAxis("500000002", "500000001", horizontal ? "b" : "l", spec.YAxisTitle, format, gridlines: true, crossesMax: horizontal);
    }

    private static XElement ValueAxis(string id, string crossId, string position, string title, string format, bool gridlines, bool crossesMax = false)
    {
        return new XElement(
            _c + "valAx",
            Val("axId", id),
            new XElement(_c + "scaling", Val("orientation", "minMax")),
            Val("delete", "0"),
            Val("axPos", position),
            gridlines ? new XElement(_c + "majorGridlines", new XElement(_c + "spPr", new XElement(_a + "ln", new XAttribute("w", "9525"), Solid("E7E6E6")))) : null,
            string.IsNullOrWhiteSpace(title) ? null : Title(title, 1000),
            new XElement(_c + "numFmt", new XAttribute("formatCode", format), new XAttribute("sourceLinked", "0")),
            Val("majorTickMark", "none"),
            Val("minorTickMark", "none"),
            Val("tickLblPos", "nextTo"),
            new XElement(_c + "spPr", new XElement(_a + "ln", new XAttribute("w", "9525"), new XElement(_a + "noFill"))),
            Val("crossAx", crossId),
            Val("crosses", crossesMax ? "max" : "autoZero"),
            Val("crossBetween", "between"));
    }

    private static XElement Title(string text, int size)
    {
        return new XElement(
            _c + "title",
            new XElement(_c + "tx", new XElement(
                _c + "rich",
                new XElement(_a + "bodyPr"),
                new XElement(_a + "lstStyle"),
                new XElement(_a + "p",
                    new XElement(_a + "pPr", new XElement(_a + "defRPr", new XAttribute("sz", size.ToString(CultureInfo.InvariantCulture)), new XAttribute("b", "1"))),
                    new XElement(_a + "r", new XElement(_a + "rPr", new XAttribute("lang", "en-US"), new XAttribute("sz", size.ToString(CultureInfo.InvariantCulture)), new XAttribute("b", "1")), new XElement(_a + "t", text.Trim()))))),
            Val("overlay", "0"));
    }

    private static XElement Point(int index, string value)
    {
        return new XElement(_c + "pt", new XAttribute("idx", index.ToString(CultureInfo.InvariantCulture)), new XElement(_c + "v", value));
    }

    private static XElement Val(string name, string value)
    {
        return new XElement(_c + name, new XAttribute("val", value));
    }

    private static XElement Solid(string color)
    {
        return new XElement(_a + "solidFill", new XElement(_a + "srgbClr", new XAttribute("val", color)));
    }

    /// <summary>
    /// Returns the color a series is drawn in.
    /// </summary>
    /// <param name="spec">The chart.</param>
    /// <param name="index">The series index.</param>
    /// <returns>The six-digit hexadecimal color.</returns>
    public static string SeriesColor(WordChartSpec spec, int index)
    {
        if (WordColor.TryParse(spec.Series[index].Color, out var own))
        {
            return own;
        }

        return PointColor(spec, index);
    }

    /// <summary>
    /// Returns the color of the nth item in the chart's palette.
    /// </summary>
    /// <param name="spec">The chart.</param>
    /// <param name="index">The position.</param>
    /// <returns>The six-digit hexadecimal color.</returns>
    public static string PointColor(WordChartSpec spec, int index)
    {
        if (spec.Colors is { Count: > 0 } && WordColor.TryParse(spec.Colors[index % spec.Colors.Count], out var color))
        {
            return color;
        }

        return Palette[index % Palette.Count];
    }

    private static byte[] BuildWorkbook(WordChartSpec spec)
    {
        using var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            var scatter = spec.Kind == "scatter";
            var rowCount = scatter ? spec.Series.Max(series => series.Values.Count) : spec.Labels.Count;

            var header = new Row { RowIndex = 1U };

            header.Append(TextCell("A1", scatter ? "X" : string.Empty));

            for (var index = 0; index < spec.Series.Count; index++)
            {
                header.Append(TextCell(ColumnName(index + 1) + "1", spec.Series[index].Name ?? "Series " + (index + 1).ToString(CultureInfo.InvariantCulture)));
            }

            data.Append(header);

            for (var point = 0; point < rowCount; point++)
            {
                var rowNumber = (uint)(point + 2);
                var row = new Row { RowIndex = rowNumber };
                var reference = "A" + rowNumber.ToString(CultureInfo.InvariantCulture);

                if (scatter)
                {
                    var x = spec.Series[0].XValues.Count > point ? spec.Series[0].XValues[point] : point + 1;

                    row.Append(NumberCell(reference, x));
                }
                else
                {
                    row.Append(TextCell(reference, spec.Labels[point] ?? string.Empty));
                }

                for (var index = 0; index < spec.Series.Count; index++)
                {
                    var values = spec.Series[index].Values;

                    row.Append(NumberCell(ColumnName(index + 1) + rowNumber.ToString(CultureInfo.InvariantCulture), point < values.Count ? values[point] : null));
                }

                data.Append(row);
            }

            worksheetPart.Worksheet = new Worksheet(data);
            workbookPart.Workbook = new Workbook(new Sheets(new Sheet
            {
                Name = "Sheet1",
                SheetId = 1U,
                Id = workbookPart.GetIdOfPart(worksheetPart),
            }));
        }

        return stream.ToArray();
    }

    private static Cell TextCell(string reference, string text)
    {
        return new Cell(new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(text ?? string.Empty)))
        {
            CellReference = reference,
            DataType = CellValues.InlineString,
        };
    }

    private static Cell NumberCell(string reference, double? value)
    {
        var cell = new Cell { CellReference = reference };

        if (value is { } number && double.IsFinite(number))
        {
            cell.CellValue = new CellValue(number.ToString("R", CultureInfo.InvariantCulture));
        }

        return cell;
    }

    /// <summary>
    /// Returns the spreadsheet column name of a zero-based column number: A, B, …, Z, AA.
    /// </summary>
    /// <param name="index">The column number from 0.</param>
    /// <returns>The column name.</returns>
    public static string ColumnName(int index)
    {
        var name = string.Empty;
        var number = index + 1;

        while (number > 0)
        {
            var remainder = (number - 1) % 26;

            name = (char)('A' + remainder) + name;
            number = (number - 1) / 26;
        }

        return name;
    }
}
