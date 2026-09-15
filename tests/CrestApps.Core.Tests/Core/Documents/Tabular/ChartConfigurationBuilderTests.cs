using System.Text.Json;
using CrestApps.Core.AI.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class ChartConfigurationBuilderTests
{
    /// <summary>
    /// Verifies that the supplied values reach the configuration exactly. The prose path this replaces
    /// asked a second model to restate the numbers, which is where precision was lost.
    /// </summary>
    [Fact]
    public void Build_SuppliedValues_AreCarriedThroughExactly()
    {
        var json = ChartConfigurationBuilder.Build(
            "bar",
            "Amount by region",
            ["North", "South"],
            [new ChartConfigurationBuilder.ChartSeries("Amount", [1250.75, -400.5])]);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("bar", root.GetProperty("type").GetString());
        Assert.Equal(["North", "South"], root.GetProperty("data").GetProperty("labels")
            .EnumerateArray().Select(value => value.GetString()));

        var data = root.GetProperty("data").GetProperty("datasets")[0].GetProperty("data");

        Assert.Equal(1250.75, data[0].GetDouble());
        Assert.Equal(-400.5, data[1].GetDouble());
    }

    /// <summary>
    /// A chart built from hundreds of points must still be valid JSON. The previous path capped the
    /// model's output, so a large chart came back truncated and unparsable.
    /// </summary>
    [Fact]
    public void Build_LargeSeries_ProducesValidConfiguration()
    {
        var labels = Enumerable.Range(0, 500).Select(index => $"Category {index}").ToList();
        var values = Enumerable.Range(0, 500).Select(index => (double?)(index * 1.5)).ToList();

        var json = ChartConfigurationBuilder.Build(
            "bar",
            "Large",
            labels,
            [new ChartConfigurationBuilder.ChartSeries("Values", values)]);

        using var document = JsonDocument.Parse(json);

        Assert.Equal(500, document.RootElement.GetProperty("data").GetProperty("labels").GetArrayLength());
    }

    /// <summary>
    /// Verifies that sign coloring assigns a color per point, which is what makes a variance chart
    /// readable without inspecting the numbers.
    /// </summary>
    [Fact]
    public void Build_ColorBySign_ColorsEachPoint()
    {
        var json = ChartConfigurationBuilder.Build(
            "bar",
            "Variance",
            ["A", "B", "C"],
            [new ChartConfigurationBuilder.ChartSeries("Variance", [10, -20, 30])],
            colorBySign: true);

        using var document = JsonDocument.Parse(json);
        var colors = document.RootElement.GetProperty("data").GetProperty("datasets")[0]
            .GetProperty("backgroundColor")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToList();

        Assert.Equal(3, colors.Count);
        Assert.Equal(colors[0], colors[2]);
        Assert.NotEqual(colors[0], colors[1]);
    }

    /// <summary>
    /// Verifies that a horizontal bar chart sets the axis that flips it, since long category labels are
    /// unreadable on a vertical chart.
    /// </summary>
    [Fact]
    public void Build_HorizontalBar_SetsIndexAxis()
    {
        var json = ChartConfigurationBuilder.Build(
            "bar",
            null,
            ["A"],
            [new ChartConfigurationBuilder.ChartSeries("Values", [1])],
            horizontal: true);

        using var document = JsonDocument.Parse(json);

        Assert.Equal("y", document.RootElement.GetProperty("options").GetProperty("indexAxis").GetString());
    }

    /// <summary>
    /// Verifies that a missing point is written as a gap rather than as zero, which would misrepresent
    /// the data.
    /// </summary>
    [Fact]
    public void Build_MissingValue_IsWrittenAsGap()
    {
        var json = ChartConfigurationBuilder.Build(
            "line",
            null,
            ["A", "B"],
            [new ChartConfigurationBuilder.ChartSeries("Values", [1, null])]);

        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data").GetProperty("datasets")[0].GetProperty("data");

        Assert.Equal(JsonValueKind.Null, data[1].ValueKind);
    }

    /// <summary>
    /// Verifies that a pie chart colors each slice, since one color across every slice would make it
    /// unreadable.
    /// </summary>
    [Fact]
    public void Build_PieChart_ColorsEachSlice()
    {
        var json = ChartConfigurationBuilder.Build(
            "pie",
            "Share",
            ["A", "B", "C"],
            [new ChartConfigurationBuilder.ChartSeries("Share", [1, 2, 3])]);

        using var document = JsonDocument.Parse(json);
        var colors = document.RootElement.GetProperty("data").GetProperty("datasets")[0]
            .GetProperty("backgroundColor");

        Assert.Equal(3, colors.GetArrayLength());
    }

    /// <summary>
    /// Verifies that the rendered configuration always carries the two members the client requires, so
    /// a built chart can never be rejected as incomplete.
    /// </summary>
    [Theory]
    [InlineData("bar")]
    [InlineData("line")]
    [InlineData("pie")]
    [InlineData("doughnut")]
    [InlineData("radar")]
    [InlineData("unrecognized")]
    public void Build_AnyChartType_CarriesRequiredMembers(string chartType)
    {
        var json = ChartConfigurationBuilder.Build(
            chartType,
            "Title",
            ["A", "B"],
            [new ChartConfigurationBuilder.ChartSeries("Values", [1, 2])]);

        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("type", out _));
        Assert.True(document.RootElement.TryGetProperty("data", out _));
    }
}
