using System.Text.Json;
using CrestApps.Core.AI.Documents.Tooling;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Tests.Core.Documents.Knowledge;

/// <summary>
/// Verifies the marker that turns a chart read out of a document into one the reader can look at.
/// </summary>
/// <remarks>
/// The marker is the host's existing contract, so what matters here is that only a chart worth drawing
/// produces one, and that what it produces is a Chart.js config rather than something shaped like one.
/// </remarks>
public sealed class KnowledgeChartMarkerTests
{
    [Fact]
    public void TryBuild_AtExactConfidence_ProducesAMarker()
    {
        var marker = KnowledgeChartMarker.TryBuild(CreateChart(ChartValueConfidence.Exact));

        Assert.NotNull(marker);
        Assert.StartsWith("[chart:", marker, StringComparison.Ordinal);
        Assert.EndsWith("]", marker, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ChartValueConfidence.Descriptive)]
    [InlineData("Approximate")]
    [InlineData(null)]
    public void TryBuild_BelowExactConfidence_DrawsNothing(string confidence)
    {
        // A canvas presents whatever it is given as measured data; there is no way to plot a value and have
        // it look like an estimate. The gate is applied here as well as where the numbers are printed.
        Assert.Null(KnowledgeChartMarker.TryBuild(CreateChart(confidence)));
    }

    [Fact]
    public void TryBuild_WithNoSeries_DrawsNothing()
    {
        var chart = CreateChart(ChartValueConfidence.Exact);
        chart.Series = [];

        Assert.Null(KnowledgeChartMarker.TryBuild(chart));
    }

    [Fact]
    public void TryBuild_WithSeriesButNoPoints_DrawsNothing()
    {
        var chart = CreateChart(ChartValueConfidence.Exact);
        chart.Series = [new ChartSeries { Name = "Measured", Points = [] }];

        Assert.Null(KnowledgeChartMarker.TryBuild(chart));
    }

    [Fact]
    public void TryBuild_WithNothingAtAll_DrawsNothing()
    {
        Assert.Null(KnowledgeChartMarker.TryBuild(null));
    }

    [Fact]
    public void TryBuild_ProducesAConfigTheRendererAccepts()
    {
        // The renderer requires "type" and "data"; a config missing either is refused and the marker reaches
        // the reader as the text the model typed.
        using var document = JsonDocument.Parse(ExtractJson(KnowledgeChartMarker.TryBuild(CreateChart(ChartValueConfidence.Exact))));

        Assert.True(document.RootElement.TryGetProperty("type", out var type));
        Assert.True(document.RootElement.TryGetProperty("data", out var data));

        // Scatter rather than line: the points carry their own x values read off the axis, and a line chart
        // would space them evenly and quietly redraw the data.
        Assert.Equal("scatter", type.GetString());

        var datasets = data.GetProperty("datasets");

        Assert.Equal(1, datasets.GetArrayLength());

        var points = datasets[0].GetProperty("data");

        Assert.Equal(2, points.GetArrayLength());
        Assert.Equal(1990, points[0].GetProperty("x").GetDouble());
        Assert.Equal(3.5, points[0].GetProperty("y").GetDouble());
    }

    [Fact]
    public void TryBuild_CarriesTheAxisTitlesTheDocumentPrinted()
    {
        using var document = JsonDocument.Parse(ExtractJson(KnowledgeChartMarker.TryBuild(CreateChart(ChartValueConfidence.Exact))));

        var scales = document.RootElement.GetProperty("options").GetProperty("scales");

        Assert.Equal("Year", scales.GetProperty("x").GetProperty("title").GetProperty("text").GetString());
        Assert.Equal("W/m²K", scales.GetProperty("y").GetProperty("title").GetProperty("text").GetString());
    }

    [Fact]
    public void TryBuild_WhenNoSeriesWasNamed_HidesTheLegend()
    {
        var chart = CreateChart(ChartValueConfidence.Exact);
        chart.Series = [new ChartSeries { Name = null, Points = [[1, 2], [3, 4]] }];

        using var document = JsonDocument.Parse(ExtractJson(KnowledgeChartMarker.TryBuild(chart)));

        // Naming a series would mean reading a legend, which is not done. A legend drawn over unnamed series
        // says "Dataset 1" with the authority of a printed key, so none is drawn at all.
        Assert.False(document.RootElement
            .GetProperty("options").GetProperty("plugins").GetProperty("legend").GetProperty("display").GetBoolean());

        Assert.False(document.RootElement
            .GetProperty("data").GetProperty("datasets")[0].TryGetProperty("label", out _));
    }

    [Fact]
    public void TryBuild_WhenASeriesWasNamed_ShowsTheLegend()
    {
        using var document = JsonDocument.Parse(ExtractJson(KnowledgeChartMarker.TryBuild(CreateChart(ChartValueConfidence.Exact))));

        Assert.True(document.RootElement
            .GetProperty("options").GetProperty("plugins").GetProperty("legend").GetProperty("display").GetBoolean());

        Assert.Equal("Measured", document.RootElement
            .GetProperty("data").GetProperty("datasets")[0].GetProperty("label").GetString());
    }

    [Fact]
    public void TryBuild_SkipsAMalformedPointRatherThanPlottingIt()
    {
        var chart = CreateChart(ChartValueConfidence.Exact);
        chart.Series = [new ChartSeries { Name = "Measured", Points = [[1], [2, 3]] }];

        using var document = JsonDocument.Parse(ExtractJson(KnowledgeChartMarker.TryBuild(chart)));

        var points = document.RootElement.GetProperty("data").GetProperty("datasets")[0].GetProperty("data");

        Assert.Equal(1, points.GetArrayLength());
        Assert.Equal(2, points[0].GetProperty("x").GetDouble());
    }

    private static string ExtractJson(string marker)
    {
        Assert.NotNull(marker);

        return marker["[chart:".Length..^1];
    }

    private static ChartDetails CreateChart(string confidence)
    {
        return new ChartDetails
        {
            ChartType = ChartTypes.Line,
            ValueConfidence = confidence,
            AxisX = "Year",
            AxisY = "W/m²K",
            Series =
            [
                new ChartSeries
                {
                    Name = "Measured",
                    Points = [[1990, 3.5], [2000, 2.1]],
                },
            ],
        };
    }
}
