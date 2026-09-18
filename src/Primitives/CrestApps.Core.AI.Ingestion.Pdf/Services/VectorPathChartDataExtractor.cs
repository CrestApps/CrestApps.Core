using System.Globalization;
using CrestApps.Core.AI.Models;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Ingestion.Pdf.Services;

/// <summary>
/// What could be read off a chart, and how far it can be trusted.
/// </summary>
internal sealed class ChartExtraction
{
    /// <summary>
    /// Gets or sets how far the values can be trusted. See <see cref="ChartValueConfidence"/>.
    /// </summary>
    public string ValueConfidence { get; set; } = ChartValueConfidence.Descriptive;

    /// <summary>
    /// Gets or sets the series, in the chart's own units.
    /// </summary>
    public IReadOnlyList<ChartSeries> Series { get; set; } = [];

    /// <summary>
    /// Gets or sets the kind of chart, when the drawing says what kind it is, or <see langword="null"/> when
    /// it does not. See <see cref="ChartTypes"/>.
    /// </summary>
    public string ChartType { get; set; }

    /// <summary>
    /// Gets or sets the horizontal axis title, as the chart prints it.
    /// </summary>
    public string AxisX { get; set; }

    /// <summary>
    /// Gets or sets the vertical axis title, as the chart prints it.
    /// </summary>
    public string AxisY { get; set; }
}

/// <summary>
/// Reads a vector chart's series out of the geometry the file draws.
/// </summary>
/// <remarks>
/// This is the difference between a useful answer and a confidently wrong one. A number read off a picture by
/// eye looks exactly like a number lifted from the file's own geometry, and only one of them is true. Nothing
/// here estimates: either the tick labels give a scale that maps drawn coordinates back to the chart's units,
/// in which case the values are exact, or they do not, in which case no value is reported at all.
/// </remarks>
internal static class VectorPathChartDataExtractor
{
    /// <summary>
    /// How many labelled ticks an axis needs before its scale can be solved. Two points define a line; one
    /// defines nothing.
    /// </summary>
    private const int MinimumTicks = 2;

    /// <summary>
    /// How near an axis a tick label has to be printed, as a fraction of the chart's size.
    /// </summary>
    private const double TickBandRatio = 0.18;

    /// <summary>
    /// How many segments a polyline needs before it is a series rather than a rule or a tick mark.
    /// </summary>
    private const int MinimumSeriesSegments = 3;

    /// <summary>
    /// How near two endpoints have to be, in points, to be the same vertex. A polyline writes its shared
    /// vertices twice, and rounding on the way into the file can move the two copies apart by a fraction of
    /// a point.
    /// </summary>
    private const double JoinTolerance = 0.5;

    /// <summary>
    /// How much of the plot's width a polyline has to run across before it is a series, as a fraction of the
    /// figure's width.
    /// </summary>
    private const double MinimumSeriesSpanRatio = 0.1;

    /// <summary>
    /// How far outside the tick labels an axis title may be set, in tick-label heights.
    /// </summary>
    private const double AxisTitleGapLines = 2.0;

    /// <summary>
    /// How much the tops of words set on one line may differ, as a fraction of a tick-label height. Words on
    /// one line vary by less than this; the line below is further away than this.
    /// </summary>
    private const double AxisTitleRowRatio = 0.6;

    /// <summary>
    /// Reads what the chart draws.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="figure">The detected vector figure.</param>
    /// <returns>The extraction. Never <see langword="null"/>.</returns>
    public static ChartExtraction Extract(Page page, DetectedVectorFigure figure)
    {
        var bounds = figure.Bounds;

        if (bounds is not { Length: 4 })
        {
            return new ChartExtraction();
        }

        var width = bounds[2] - bounds[0];
        var height = bounds[3] - bounds[1];

        if (width <= 0 || height <= 0)
        {
            return new ChartExtraction();
        }

        var words = page.GetWords()
            .Where(word => Intersects(word, bounds, width, height))
            .ToList();

        // A label under the plot belongs to the horizontal axis and one to its left belongs to the
        // vertical axis. Without saying so, the corner label counts for both, and a scale solved from a
        // label that belongs to the other axis is wrong everywhere.
        var xBand = words
            .Where(word => word.BoundingBox.Top <= bounds[1] + (height * TickBandRatio) && word.BoundingBox.Left >= bounds[0] - 2)
            .ToList();

        var yBand = words
            .Where(word => word.BoundingBox.Right <= bounds[0] + (width * TickBandRatio) && word.BoundingBox.Bottom >= bounds[1] - 2)
            .ToList();

        var xAxis = SolveAxis(xBand, word => (word.BoundingBox.Left + word.BoundingBox.Right) / 2);
        var yAxis = SolveAxis(yBand, word => (word.BoundingBox.Bottom + word.BoundingBox.Top) / 2);

        if (xAxis is null || yAxis is null)
        {
            // Axes were readable but the scale was not, or neither was. Either way nothing is reported as a
            // value: an axis that cannot be solved is the whole reason an estimate would be wrong.
            return new ChartExtraction
            {
                ValueConfidence = words.Count > 0 ? ChartValueConfidence.AxesOnly : ChartValueConfidence.Descriptive,
            };
        }

        // The tick labels are known now, so the words left over in each band are the ones that name the axis
        // rather than scale it.
        var axisX = ReadHorizontalAxisTitle(xBand);
        var axisY = ReadVerticalAxisTitle(yBand);

        // A value printed on the chart is the value, not a reading of one. It beats geometry, and it is the
        // only thing that recovers a bar chart, whose bars carry no sloped segments to follow.
        var labelled = BuildSeriesFromDataLabels(words, bounds, width, height, xAxis);

        if (labelled.Count > 0)
        {
            // No chart type is recorded here. A printed value says what the number is and nothing at all
            // about whether a bar, a column or a line was drawn to carry it.
            return new ChartExtraction
            {
                ValueConfidence = ChartValueConfidence.Exact,
                Series = labelled,
                AxisX = axisX,
                AxisY = axisY,
            };
        }

        var series = BuildSeries(figure.Segments, bounds, xAxis, yAxis);

        if (series.Count == 0)
        {
            return new ChartExtraction
            {
                ValueConfidence = ChartValueConfidence.AxesOnly,
                AxisX = axisX,
                AxisY = axisY,
            };
        }

        // The values were followed along the sloped polylines the chart draws, which is what a line chart is
        // made of. That is a statement about the drawing, not a guess at what it depicts.
        return new ChartExtraction
        {
            ValueConfidence = ChartValueConfidence.Exact,
            Series = series,
            ChartType = ChartTypes.Line,
            AxisX = axisX,
            AxisY = axisY,
        };
    }

    /// <summary>
    /// Builds the series from the values the chart prints on itself.
    /// </summary>
    /// <param name="words">The words in and around the chart.</param>
    /// <param name="bounds">The figure bounds.</param>
    /// <param name="width">The figure width.</param>
    /// <param name="height">The figure height.</param>
    /// <param name="xAxis">The horizontal scale, which says where along the axis a label sits.</param>
    /// <returns>The series, or none when the chart prints no values.</returns>
    /// <remarks>
    /// Only labels strictly inside the plot count. A number in an axis band is a tick, and a number outside
    /// the figure belongs to the page.
    /// </remarks>
    private static List<ChartSeries> BuildSeriesFromDataLabels(
        List<Word> words,
        double[] bounds,
        double width,
        double height,
        Axis xAxis)
    {
        var left = bounds[0] + (width * TickBandRatio);
        var bottom = bounds[1] + (height * TickBandRatio);
        var points = new List<double[]>();

        foreach (var word in words)
        {
            var box = word.BoundingBox;

            if (box.Left < left || box.Bottom < bottom || box.Right > bounds[2] || box.Top > bounds[3])
            {
                continue;
            }

            if (!TryParseNumber(word.Text, out var value))
            {
                continue;
            }

            points.Add([Math.Round(xAxis.ToValue((box.Left + box.Right) / 2), 6), value]);
        }

        if (points.Count < MinimumTicks)
        {
            return [];
        }

        return
        [
            new ChartSeries
            {
                Points = points.OrderBy(point => point[0]).ToList(),
            }
        ];
    }

    /// <summary>
    /// Builds the series by following the polylines the chart draws.
    /// </summary>
    /// <param name="segments">The figure's segments.</param>
    /// <param name="bounds">The figure's bounds.</param>
    /// <param name="xAxis">The horizontal scale.</param>
    /// <param name="yAxis">The vertical scale.</param>
    /// <returns>The series, one per polyline the chart draws.</returns>
    /// <remarks>
    /// Each polyline is its own series. Pouring every sloped segment in the figure into one list produces a
    /// series that exists nowhere on the page: a chart of two lines comes back as one line zig-zagging
    /// between them, and every value in it is real, which is what makes it so hard to catch downstream.
    /// </remarks>
    private static List<ChartSeries> BuildSeries(IReadOnlyList<PdfSegment> segments, double[] bounds, Axis xAxis, Axis yAxis)
    {
        // Axes, gridlines and tick marks are all axis-parallel. A data series is not, so following only the
        // sloped segments separates the data from the furniture without having to identify the furniture.
        var sloped = segments
            .Where(segment => Math.Abs(segment.X1 - segment.X2) > 0.5 && Math.Abs(segment.Y1 - segment.Y2) > 0.5)
            .OrderBy(segment => segment.Left)
            .ToList();

        if (sloped.Count < MinimumSeriesSegments)
        {
            return [];
        }

        var minimumSpan = (bounds[2] - bounds[0]) * MinimumSeriesSpanRatio;
        var series = new List<ChartSeries>();

        foreach (var polyline in GroupPolylines(sloped))
        {
            if (polyline.Count < MinimumSeriesSegments)
            {
                continue;
            }

            // A series runs across the plot. A marker, an arrowhead and a legend swatch are drawn out of
            // sloped segments too, and none of them becomes a series however many segments it takes.
            if (polyline.Max(segment => segment.Right) - polyline.Min(segment => segment.Left) < minimumSpan)
            {
                continue;
            }

            var points = new List<double[]>();
            var seen = new HashSet<(double, double)>();

            foreach (var segment in polyline)
            {
                AddPoint(points, seen, segment.X1, segment.Y1, xAxis, yAxis);
                AddPoint(points, seen, segment.X2, segment.Y2, xAxis, yAxis);
            }

            if (points.Count < MinimumTicks)
            {
                continue;
            }

            // The name is left unset, and nothing here can set it. Telling which line a legend swatch stands
            // for needs the colour or the dash pattern the page drew each path with, and a segment carries
            // neither: PdfPageGeometry flattens the page's paths down to four coordinates and drops their
            // appearance. Position alone does not say which line a label names, and a series named by
            // guesswork is attributed to something the document never said.
            series.Add(new ChartSeries
            {
                Points = points.OrderBy(point => point[0]).ToList(),
            });
        }

        return series.OrderBy(entry => entry.Points[0][0]).ToList();
    }

    /// <summary>
    /// Groups the sloped segments into the polylines they were drawn as.
    /// </summary>
    /// <param name="segments">The sloped segments, in a stable order.</param>
    /// <returns>The polylines.</returns>
    /// <remarks>
    /// A page draws a polyline as segments that carry on from each other, so the segments that share an
    /// endpoint are one line. Nothing upstream keeps the path a segment came from: the figure's segments were
    /// flattened out of the page's paths and then regrouped by proximity, which puts every line of a chart in
    /// one bag. Walking the shared endpoints takes them apart again.
    /// </remarks>
    private static List<List<PdfSegment>> GroupPolylines(List<PdfSegment> segments)
    {
        var polylines = new List<List<PdfSegment>>();
        var assigned = new bool[segments.Count];

        for (var index = 0; index < segments.Count; index++)
        {
            if (assigned[index])
            {
                continue;
            }

            var polyline = new List<PdfSegment>();
            var queue = new Queue<int>();

            queue.Enqueue(index);
            assigned[index] = true;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                polyline.Add(segments[current]);

                for (var other = 0; other < segments.Count; other++)
                {
                    if (assigned[other] || !SharesEndpoint(segments[current], segments[other]))
                    {
                        continue;
                    }

                    assigned[other] = true;
                    queue.Enqueue(other);
                }
            }

            polylines.Add(polyline);
        }

        return polylines;
    }

    private static bool SharesEndpoint(PdfSegment left, PdfSegment right)
    {
        return IsSamePoint(left.X1, left.Y1, right.X1, right.Y1) ||
            IsSamePoint(left.X1, left.Y1, right.X2, right.Y2) ||
            IsSamePoint(left.X2, left.Y2, right.X1, right.Y1) ||
            IsSamePoint(left.X2, left.Y2, right.X2, right.Y2);
    }

    private static bool IsSamePoint(double x1, double y1, double x2, double y2)
    {
        return Math.Abs(x1 - x2) <= JoinTolerance && Math.Abs(y1 - y2) <= JoinTolerance;
    }

    private static void AddPoint(List<double[]> points, HashSet<(double, double)> seen, double x, double y, Axis xAxis, Axis yAxis)
    {
        var value = new[] { Math.Round(xAxis.ToValue(x), 6), Math.Round(yAxis.ToValue(y), 6) };

        if (seen.Add((value[0], value[1])))
        {
            points.Add(value);
        }
    }

    /// <summary>
    /// Solves an axis from its printed tick labels: which drawn coordinate corresponds to which value.
    /// </summary>
    /// <param name="candidates">The words printed in the axis band.</param>
    /// <param name="position">Reads the drawn coordinate of a word along the axis.</param>
    /// <returns>The scale, or <see langword="null"/> when the labels do not describe one.</returns>
    private static Axis SolveAxis(IEnumerable<Word> candidates, Func<Word, double> position)
    {
        var ticks = new List<(double Position, double Value)>();

        foreach (var word in candidates)
        {
            if (!TryParseNumber(word.Text, out var value))
            {
                continue;
            }

            ticks.Add((position(word), value));
        }

        if (ticks.Count < MinimumTicks)
        {
            return null;
        }

        ticks = ticks.OrderBy(tick => tick.Position).ToList();

        var first = ticks[0];
        var last = ticks[^1];
        var positionSpan = last.Position - first.Position;
        var valueSpan = last.Value - first.Value;

        if (Math.Abs(positionSpan) < 1 || Math.Abs(valueSpan) < double.Epsilon)
        {
            return null;
        }

        var scale = valueSpan / positionSpan;

        // Every labelled tick has to sit where a straight scale says it should. A log axis, a broken axis or
        // a label that is not a tick all fail this, and failing it is the point: a scale that does not hold
        // everywhere must not be used to read values anywhere.
        foreach (var tick in ticks)
        {
            var predicted = first.Value + ((tick.Position - first.Position) * scale);

            if (Math.Abs(predicted - tick.Value) > Math.Abs(valueSpan) * 0.02)
            {
                return null;
            }
        }

        return new Axis(first.Position, first.Value, scale);
    }

    /// <summary>
    /// Reads the horizontal axis title off the words printed in its band.
    /// </summary>
    /// <param name="band">The words printed in the horizontal axis band.</param>
    /// <returns>The title, or <see langword="null"/> when none is printed.</returns>
    /// <remarks>
    /// Only the line immediately under the tick labels counts. An axis title is set there; a caption, a
    /// footnote and the prose that follows the figure are set further down, and reading one of those as the
    /// name of an axis would label the axis with a sentence about something else entirely.
    /// </remarks>
    private static string ReadHorizontalAxisTitle(List<Word> band)
    {
        var lineHeight = GetTickHeight(band);

        if (lineHeight is null)
        {
            return null;
        }

        var ticksBottom = band.Where(IsNumber).Min(word => word.BoundingBox.Bottom);
        var title = SelectNearestLine(band, word => ticksBottom - word.BoundingBox.Top, lineHeight.Value);

        return Join(title.OrderBy(word => word.BoundingBox.Left));
    }

    /// <summary>
    /// Reads the vertical axis title off the words printed in its band.
    /// </summary>
    /// <param name="band">The words printed in the vertical axis band.</param>
    /// <returns>The title, or <see langword="null"/> when none is printed.</returns>
    /// <remarks>
    /// A vertical title is set either sideways, as one column of words, or flat, as a row of them reaching
    /// away from the axis. Both are the run of words that starts beside the tick labels and carries on
    /// outward, so the run is followed until it stops, rather than a single line being taken.
    /// </remarks>
    private static string ReadVerticalAxisTitle(List<Word> band)
    {
        var lineHeight = GetTickHeight(band);

        if (lineHeight is null)
        {
            return null;
        }

        var ticksLeft = band.Where(IsNumber).Min(word => word.BoundingBox.Left);

        var title = SelectRun(
            band,
            word => ticksLeft - word.BoundingBox.Right,
            word => word.BoundingBox.Width,
            lineHeight.Value);

        return Join(title
            .OrderByDescending(word => word.BoundingBox.Top)
            .ThenBy(word => word.BoundingBox.Left));
    }

    /// <summary>
    /// Gets how tall a tick label is set, which is the measure every distance around an axis is judged by.
    /// </summary>
    /// <param name="band">The words printed in the axis band.</param>
    /// <returns>The height, or <see langword="null"/> when the band holds no tick labels.</returns>
    private static double? GetTickHeight(List<Word> band)
    {
        var ticks = band.Where(IsNumber).ToList();

        if (ticks.Count < MinimumTicks)
        {
            return null;
        }

        // A degenerate box would make every distance around the axis zero, which finds nothing rather than
        // finding the wrong thing.
        return Math.Max(ticks.Max(word => word.BoundingBox.Height), 1d);
    }

    /// <summary>
    /// Picks the words set on the line nearest the tick labels.
    /// </summary>
    /// <param name="band">The words printed in the axis band.</param>
    /// <param name="distance">Reads how far outside the tick labels a word sits.</param>
    /// <param name="lineHeight">The height of a tick label.</param>
    /// <returns>The words, or none when nothing is set against the axis.</returns>
    private static List<Word> SelectNearestLine(List<Word> band, Func<Word, double> distance, double lineHeight)
    {
        var candidates = Measure(band, distance);

        if (candidates.Count == 0 || candidates[0].Distance > lineHeight * AxisTitleGapLines)
        {
            return [];
        }

        var nearest = candidates[0].Distance;

        return candidates
            .Where(entry => entry.Distance - nearest <= lineHeight * AxisTitleRowRatio)
            .Select(entry => entry.Word)
            .ToList();
    }

    /// <summary>
    /// Picks the run of words that starts beside the tick labels and carries on outward, stopping at the
    /// first real gap.
    /// </summary>
    /// <param name="band">The words printed in the axis band.</param>
    /// <param name="distance">Reads how far outside the tick labels a word sits.</param>
    /// <param name="extent">Reads how far a word reaches on outward from where it starts.</param>
    /// <param name="lineHeight">The height of a tick label.</param>
    /// <returns>The words, or none when nothing is set against the axis.</returns>
    private static List<Word> SelectRun(List<Word> band, Func<Word, double> distance, Func<Word, double> extent, double lineHeight)
    {
        var candidates = Measure(band, distance);
        var gap = lineHeight * AxisTitleGapLines;
        var run = new List<Word>();
        var reach = gap;

        foreach (var entry in candidates)
        {
            if (entry.Distance > reach)
            {
                break;
            }

            run.Add(entry.Word);

            // The next word of a title carries on from the far edge of this one, not from where it starts.
            // A title set flat is several words wide, and measuring from the near edge would stop at the
            // first of them.
            reach = Math.Max(reach, entry.Distance + extent(entry.Word) + gap);
        }

        return run;
    }

    /// <summary>
    /// Measures how far each word that is not a tick label sits outside the tick labels, nearest first. A
    /// word on the tick labels' own side of the axis measures negative and is dropped: it is part of the
    /// plot, not a name for it.
    /// </summary>
    /// <param name="band">The words printed in the axis band.</param>
    /// <param name="distance">Reads how far outside the tick labels a word sits.</param>
    /// <returns>The measured words, nearest first.</returns>
    private static List<(Word Word, double Distance)> Measure(List<Word> band, Func<Word, double> distance)
    {
        return band
            .Where(word => !IsNumber(word))
            .Select(word => (Word: word, Distance: distance(word)))
            .Where(entry => entry.Distance >= 0)
            .OrderBy(entry => entry.Distance)
            .ToList();
    }

    private static string Join(IEnumerable<Word> words)
    {
        var title = PdfTextNormalizer.Normalize(string.Join(' ', words.Select(word => word.Text)));

        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static bool IsNumber(Word word)
    {
        return TryParseNumber(word.Text, out _);
    }

    private static bool TryParseNumber(string text, out double value)
    {
        value = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = text.Trim().Trim('%', '°');

        if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return TryParseGroupedNumber(cleaned, out value);
    }

    /// <summary>
    /// Reads a tick label whose digits are separated by commas.
    /// </summary>
    /// <param name="text">The trimmed label.</param>
    /// <param name="value">The value the label states.</param>
    /// <returns><see langword="true"/> when the label states one unambiguous number.</returns>
    /// <remarks>
    /// A comma is a decimal point in most of the world and a thousands separator in the rest, and the two
    /// readings of one label differ by a factor of a thousand. Reading <c>1,000</c> as a decimal comma turns
    /// an axis of one to four thousand into an axis of one to four; the ticks stay evenly spaced, so the
    /// linearity check still passes and every series value is reported a thousand times too small — and
    /// reported as exact, because it came from the file's own geometry rather than from a model's guess.
    /// <para>
    /// So only the unambiguous readings are accepted. Two or more three-digit groups can only be thousands,
    /// and one or two digits after a comma can only be a decimal. A single three-digit group is genuinely
    /// ambiguous and is refused, which costs the chart its exact values and leaves it described instead.
    /// </para>
    /// </remarks>
    private static bool TryParseGroupedNumber(string text, out double value)
    {
        value = 0;

        var parts = text.Split(',');

        if (parts.Length < 2)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length == 0)
            {
                return false;
            }
        }

        var tail = parts[^1];

        // One comma with one or two digits behind it is a decimal comma; nothing groups thousands that way.
        if (parts.Length == 2 && tail.Length is 1 or 2)
        {
            return double.TryParse(
                string.Concat(parts[0], ".", tail),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        foreach (var part in parts[1..])
        {
            if (part.Length != 3)
            {
                return false;
            }
        }

        // A single three-digit group reads as one thousand or as one point zero depending on where the
        // document was typeset, and nothing on the page says which.
        if (parts.Length == 2)
        {
            return false;
        }

        return double.TryParse(
            string.Concat(parts),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static bool Intersects(Word word, double[] bounds, double width, double height)
    {
        var box = word.BoundingBox;
        var margin = Math.Max(width, height) * TickBandRatio;

        return box.Right >= bounds[0] - margin &&
            box.Left <= bounds[2] + margin &&
            box.Top >= bounds[1] - margin &&
            box.Bottom <= bounds[3] + margin;
    }

    private sealed record Axis(double Origin, double OriginValue, double Scale)
    {
        public double ToValue(double position)
        {
            return OriginValue + ((position - Origin) * Scale);
        }
    }
}
