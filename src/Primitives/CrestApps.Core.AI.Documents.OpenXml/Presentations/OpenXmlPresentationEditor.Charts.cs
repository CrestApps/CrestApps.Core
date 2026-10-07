using System.Globalization;
using System.Xml.Linq;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using P = DocumentFormat.OpenXml.Presentation;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Builds and changes slide charts.
/// </summary>
internal sealed partial class OpenXmlPresentationEditor
{
    private static readonly XNamespace _c = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace _a = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private P.GraphicFrame BuildChart(SlidePart slidePart, PresentationElementSpec spec, PresentationBounds area, uint id)
    {
        var chartSpec = spec.Chart;

        if (chartSpec?.Series is not { Count: > 0 } || chartSpec.Series.All(series => series?.Values is not { Count: > 0 }))
        {
            throw new PresentationEditException("A chart needs data: categories, and at least one series with values.");
        }

        var definition = Define(chartSpec, PresentationChartStyle.Combine(HouseStyle.Chart, chartSpec.Style), null);
        var bounds = ResolveBounds(spec.Bounds, area, area);
        var relationshipId = OpenXmlSchemaOrder.NextRelationshipId(slidePart);

        WriteChartPart(slidePart.AddNewPart<ChartPart>(relationshipId), definition);

        return new P.GraphicFrame(
            new P.NonVisualGraphicFrameProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = spec.Name ?? "Chart " + id.ToString(CultureInfo.InvariantCulture) },
                new P.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.Transform(new A.Offset { X = bounds.X, Y = bounds.Y }, new A.Extents { Cx = bounds.Width, Cy = bounds.Height }),
            new A.Graphic(new A.GraphicData(new C.ChartReference { Id = relationshipId }) { Uri = OpenXmlPresentationConstants.ChartGraphicUri }));
    }

    private static void WriteChartPart(ChartPart chartPart, OpenXmlChartDefinition definition)
    {
        var workbook = chartPart.EmbeddedPackagePart ?? chartPart.AddNewPart<EmbeddedPackagePart>(OpenXmlPresentationConstants.WorkbookContentType, "rId1");
        var workbookId = chartPart.GetIdOfPart(workbook);

        using (var stream = new MemoryStream(OpenXmlChartWriter.BuildWorkbook(definition)))
        {
            workbook.FeedData(stream);
        }

        chartPart.ChartSpace = new C.ChartSpace(OpenXmlChartWriter.Build(definition, workbookId));
    }

    /// <summary>
    /// Turns a chart request into a definition, filling what it leaves out from the chart it replaces.
    /// </summary>
    private OpenXmlChartDefinition Define(PresentationChartSpec spec, PresentationChartStyle style, PresentationChart current)
    {
        var kind = (spec.Kind ?? KindName(current) ?? "column").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

        if (!PresentationChartSpec.Kinds.Contains(kind))
        {
            throw new PresentationEditException($"\"{spec.Kind}\" is not a chart kind. Use one of: {string.Join(", ", PresentationChartSpec.Kinds)}.");
        }

        var definition = new OpenXmlChartDefinition
        {
            Plot = kind switch
            {
                "line" or "line_markers" => "line",
                "area" or "stacked_area" => "area",
                "pie" => "pie",
                "doughnut" => "doughnut",
                "scatter" => "scatter",
                "radar" => "radar",
                _ => "bar",
            },
            Horizontal = kind is "bar" or "stacked_bar" or "percent_bar",
            Grouping = kind switch
            {
                "stacked_column" or "stacked_bar" or "stacked_area" => "stacked",
                "percent_column" or "percent_bar" => "percentStacked",
                "line" or "line_markers" or "area" => "standard",
                _ => "clustered",
            },
            Markers = kind == "line_markers",
            Title = spec.Title ?? current?.Title,
            CategoryAxisTitle = spec.CategoryAxisTitle ?? current?.CategoryAxisTitle,
            ValueAxisTitle = spec.ValueAxisTitle ?? current?.ValueAxisTitle,
            NumberFormat = style?.NumberFormat ?? current?.NumberFormat,
            Font = style?.Font ?? current?.Font,
            FontSize = style?.FontSize ?? 12,
            TextColor = style?.TextColor ?? (current?.TextColor is { } textColor ? "#" + textColor : null),
            Palette = style?.Colors?.ToList() ?? [],
        };

        if (string.IsNullOrEmpty(definition.Title))
        {
            definition.Title = null;
        }

        definition.Categories = (spec.Categories ?? current?.Categories ?? []).Select(category => category ?? string.Empty).ToList();

        if (spec.Series is { Count: > 0 })
        {
            definition.Series = spec.Series
                .Where(series => series is not null)
                .Select(series => new OpenXmlChartSeriesDefinition
                {
                    Name = series.Name,
                    Values = series.Values?.ToList() ?? [],
                    XValues = series.XValues?.ToList() ?? [],
                    Color = series.Color,
                })
                .ToList();
        }
        else if (current is not null)
        {
            definition.Series = current.Series
                .Select(series => new OpenXmlChartSeriesDefinition
                {
                    Name = series.Name,
                    Values = [.. series.Values],
                    XValues = [.. series.XValues],
                    Color = series.Color is null ? null : "#" + series.Color,
                })
                .ToList();
        }

        if (definition.Palette.Count == 0 && current is not null && definition.Plot is "pie" or "doughnut" && current.Series.Count > 0)
        {
            definition.Palette = current.Series[0].PointColors.Where(color => color is not null).Select(color => "#" + color).ToList();
        }

        if (definition.Plot is "pie" or "doughnut" && definition.Series.Count > 1)
        {
            _result.Warnings.Add($"A {definition.Plot} chart shows one series; only \"{definition.Series[0].Name}\" is drawn.");
            definition.Series = [definition.Series[0]];
        }

        if (definition.Categories.Count > 0)
        {
            foreach (var series in definition.Series)
            {
                if (series.Values.Count != definition.Categories.Count && definition.Plot != "scatter")
                {
                    _result.Warnings.Add($"The series \"{series.Name}\" has {series.Values.Count.ToString(CultureInfo.InvariantCulture)} value(s) for {definition.Categories.Count.ToString(CultureInfo.InvariantCulture)} categories.");
                }
            }
        }

        if (OpenXmlChartWriter.PointCount(definition) > 60)
        {
            _result.Warnings.Add($"The chart plots {OpenXmlChartWriter.PointCount(definition).ToString(CultureInfo.InvariantCulture)} points, which will be hard to read on a slide; consider showing only the top categories.");
        }

        var legend = style?.Legend?.Trim().ToLowerInvariant();
        definition.LegendPosition = legend switch
        {
            "none" or "hide" or "hidden" or "off" => null,
            "top" => "t",
            "left" => "l",
            "right" => "r",
            "bottom" => "b",
            _ => current?.ShowLegend == true
                ? current.LegendPosition switch { "top" => "t", "left" => "l", "right" => "r", _ => "b" }
                : definition.Series.Count > 1 || definition.Plot is "pie" or "doughnut" ? "b" : null,
        };

        definition.DataLabels = style?.DataLabels ?? current?.ShowDataLabels ?? definition.Plot is "pie" or "doughnut";
        definition.Gridlines = style?.Gridlines ?? true;

        return definition;
    }

    private static string KindName(PresentationChart chart)
    {
        if (chart is null)
        {
            return null;
        }

        return chart.Kind switch
        {
            "column" => chart.PercentStacked ? "percent_column" : chart.Stacked ? "stacked_column" : "column",
            "bar" => chart.PercentStacked ? "percent_bar" : chart.Stacked ? "stacked_bar" : "bar",
            "area" => chart.Stacked ? "stacked_area" : "area",
            "line" or "pie" or "doughnut" or "scatter" or "radar" => chart.Kind,
            _ => null,
        };
    }

    private void UpdateChart(UpdateChartEdit edit)
    {
        var slidePart = GetSlide(edit.Slide);
        var frame = FindGraphic(slidePart, edit.Element, edit.Slide, OpenXmlPresentationConstants.ChartGraphicUri, "chart");
        var relationshipId = OpenXmlMarkup.RelationshipAttribute(OpenXmlMarkup.Path(frame, "graphic", "graphicData", "chart"), "id");

        if (relationshipId is null || !slidePart.TryGetPartById(relationshipId, out var part) || part is not ChartPart chartPart)
        {
            throw new PresentationEditException("The chart's data could not be found in the deck.");
        }

        var context = OpenXmlSlideContext.ForSlide(slidePart, _themes);
        var current = OpenXmlChartReader.Read(chartPart, context);
        var spec = edit.Chart ?? new PresentationChartSpec();
        var requestedKind = spec.Kind?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        var currentKind = KindName(current);

        // A chart of another kind is rebuilt from the data it has; any other change is made in place so a
        // chart that came with the deck keeps the look its author gave it.
        if ((requestedKind is not null && requestedKind != currentKind) || currentKind is null)
        {
            WriteChartPart(chartPart, Define(spec, spec.Style, current));
            MarkChanged(slidePart);
            _result.Changes.Add($"Rebuilt chart {ElementId(frame).ToString(CultureInfo.InvariantCulture)} on slide {edit.Slide.ToString(CultureInfo.InvariantCulture)} as a {requestedKind ?? "column"} chart.");

            return;
        }

        var document = XDocument.Parse(chartPart.ChartSpace.OuterXml);
        var root = document.Root;
        var chartElement = root.Element(_c + "chart");
        var plotArea = chartElement?.Element(_c + "plotArea");
        var plot = plotArea?.Elements().FirstOrDefault(element => element.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal));

        if (plot is null)
        {
            throw new PresentationEditException("The chart has no plot to change.");
        }

        var definition = Define(spec, spec.Style, current);
        var changes = new List<string>();

        if (spec.Title is not null)
        {
            SetChartTitle(chartElement, spec.Title, definition);
            changes.Add(spec.Title.Length == 0 ? "removed the title" : "set the title");
        }

        if (spec.Categories is not null || spec.Series is not null)
        {
            ReplaceSeries(plot, definition);
            RefreshWorkbook(chartPart, root, definition);
            changes.Add($"replaced the data ({definition.Series.Count.ToString(CultureInfo.InvariantCulture)} series, {OpenXmlChartWriter.PointCount(definition).ToString(CultureInfo.InvariantCulture)} points)");
        }

        if (spec.CategoryAxisTitle is not null)
        {
            SetAxisTitle(plotArea.Element(_c + "catAx") ?? plotArea.Elements(_c + "valAx").FirstOrDefault(), spec.CategoryAxisTitle, definition, rotated: definition.Horizontal);
            changes.Add("set the category axis title");
        }

        if (spec.ValueAxisTitle is not null)
        {
            SetAxisTitle(plotArea.Elements(_c + "valAx").LastOrDefault(), spec.ValueAxisTitle, definition, rotated: !definition.Horizontal);
            changes.Add("set the value axis title");
        }

        if (spec.Style is { } style)
        {
            ApplyChartStyle(root, chartElement, plotArea, plot, style, definition);
            changes.Add("restyled it");
        }

        if (changes.Count == 0)
        {
            throw new PresentationEditException("Nothing to change was given for the chart.");
        }

        chartPart.ChartSpace = new C.ChartSpace(root.ToString(SaveOptions.DisableFormatting));
        MarkChanged(slidePart);
        _result.Changes.Add($"On slide {edit.Slide.ToString(CultureInfo.InvariantCulture)}, chart {ElementId(frame).ToString(CultureInfo.InvariantCulture)}: {string.Join(", ", changes)}.");
    }

    private static XElement Fragment(string markup)
    {
        var wrapper = XElement.Parse("<root xmlns:c=\"" + _c.NamespaceName + "\" xmlns:a=\"" + _a.NamespaceName + "\" xmlns:r=\"" + OpenXmlPresentationConstants.RelationshipsNamespace + "\">" + markup + "</root>");

        return wrapper.Elements().First();
    }

    private static void SetChartTitle(XElement chartElement, string title, OpenXmlChartDefinition definition)
    {
        chartElement.Element(_c + "title")?.Remove();
        chartElement.Element(_c + "autoTitleDeleted")?.Remove();

        if (title.Length == 0)
        {
            chartElement.AddFirst(Fragment("<c:autoTitleDeleted val=\"1\"/>"));

            return;
        }

        chartElement.AddFirst(Fragment("<c:autoTitleDeleted val=\"0\"/>"));
        chartElement.AddFirst(Fragment(OpenXmlChartWriter.Title(title, definition, definition.FontSize + 4, rotated: false)));
    }

    private static void SetAxisTitle(XElement axis, string title, OpenXmlChartDefinition definition, bool rotated)
    {
        if (axis is null)
        {
            return;
        }

        axis.Element(_c + "title")?.Remove();

        if (title.Length == 0)
        {
            return;
        }

        InsertBefore(axis, Fragment(OpenXmlChartWriter.Title(title, definition, definition.FontSize, rotated)), "numFmt", "majorTickMark", "minorTickMark", "tickLblPos", "spPr", "txPr", "crossAx");
    }

    private static void InsertBefore(XElement parent, XElement child, params string[] followers)
    {
        var anchor = parent.Elements().FirstOrDefault(element => followers.Contains(element.Name.LocalName));

        if (anchor is null)
        {
            parent.Add(child);
        }
        else
        {
            anchor.AddBeforeSelf(child);
        }
    }

    /// <summary>
    /// Replaces a chart's series with new data, cloning the existing series so they keep their formatting.
    /// </summary>
    private static void ReplaceSeries(XElement plot, OpenXmlChartDefinition definition)
    {
        var templates = plot.Elements(_c + "ser").ToList();

        if (templates.Count == 0)
        {
            throw new PresentationEditException("The chart has no series to take new data.");
        }

        var count = OpenXmlChartWriter.PointCount(definition);
        var isScatter = plot.Name.LocalName == "scatterChart";
        var created = new List<XElement>();

        for (var index = 0; index < definition.Series.Count; index++)
        {
            var series = definition.Series[index];
            var element = new XElement(templates[Math.Min(index, templates.Count - 1)]);
            var column = OpenXmlChartWriter.ColumnName(index + 1);
            var name = string.IsNullOrWhiteSpace(series.Name) ? "Series " + (index + 1).ToString(CultureInfo.InvariantCulture) : series.Name;

            element.Element(_c + "idx")?.SetAttributeValue("val", index);
            element.Element(_c + "order")?.SetAttributeValue("val", index);

            var text = Fragment("<c:tx><c:strRef><c:f>" + OpenXmlChartWriter.SheetName + "!$" + column + "$1</c:f><c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>" + PresentationTemplateXml.Escape(name) + "</c:v></c:pt></c:strCache></c:strRef></c:tx>");
            ReplaceOrInsert(element, text, "order");

            var format = element.Descendants(_c + "formatCode").FirstOrDefault()?.Value;
            format = definition.NumberFormat ?? (format == "General" ? null : format);

            if (isScatter)
            {
                var x = series.XValues.Count > 0 ? series.XValues : Enumerable.Range(1, count).Select(value => (double?)value).ToList();
                ReplaceOrInsert(element, Fragment("<c:xVal>" + OpenXmlChartWriter.NumberReference(0, x, count, null) + "</c:xVal>"), "dLbls", "errBars", "trendline", "dPt", "marker", "spPr", "tx");
                ReplaceOrInsert(element, Fragment("<c:yVal>" + OpenXmlChartWriter.NumberReference(index + 1, series.Values, count, format) + "</c:yVal>"), "xVal");
            }
            else
            {
                ReplaceOrInsert(element, Fragment("<c:cat>" + OpenXmlChartWriter.StringReference(definition.Categories, count) + "</c:cat>"), "errBars", "trendline", "dLbls", "dPt", "pictureOptions", "invertIfNegative", "marker", "explosion", "spPr", "tx");
                ReplaceOrInsert(element, Fragment("<c:val>" + OpenXmlChartWriter.NumberReference(index + 1, series.Values, count, format) + "</c:val>"), "cat");
            }

            // A point past the end of the new data would describe nothing.
            foreach (var point in element.Elements(_c + "dPt").ToList())
            {
                if (int.TryParse(point.Element(_c + "idx")?.Attribute("val")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pointIndex) && pointIndex >= count)
                {
                    point.Remove();
                }
            }

            if (!string.IsNullOrWhiteSpace(series.Color))
            {
                SetSeriesColor(element, plot.Name.LocalName, series.Color);
            }

            created.Add(element);
        }

        templates[0].AddBeforeSelf(created);

        foreach (var template in templates)
        {
            template.Remove();
        }
    }

    private static void ReplaceOrInsert(XElement parent, XElement child, params string[] predecessors)
    {
        var existing = parent.Element(child.Name);

        if (existing is not null)
        {
            existing.ReplaceWith(child);

            return;
        }

        foreach (var predecessor in predecessors)
        {
            var anchor = parent.Elements().LastOrDefault(element => element.Name.LocalName == predecessor);

            if (anchor is not null)
            {
                anchor.AddAfterSelf(child);

                return;
            }
        }

        parent.Add(child);
    }

    private static void SetSeriesColor(XElement series, string plot, string color)
    {
        var colorXml = OpenXmlChartWriter.ColorXml(color);
        var properties = series.Element(_c + "spPr");

        if (properties is null)
        {
            properties = Fragment("<c:spPr/>");
            ReplaceOrInsert(series, properties, "tx", "order");
        }

        if (plot is "lineChart" or "radarChart" or "scatterChart")
        {
            var line = properties.Element(_a + "ln");

            if (line is null)
            {
                line = Fragment("<a:ln w=\"28575\"/>");
                properties.Add(line);
            }

            line.Elements().Where(element => element.Name.LocalName is "noFill" or "solidFill" or "gradFill").Remove();
            line.AddFirst(Fragment("<a:solidFill>" + colorXml + "</a:solidFill>"));

            return;
        }

        properties.Elements().Where(element => element.Name.LocalName is "noFill" or "solidFill" or "gradFill" or "pattFill").Remove();
        properties.AddFirst(Fragment("<a:solidFill>" + colorXml + "</a:solidFill>"));
    }

    private static void RefreshWorkbook(ChartPart chartPart, XElement root, OpenXmlChartDefinition definition)
    {
        var workbook = chartPart.EmbeddedPackagePart;

        if (workbook is null)
        {
            workbook = chartPart.AddNewPart<EmbeddedPackagePart>(OpenXmlPresentationConstants.WorkbookContentType, OpenXmlSchemaOrder.NextRelationshipId(chartPart));
            root.Element(_c + "externalData")?.Remove();

            var external = Fragment("<c:externalData r:id=\"" + chartPart.GetIdOfPart(workbook) + "\"><c:autoUpdate val=\"0\"/></c:externalData>");
            var anchor = root.Elements().FirstOrDefault(element => element.Name.LocalName is "printSettings" or "userShapes" or "extLst");

            if (anchor is null)
            {
                root.Add(external);
            }
            else
            {
                anchor.AddBeforeSelf(external);
            }
        }

        using var stream = new MemoryStream(OpenXmlChartWriter.BuildWorkbook(definition));
        workbook.FeedData(stream);
    }

    private static void ApplyChartStyle(XElement root, XElement chartElement, XElement plotArea, XElement plot, PresentationChartStyle style, OpenXmlChartDefinition definition)
    {
        if (style.Legend is not null)
        {
            chartElement.Element(_c + "legend")?.Remove();

            if (definition.LegendPosition is { } position)
            {
                plotArea.AddAfterSelf(Fragment("<c:legend><c:legendPos val=\"" + position + "\"/><c:overlay val=\"0\"/>" + OpenXmlChartWriter.TextProperties(definition, definition.FontSize, bold: false) + "</c:legend>"));
            }
        }

        if (style.DataLabels is { } labels)
        {
            plot.Element(_c + "dLbls")?.Remove();

            foreach (var series in plot.Elements(_c + "ser"))
            {
                series.Element(_c + "dLbls")?.Remove();
            }

            if (labels)
            {
                var markup = "<c:dLbls>" +
                    (string.IsNullOrWhiteSpace(definition.NumberFormat) ? string.Empty : "<c:numFmt formatCode=\"" + PresentationTemplateXml.Escape(definition.NumberFormat) + "\" sourceLinked=\"0\"/>") +
                    "<c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr>" + OpenXmlChartWriter.TextProperties(definition, Math.Max(8, definition.FontSize - 1), bold: false) +
                    "<c:showLegendKey val=\"0\"/><c:showVal val=\"1\"/><c:showCatName val=\"0\"/><c:showSerName val=\"0\"/><c:showPercent val=\"0\"/><c:showBubbleSize val=\"0\"/></c:dLbls>";

                var lastSeries = plot.Elements(_c + "ser").LastOrDefault();

                if (lastSeries is null)
                {
                    plot.AddFirst(Fragment(markup));
                }
                else
                {
                    lastSeries.AddAfterSelf(Fragment(markup));
                }
            }
        }

        if (style.Gridlines is { } gridlines)
        {
            foreach (var axis in plotArea.Elements(_c + "valAx"))
            {
                axis.Element(_c + "majorGridlines")?.Remove();

                if (gridlines && axis == plotArea.Elements(_c + "valAx").Last())
                {
                    axis.Element(_c + "axPos")?.AddAfterSelf(Fragment("<c:majorGridlines><c:spPr><a:ln w=\"9525\"><a:solidFill><a:schemeClr val=\"tx1\"><a:lumMod val=\"15000\"/><a:lumOff val=\"85000\"/></a:schemeClr></a:solidFill></a:ln></c:spPr></c:majorGridlines>"));
                }
            }
        }

        if (style.Colors is { Count: > 0 } colors)
        {
            var index = 0;

            foreach (var series in plot.Elements(_c + "ser"))
            {
                if (plot.Name.LocalName is "pieChart" or "doughnutChart" or "pie3DChart")
                {
                    foreach (var point in series.Elements(_c + "dPt"))
                    {
                        var pointIndex = int.TryParse(point.Element(_c + "idx")?.Attribute("val")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
                        var properties = point.Element(_c + "spPr");

                        if (properties is null)
                        {
                            continue;
                        }

                        properties.Elements().Where(element => element.Name.LocalName is "noFill" or "solidFill" or "gradFill" or "pattFill").Remove();
                        properties.AddFirst(Fragment("<a:solidFill>" + OpenXmlChartWriter.ColorXml(colors[pointIndex % colors.Count]) + "</a:solidFill>"));
                    }
                }
                else
                {
                    SetSeriesColor(series, plot.Name.LocalName, colors[index % colors.Count]);
                }

                index++;
            }
        }

        if (style.Font is not null || style.FontSize is not null || style.TextColor is not null)
        {
            // The chart's own text properties apply to every label that does not set its own, so the
            // per-label overrides are cleared and the one at the top is replaced.
            foreach (var textProperties in root.Descendants(_c + "txPr").ToList())
            {
                textProperties.Remove();
            }

            var anchor = root.Elements().FirstOrDefault(element => element.Name.LocalName is "externalData" or "printSettings" or "userShapes" or "extLst");
            var markup = Fragment(OpenXmlChartWriter.TextProperties(definition, definition.FontSize, bold: false));

            if (anchor is null)
            {
                root.Add(markup);
            }
            else
            {
                anchor.AddBeforeSelf(markup);
            }
        }

        if (style.NumberFormat is not null)
        {
            foreach (var axis in plotArea.Elements(_c + "valAx"))
            {
                axis.Element(_c + "numFmt")?.Remove();
                InsertBefore(axis, Fragment("<c:numFmt formatCode=\"" + PresentationTemplateXml.Escape(style.NumberFormat) + "\" sourceLinked=\"0\"/>"), "majorTickMark", "minorTickMark", "tickLblPos", "spPr", "txPr", "crossAx");
            }

            foreach (var code in plot.Descendants(_c + "formatCode"))
            {
                code.Value = style.NumberFormat;
            }
        }
    }
}
