using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Knowledge;
using CrestApps.Core.AI.Ingestion.Pdf;
using CrestApps.Core.AI.Ingestion.Pdf.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Ingestion.Pdf;

/// <summary>
/// Covers what a page draws rather than what it places: ruled tables, figures built out of vector geometry,
/// and the series a chart's own geometry states exactly.
/// </summary>
public sealed class PdfTableAndChartTests
{
    /// <summary>
    /// Verifies that a drawn three-by-three grid is read as a table with nine cells, each holding the text
    /// printed inside it.
    /// </summary>
    [Fact]
    public async Task Read_RuledGrid_YieldsTableWithEveryCell()
    {
        var fixture = new PdfFixtureBuilder().Page(page =>
        {
            // Four horizontal and four vertical rules make a three-by-three grid.
            for (var index = 0; index < 4; index++)
            {
                var y = 600 + (index * 40);

                page.Line(100, y, 400, y);
            }

            for (var index = 0; index < 4; index++)
            {
                var x = 100 + (index * 100);

                page.Line(x, 600, x, 720);
            }

            var values = new[]
            {
                new[] { "Material", "Strength", "Temper" },
                new[] { "Steel", "400", "Hard" },
                new[] { "Copper", "220", "Soft" },
            };

            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    page.Text(values[row][column], 110 + (column * 100), 690 - (row * 40));
                }
            }
        });

        var document = await ReadAsync(fixture);

        var table = Assert.Single(document.EnumerateContent().OfType<IngestionDocumentTable>());

        Assert.Equal(3, table.Cells.GetLength(0));
        Assert.Equal(3, table.Cells.GetLength(1));

        var texts = new List<string>();

        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                texts.Add(table.Cells[row, column].Text);
            }
        }

        Assert.Equal(9, texts.Count);
        Assert.Contains("Material", texts);
        Assert.Contains("Steel", texts);
        Assert.Contains("400", texts);
        Assert.Contains("Soft", texts);
    }

    /// <summary>
    /// Verifies that two tables on one page are read as two tables, each with its own cells. Rules that never
    /// meet belong to different grids; one grid stretched over both would put a band of nonsense between them
    /// and shift every value below it into the wrong row.
    /// </summary>
    [Fact]
    public async Task Read_TwoRuledTablesOnOnePage_YieldsTwoTables()
    {
        var fixture = new PdfFixtureBuilder().Page(page =>
        {
            // An upper two-by-two grid and, well below it, a lower two-by-two grid.
            DrawGrid(page, left: 100, bottom: 640, cellWidth: 100, cellHeight: 40, columns: 2, rows: 2);
            DrawGrid(page, left: 100, bottom: 300, cellWidth: 100, cellHeight: 40, columns: 2, rows: 2);

            page.Text("Alpha", 110, 690);
            page.Text("Bravo", 210, 690);
            page.Text("Charlie", 110, 650);
            page.Text("Delta", 210, 650);

            page.Text("Echo", 110, 350);
            page.Text("Foxtrot", 210, 350);
            page.Text("Golf", 110, 310);
            page.Text("Hotel", 210, 310);
        });

        var document = await ReadAsync(fixture);

        var tables = document.EnumerateContent().OfType<IngestionDocumentTable>().ToList();

        Assert.Equal(2, tables.Count);

        var upper = tables[0];
        var lower = tables[1];

        Assert.Equal("Alpha", upper.Cells[0, 0].Text);
        Assert.Equal("Delta", upper.Cells[1, 1].Text);
        Assert.Equal("Echo", lower.Cells[0, 0].Text);
        Assert.Equal("Hotel", lower.Cells[1, 1].Text);
    }

    /// <summary>
    /// Verifies that a chart the page draws becomes a figure with a picture, even though the file contains no
    /// image at all.
    /// </summary>
    [Fact]
    public async Task Read_VectorChart_YieldsFigureWithRenderedPicture()
    {
        var document = await ReadAsync(BuildChart());

        var figure = Assert.Single(
            document.EnumerateContent().OfType<IngestionDocumentImage>(),
            image => image.HasMetadata && image.Metadata.ContainsKey(FigureMetadataKeys.IsVectorFigure));

        Assert.Equal("image/png", figure.MediaType);
        Assert.NotNull(figure.Content);
        Assert.True(figure.Content!.Value.Length > 0);

        // A PNG always starts with the same eight bytes.
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], figure.Content.Value[..8].ToArray());
    }

    /// <summary>
    /// Verifies that a drawn polyline with labelled ticks yields the values it actually draws, within one
    /// percent, rather than an estimate of them.
    /// </summary>
    [Fact]
    public async Task Read_VectorChartWithTickLabels_YieldsExactPoints()
    {
        var document = await ReadAsync(BuildChart());

        var figure = Assert.Single(
            document.EnumerateContent().OfType<IngestionDocumentImage>(),
            image => image.HasMetadata && image.Metadata.ContainsKey(FigureMetadataKeys.IsVectorFigure));

        Assert.Equal(ChartValueConfidence.Exact, figure.GetMetadataString(FigureMetadataKeys.ValueConfidence));

        var raw = figure.GetMetadataString(FigureMetadataKeys.ChartSeries);

        Assert.NotNull(raw);

        var series = System.Text.Json.JsonSerializer.Deserialize<List<ChartSeries>>(raw);
        var points = Assert.Single(series).Points;

        // The polyline is drawn through (0, 0), (10, 100) and (20, 50) in chart units.
        Assert.Contains(points, point => Near(point[0], 0) && Near(point[1], 0));
        Assert.Contains(points, point => Near(point[0], 10) && Near(point[1], 100));
        Assert.Contains(points, point => Near(point[0], 20) && Near(point[1], 50));
    }

    /// <summary>
    /// Verifies that a chart of two lines comes back as two series rather than one.
    /// </summary>
    /// <remarks>
    /// Every value in a merged series is real, which is exactly what makes it dangerous. A line zig-zagging
    /// between the two the chart draws is attributed to a series the document never plotted, and nothing
    /// about it reads as an estimate, so no reader downstream has any reason to doubt it.
    /// </remarks>
    [Fact]
    public async Task Read_VectorChartWithTwoPolylines_YieldsOneSeriesPerLine()
    {
        var fixture = new PdfFixtureBuilder().Page(page =>
        {
            DrawAxes(page);

            // A lower line from (0, 10) to (20, 50) and an upper one from (0, 90) to (20, 70). They share no
            // point, so nothing joins them.
            DrawPolyline(page, 150, 430, 450, 550);
            DrawPolyline(page, 150, 670, 450, 610);
        });

        var series = ReadChartSeries(await ReadAsync(fixture));

        Assert.Equal(2, series.Count);

        // Each series stays on its own side of the plot. One list holding values from both would satisfy
        // neither.
        var lower = Assert.Single(series, entry => entry.Points.All(point => point[1] < 60));
        var upper = Assert.Single(series, entry => entry.Points.All(point => point[1] > 60));

        Assert.Contains(lower.Points, point => Near(point[0], 0) && Near(point[1], 10));
        Assert.Contains(lower.Points, point => Near(point[0], 20) && Near(point[1], 50));
        Assert.Contains(upper.Points, point => Near(point[0], 0) && Near(point[1], 90));
        Assert.Contains(upper.Points, point => Near(point[0], 20) && Near(point[1], 70));

        // The legend is not read, so no series is named. A name arrived at by guesswork credits the values
        // to something the document never said.
        Assert.All(series, entry => Assert.Null(entry.Name));
    }

    /// <summary>
    /// Verifies that the markers drawn on a line are not reported as series of their own. A diamond is four
    /// sloped edges, so splitting the figure by polyline and stopping there would report one series per data
    /// point on top of the line they sit on.
    /// </summary>
    [Fact]
    public async Task Read_VectorChartWithMarkers_YieldsOnlyTheLine()
    {
        var fixture = new PdfFixtureBuilder().Page(page =>
        {
            DrawAxes(page);

            // One line from (0, 10) to (20, 50), carrying a marker on three of its points.
            DrawPolyline(page, 150, 430, 450, 550);

            for (var index = 0; index <= 2; index++)
            {
                DrawDiamond(page, 150 + (index * 150), 430 + (index * 60), 5);
            }
        });

        var points = Assert.Single(ReadChartSeries(await ReadAsync(fixture))).Points;

        Assert.Contains(points, point => Near(point[0], 0) && Near(point[1], 10));
        Assert.Contains(points, point => Near(point[0], 20) && Near(point[1], 50));
    }

    /// <summary>
    /// Verifies that the titles printed against the axes, and the fact that the values were followed along a
    /// drawn line, reach the chart a knowledge base stores. A series of bare numbers says nothing about what
    /// was measured.
    /// </summary>
    [Fact]
    public async Task Read_VectorChartWithAxisTitles_CarriesTitlesAndTypeToChartDetails()
    {
        var document = await ReadAsync(BuildChart(page =>
        {
            page.Text("Elapsed time", 250, 366, 8);
            page.Text("Load", 104, 520, 8);
        }));

        var objects = KnowledgeObjectBuilder.Build(
            document,
            new KnowledgeObjectBuildOptions
            {
                FileKey = "fixture",
                DataSourceId = "tests",
                Title = "Fixture",
            },
            new List<string>());

        var chart = Assert.Single(objects, entry => entry.ObjectType == KnowledgeObjectTypes.Chart);

        Assert.True(chart.TryGet<ChartDetails>(out var details));
        Assert.Equal("Elapsed time", details.AxisX);
        Assert.Equal("Load", details.AxisY);

        // The values were followed along the sloped polyline the chart draws, which is what a line chart is
        // made of.
        Assert.Equal(ChartTypes.Line, details.ChartType);
    }

    /// <summary>
    /// Verifies that a chart that arrived as a picture stays descriptive: nothing read values off it, so
    /// nothing claims to have any.
    /// </summary>
    [Fact]
    public async Task Read_RasterChart_StaysDescriptive()
    {
        var png = TestImageFactory.BarChart(240, 180);

        var fixture = new PdfFixtureBuilder().Page(page =>
        {
            page.Text("Figure 1. Yield by material.", 100, 560);
            page.Png(png, 100, 580, 240, 180);
        });

        var document = await ReadAsync(fixture);

        var figure = Assert.Single(document.EnumerateContent().OfType<IngestionDocumentImage>());

        Assert.False(figure.HasMetadata && figure.Metadata.ContainsKey(FigureMetadataKeys.ChartSeries));
        Assert.Null(figure.GetMetadataString(FigureMetadataKeys.ValueConfidence));
    }

    /// <summary>
    /// Verifies that the rules of a table are never also reported as a drawing, which would put a picture of
    /// a grid into the knowledge base alongside the table it already read.
    /// </summary>
    [Fact]
    public async Task Read_RuledTable_IsNotAlsoAVectorFigure()
    {
        var fixture = new PdfFixtureBuilder().Page(page =>
        {
            for (var index = 0; index < 8; index++)
            {
                var y = 400 + (index * 40);

                page.Line(80, y, 500, y);
            }

            for (var index = 0; index < 8; index++)
            {
                var x = 80 + (index * 60);

                page.Line(x, 400, x, 680);
            }

            page.Text("Material", 90, 650);
            page.Text("Steel", 90, 610);
        });

        var document = await ReadAsync(fixture);

        Assert.Single(document.EnumerateContent().OfType<IngestionDocumentTable>());
        Assert.Empty(document.EnumerateContent().OfType<IngestionDocumentImage>());
    }

    /// <summary>
    /// Verifies that a table laid out with nothing but alignment is still read as a table, so its numbers
    /// keep the columns that say what they mean.
    /// </summary>
    [Fact]
    public async Task Read_WhitespaceAlignedTable_YieldsTableWithColumns()
    {
        var rows = new[]
        {
            new[] { "Material", "Strength", "Temper" },
            new[] { "Steel", "400", "Hard" },
            new[] { "Copper", "220", "Soft" },
            new[] { "Brass", "310", "Half" },
        };

        var fixture = new PdfFixtureBuilder().Page(page =>
        {
            for (var row = 0; row < rows.Length; row++)
            {
                var y = 700 - (row * 20);

                page.Text(rows[row][0], 100, y);
                page.Text(rows[row][1], 250, y);
                page.Text(rows[row][2], 380, y);
            }
        });

        var document = await ReadAsync(fixture);

        var table = Assert.Single(document.EnumerateContent().OfType<IngestionDocumentTable>());

        Assert.Equal(4, table.Cells.GetLength(0));
        Assert.Equal(3, table.Cells.GetLength(1));
        Assert.Equal("Material", table.Cells[0, 0].Text);
        Assert.Equal("Strength", table.Cells[0, 1].Text);
        Assert.Equal("Steel", table.Cells[1, 0].Text);
        Assert.Equal("400", table.Cells[1, 1].Text);
        Assert.Equal("Half", table.Cells[3, 2].Text);
    }

    /// <summary>
    /// Verifies that running prose is never mistaken for a table, which is the failure whitespace detection
    /// has to avoid far more than it has to find every table.
    /// </summary>
    [Fact]
    public async Task Read_RunningProse_IsNotReadAsATable()
    {
        var fixture = new PdfFixtureBuilder().Page(page =>
        {
            page.Text("The rig was measured at three separate loads over the course of the week.", 100, 700);
            page.Text("Each measurement was repeated until the readings agreed to within one percent.", 100, 680);
            page.Text("The results are summarised in the paragraphs that follow this introduction.", 100, 660);
            page.Text("No further calibration was carried out after the second day of testing.", 100, 640);
        });

        var document = await ReadAsync(fixture);

        Assert.Empty(document.EnumerateContent().OfType<IngestionDocumentTable>());
    }

    /// <summary>
    /// Verifies that a value printed on a chart is taken as the value. A printed number is the figure the
    /// document states, not a reading off a picture, so it is exact even for a bar chart that draws no
    /// sloped line to follow.
    /// </summary>
    [Fact]
    public async Task Read_ChartWithPrintedDataLabels_YieldsExactPointsFromTheLabels()
    {
        var fixture = new PdfFixtureBuilder().Page(page =>
        {
            page.Line(150, 400, 450, 400);
            page.Line(150, 400, 150, 700);

            for (var index = 0; index <= 2; index++)
            {
                var x = 150 + (index * 150);
                var y = 400 + (index * 150);

                page.Line(x, 400, x, 394);
                page.Line(150, y, 144, y);

                page.Text((index * 10).ToString(System.Globalization.CultureInfo.InvariantCulture), x - 4, 380, 8);
                page.Text((index * 50).ToString(System.Globalization.CultureInfo.InvariantCulture), 128, y - 3, 8);
            }

            // Three bars, each with its value printed above it. The bars are rectangles, so nothing sloped
            // is drawn anywhere on this chart.
            var values = new[] { 20, 80, 45 };

            for (var index = 0; index < values.Length; index++)
            {
                var left = 190 + (index * 80);

                page.Rectangle(left, 400, 40, values[index] * 3);
                page.Text(values[index].ToString(System.Globalization.CultureInfo.InvariantCulture), left + 12, 410 + (values[index] * 3), 8);
            }
        });

        var document = await ReadAsync(fixture);

        var figure = Assert.Single(
            document.EnumerateContent().OfType<IngestionDocumentImage>(),
            image => image.HasMetadata && image.Metadata.ContainsKey(FigureMetadataKeys.IsVectorFigure));

        Assert.Equal(ChartValueConfidence.Exact, figure.GetMetadataString(FigureMetadataKeys.ValueConfidence));

        var series = System.Text.Json.JsonSerializer.Deserialize<List<ChartSeries>>(
            figure.GetMetadataString(FigureMetadataKeys.ChartSeries));

        var points = Assert.Single(series).Points;

        // The printed values come back verbatim, whatever the bars measure.
        Assert.Contains(points, point => point[1] == 20);
        Assert.Contains(points, point => point[1] == 80);
        Assert.Contains(points, point => point[1] == 45);

        // A printed value says what the number is and nothing about whether a bar, a column or a line was
        // drawn to carry it, so the chart is left untyped rather than typed by guesswork.
        Assert.Null(figure.GetMetadataString(FigureMetadataKeys.ChartType));
    }

    /// <summary>
    /// Builds a page that draws a chart: two axes, labelled ticks, and a polyline through known values.
    /// </summary>
    /// <param name="decorate">Draws anything else the chart carries, or <see langword="null"/> for none.</param>
    /// <remarks>
    /// The plot area runs from (150, 400) to (450, 700) in page points and from (0, 0) to (20, 100) in chart
    /// units, so a drawn point maps back to a chart value by a straight scale.
    /// </remarks>
    private static PdfFixtureBuilder BuildChart(Action<PdfFixturePage> decorate = null)
    {
        return new PdfFixtureBuilder().Page(page =>
        {
            DrawAxes(page);

            // The series: (0, 0) to (10, 100) to (20, 50), drawn as many short sloped segments so the
            // cluster is dense enough to be a drawing rather than a stray rule.
            DrawPolyline(page, 150, 400, 300, 700);
            DrawPolyline(page, 300, 700, 450, 550);

            decorate?.Invoke(page);
        });
    }

    /// <summary>
    /// Draws the frame every chart fixture shares: two axes, their tick marks, and the labels that give the
    /// plot its scale.
    /// </summary>
    /// <param name="page">The page being drawn.</param>
    private static void DrawAxes(PdfFixturePage page)
    {
        page.Line(150, 400, 450, 400);
        page.Line(150, 400, 150, 700);

        // Ticks along the bottom at 0, 10 and 20, and up the side at 0, 50 and 100.
        for (var index = 0; index <= 2; index++)
        {
            var x = 150 + (index * 150);
            var y = 400 + (index * 150);

            page.Line(x, 400, x, 394);
            page.Line(150, y, 144, y);

            page.Text((index * 10).ToString(System.Globalization.CultureInfo.InvariantCulture), x - 4, 380, 8);
            page.Text((index * 50).ToString(System.Globalization.CultureInfo.InvariantCulture), 128, y - 3, 8);
        }
    }

    /// <summary>
    /// Draws a diamond marker of the kind a chart puts on each of a line's data points.
    /// </summary>
    /// <param name="page">The page being drawn.</param>
    /// <param name="x">The centre, in points from the left edge.</param>
    /// <param name="y">The centre, in points from the bottom edge.</param>
    /// <param name="radius">How far each corner sits from the centre.</param>
    private static void DrawDiamond(PdfFixturePage page, double x, double y, double radius)
    {
        page.Line(x, y + radius, x + radius, y);
        page.Line(x + radius, y, x, y - radius);
        page.Line(x, y - radius, x - radius, y);
        page.Line(x - radius, y, x, y + radius);
    }

    private static void DrawPolyline(PdfFixturePage page, double fromX, double fromY, double toX, double toY)
    {
        const int Steps = 12;

        for (var step = 0; step < Steps; step++)
        {
            var t0 = (double)step / Steps;
            var t1 = (double)(step + 1) / Steps;

            page.Line(
                fromX + ((toX - fromX) * t0),
                fromY + ((toY - fromY) * t0),
                fromX + ((toX - fromX) * t1),
                fromY + ((toY - fromY) * t1));
        }
    }

    private static bool Near(double actual, double expected)
    {
        // One percent of the axis range, which is what "exact" means for a value recovered from geometry.
        return Math.Abs(actual - expected) <= 1.0;
    }

    /// <summary>
    /// Draws a ruled grid of the given shape.
    /// </summary>
    /// <param name="page">The page being drawn.</param>
    /// <param name="left">The left edge.</param>
    /// <param name="bottom">The bottom edge.</param>
    /// <param name="cellWidth">The width of one cell.</param>
    /// <param name="cellHeight">The height of one cell.</param>
    /// <param name="columns">How many columns.</param>
    /// <param name="rows">How many rows.</param>
    private static void DrawGrid(PdfFixturePage page, double left, double bottom, double cellWidth, double cellHeight, int columns, int rows)
    {
        var right = left + (columns * cellWidth);
        var top = bottom + (rows * cellHeight);

        for (var index = 0; index <= rows; index++)
        {
            var y = bottom + (index * cellHeight);

            page.Line(left, y, right, y);
        }

        for (var index = 0; index <= columns; index++)
        {
            var x = left + (index * cellWidth);

            page.Line(x, bottom, x, top);
        }
    }

    /// <summary>
    /// Verifies that a page set in columns is read as text rather than as a table.
    /// </summary>
    /// <remarks>
    /// Words are grouped into lines across the whole page, so a three-column page yields lines holding one
    /// fragment from each column, starting at the same three offsets on every line. That is exactly the
    /// evidence a whitespace-aligned table is recognized by, and a magazine page would otherwise be stored
    /// as a table dozens of rows deep whose cells are the halves of sentences.
    /// </remarks>
    [Fact]
    public async Task Read_ThreeColumnProse_IsNotReadAsATable()
    {
        var lines = new[]
        {
            "the quick brown fox jumps over",
            "a lazy dog beside the river in",
            "the evening light while nobody",
            "watches the water moving past",
            "the old stone bridge downstream",
            "toward the harbour and the sea",
            "where the fishing boats return",
            "each morning with the full tide",
        };

        var fixture = new PdfFixtureBuilder().Page(page =>
        {
            // Three columns of running text, every line starting at the same three offsets.
            for (var row = 0; row < lines.Length; row++)
            {
                var y = 700 - (row * 16);

                page.Text(lines[row], 50, y, size: 9);
                page.Text(lines[(row + 1) % lines.Length], 230, y, size: 9);
                page.Text(lines[(row + 2) % lines.Length], 410, y, size: 9);
            }
        });

        var document = await ReadAsync(fixture);

        Assert.Empty(document.EnumerateContent().OfType<IngestionDocumentTable>());
    }

    /// <summary>
    /// Reads the series the one vector figure on the page carries.
    /// </summary>
    /// <param name="document">The document that was read.</param>
    /// <returns>The series.</returns>
    private static List<ChartSeries> ReadChartSeries(IngestionDocument document)
    {
        var figure = Assert.Single(
            document.EnumerateContent().OfType<IngestionDocumentImage>(),
            image => image.HasMetadata && image.Metadata.ContainsKey(FigureMetadataKeys.IsVectorFigure));

        Assert.Equal(ChartValueConfidence.Exact, figure.GetMetadataString(FigureMetadataKeys.ValueConfidence));

        var raw = figure.GetMetadataString(FigureMetadataKeys.ChartSeries);

        Assert.NotNull(raw);

        return System.Text.Json.JsonSerializer.Deserialize<List<ChartSeries>>(raw);
    }

    private static async Task<IngestionDocument> ReadAsync(PdfFixtureBuilder fixture)
    {
        var reader = new PdfIngestionDocumentReader(Options.Create(new PdfLayoutOptions()));

        await using var stream = fixture.Build();

        return await reader.ReadAsync(stream, "fixture.pdf", "application/pdf", TestContext.Current.CancellationToken);
    }
}
