using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Graphics;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Draws a chart into a box on a PDF page: its title, legend, axes with gridlines and value labels, and the
/// bars, lines, areas or pie slices.
/// </summary>
internal sealed class PdfChartPainter
{
    private const double Swatch = 6;
    private const double LegendGap = 12;
    private const double MaxCategoryLabelWidth = 90;

    // A group of bars takes this part of its category, and a bar this part of its slot.
    private const double GroupShare = 0.8;
    private const double BarShare = 0.9;

    private readonly PdfChartModel _chart;
    private readonly PdfFont _font;
    private readonly PdfFont _bold;
    private readonly int _categories;
    private PdfCanvas _canvas;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfChartPainter"/> class.
    /// </summary>
    /// <param name="chart">The chart.</param>
    public PdfChartPainter(PdfChartModel chart)
    {
        _chart = chart;
        _font = PdfFontLibrary.Get(chart.FontFamily);
        _bold = PdfFontLibrary.Get(chart.FontFamily, bold: true);
        _categories = Math.Max(chart.Labels.Count, chart.Series.Count == 0 ? 0 : chart.Series.Max(series => series.Values.Count));
    }

    private bool IsPie => _chart.Kind is PdfChartKind.Pie or PdfChartKind.ExplodedPie;

    private bool IsHorizontal => _chart.Kind is PdfChartKind.Bar or PdfChartKind.StackedBar;

    private bool IsStacked => _chart.Kind is PdfChartKind.StackedBar or PdfChartKind.StackedColumn;

    private double LineHeight => _font.LineSpacing * _chart.FontSize;

    /// <summary>
    /// Draws the chart.
    /// </summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="left">The left edge of the box.</param>
    /// <param name="top">The top edge of the box.</param>
    /// <param name="width">The width of the box.</param>
    /// <param name="height">The height of the box.</param>
    public void Draw(PdfCanvas canvas, double left, double top, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (_chart.Series.Count == 0 || _categories == 0)
        {
            return;
        }

        _canvas = canvas;

        var box = new Box(left, top, width, height);

        if (!string.IsNullOrWhiteSpace(_chart.Title))
        {
            var titleWidth = _bold.Measure(_chart.Title, _chart.TitleSize);

            Text(_chart.Title, _bold, _chart.TitleSize, left + ((width - titleWidth) / 2), top, _chart.TitleColor);
            box = box with { Top = top + (_bold.LineSpacing * _chart.TitleSize) + 6, Height = height - (_bold.LineSpacing * _chart.TitleSize) - 6 };
        }

        if (IsPie)
        {
            DrawPie(box);

            return;
        }

        var legendHeight = _chart.ShowLegend
            ? DrawSeriesLegend(box)
            : 0;

        box = box with { Height = box.Height - legendHeight };

        if (IsHorizontal)
        {
            DrawHorizontal(box);
        }
        else
        {
            DrawVertical(box);
        }
    }

    private void DrawVertical(Box box)
    {
        var scale = GetScale();
        var tickLabels = scale.Ticks.Select(Format).ToList();
        var valueTitle = !string.IsNullOrWhiteSpace(_chart.ValueAxisTitle);
        var categoryTitle = !string.IsNullOrWhiteSpace(_chart.CategoryAxisTitle);
        var plotLeft = box.Left + tickLabels.Max(label => _font.Measure(label, _chart.FontSize)) + 6 + (valueTitle ? LineHeight + 4 : 0);
        var plotRight = box.Right - 6;
        var plotTop = box.Top + (_chart.ShowDataLabels ? LineHeight : 0) + 4;
        var plotBottom = box.Bottom - LineHeight - 4 - (categoryTitle ? LineHeight + 4 : 0);
        var bandWidth = (plotRight - plotLeft) / _categories;

        double Y(double value)
        {
            return plotBottom - ((value - scale.Min) / (scale.Max - scale.Min) * (plotBottom - plotTop));
        }

        for (var index = 0; index < scale.Ticks.Count; index++)
        {
            var y = Y(scale.Ticks[index]);

            _canvas.DrawLine(plotLeft, y, plotRight, y, new PdfPen(_chart.GridColor, 0.5));
            Text(tickLabels[index], _font, _chart.FontSize, plotLeft - 4 - _font.Measure(tickLabels[index], _chart.FontSize), y - (LineHeight / 2), _chart.TextColor);
        }

        // Labels that would run into each other are skipped evenly.
        var step = GetLabelStep(bandWidth);

        for (var index = 0; index < _categories; index += step)
        {
            var label = Fit(Label(index), Math.Min(MaxCategoryLabelWidth, (bandWidth * step) - 2));
            var center = plotLeft + ((index + 0.5) * bandWidth);

            Text(label, _font, _chart.FontSize, center - (_font.Measure(label, _chart.FontSize) / 2), plotBottom + 3, _chart.TextColor);
        }

        if (_chart.Kind is PdfChartKind.Line or PdfChartKind.Area)
        {
            DrawLines(plotLeft, bandWidth, Y);
        }
        else
        {
            DrawColumns(plotLeft, bandWidth, Y);
        }

        _canvas.DrawLine(plotLeft, Y(0), plotRight, Y(0), new PdfPen(_chart.AxisColor, 0.75));

        if (categoryTitle)
        {
            var width = _font.Measure(_chart.CategoryAxisTitle, _chart.FontSize);

            Text(_chart.CategoryAxisTitle, _font, _chart.FontSize, plotLeft + ((plotRight - plotLeft - width) / 2), plotBottom + LineHeight + 7, _chart.TextColor);
        }

        if (valueTitle)
        {
            UprightText(_chart.ValueAxisTitle, box.Left, plotTop + ((plotBottom - plotTop) / 2));
        }
    }

    private void DrawColumns(double plotLeft, double bandWidth, Func<double, double> y)
    {
        var groupWidth = bandWidth * GroupShare;
        var slotWidth = IsStacked
            ? groupWidth
            : groupWidth / _chart.Series.Count;

        for (var category = 0; category < _categories; category++)
        {
            var positive = 0d;
            var negative = 0d;

            for (var index = 0; index < _chart.Series.Count; index++)
            {
                if (Value(index, category) is not { } value || value == 0)
                {
                    continue;
                }

                var slotLeft = plotLeft + (category * bandWidth) + ((bandWidth - groupWidth) / 2);

                if (!IsStacked)
                {
                    slotLeft += index * slotWidth;
                }

                var (from, to) = Extent(value, ref positive, ref negative);
                var barTop = y(Math.Max(from, to));
                var barLeft = slotLeft + (slotWidth * (1 - BarShare) / 2);

                _canvas.DrawRectangle(barLeft, barTop, slotWidth * BarShare, y(Math.Min(from, to)) - barTop, new PdfBrush(_chart.Series[index].Color));

                if (_chart.ShowDataLabels && !IsStacked)
                {
                    var text = Format(value);
                    var width = _font.Measure(text, _chart.FontSize);
                    var labelTop = value >= 0
                        ? barTop - LineHeight
                        : y(Math.Min(from, to));

                    Text(text, _font, _chart.FontSize, barLeft + ((slotWidth * BarShare) - width) / 2, labelTop, _chart.TextColor);
                }
            }
        }
    }

    private void DrawLines(double plotLeft, double bandWidth, Func<double, double> y)
    {
        var runs = _chart.Series.Select((_, index) => Runs(index, plotLeft, bandWidth, y)).ToList();

        // Areas are filled first, so every line is drawn over them.
        if (_chart.Kind == PdfChartKind.Area)
        {
            for (var index = 0; index < _chart.Series.Count; index++)
            {
                foreach (var run in runs[index].Where(run => run.Count > 1))
                {
                    var path = new PdfPath().MoveTo(run[0].X, y(0));

                    foreach (var (x, top) in run)
                    {
                        path.LineTo(x, top);
                    }

                    path.LineTo(run[^1].X, y(0)).Close();
                    _canvas.DrawPath(path, new PdfBrush(_chart.Series[index].Color.Blend(new PdfColor(255, 255, 255), 0.5)));
                }
            }
        }

        for (var index = 0; index < _chart.Series.Count; index++)
        {
            var color = _chart.Series[index].Color;

            foreach (var run in runs[index])
            {
                var path = new PdfPath().MoveTo(run[0].X, run[0].Top);

                foreach (var (x, top) in run.Skip(1))
                {
                    path.LineTo(x, top);
                }

                if (run.Count > 1)
                {
                    _canvas.DrawPath(path, null, new PdfPen(color, 2, RoundJoins: true));
                }

                if (_chart.Kind == PdfChartKind.Line)
                {
                    foreach (var (x, top) in run)
                    {
                        _canvas.DrawPath(new PdfPath().AddEllipse(x - 2, top - 2, 4, 4), new PdfBrush(color));
                    }
                }
            }
        }

        if (!_chart.ShowDataLabels)
        {
            return;
        }

        for (var index = 0; index < _chart.Series.Count; index++)
        {
            for (var category = 0; category < _categories; category++)
            {
                if (Value(index, category) is not { } value)
                {
                    continue;
                }

                var text = Format(value);
                var x = plotLeft + ((category + 0.5) * bandWidth);

                Text(text, _font, _chart.FontSize, x - (_font.Measure(text, _chart.FontSize) / 2), y(value) - LineHeight - 2, _chart.TextColor);
            }
        }
    }

    private List<List<(double X, double Top)>> Runs(int series, double plotLeft, double bandWidth, Func<double, double> y)
    {
        // A missing value breaks the line, as a gap in the data.
        var runs = new List<List<(double X, double Top)>>();
        List<(double X, double Top)> run = null;

        for (var category = 0; category < _categories; category++)
        {
            if (Value(series, category) is not { } value)
            {
                run = null;

                continue;
            }

            if (run is null)
            {
                run = [];
                runs.Add(run);
            }

            run.Add((plotLeft + ((category + 0.5) * bandWidth), y(value)));
        }

        return runs;
    }

    private void DrawHorizontal(Box box)
    {
        var scale = GetScale();
        var tickLabels = scale.Ticks.Select(Format).ToList();
        var categoryTitle = !string.IsNullOrWhiteSpace(_chart.CategoryAxisTitle);
        var valueTitle = !string.IsNullOrWhiteSpace(_chart.ValueAxisTitle);
        var labelWidth = Math.Min(Enumerable.Range(0, _categories).Max(index => _font.Measure(Label(index), _chart.FontSize)), box.Width * 0.3);
        var plotLeft = box.Left + labelWidth + 6 + (categoryTitle ? LineHeight + 4 : 0);
        var plotRight = box.Right - (_font.Measure(tickLabels[^1], _chart.FontSize) / 2) - 2 - (_chart.ShowDataLabels ? 30 : 0);
        var plotTop = box.Top + 4;
        var plotBottom = box.Bottom - LineHeight - 4 - (valueTitle ? LineHeight + 4 : 0);
        var bandHeight = (plotBottom - plotTop) / _categories;

        double X(double value)
        {
            return plotLeft + ((value - scale.Min) / (scale.Max - scale.Min) * (plotRight - plotLeft));
        }

        for (var index = 0; index < scale.Ticks.Count; index++)
        {
            var x = X(scale.Ticks[index]);

            _canvas.DrawLine(x, plotTop, x, plotBottom, new PdfPen(_chart.GridColor, 0.5));
            Text(tickLabels[index], _font, _chart.FontSize, x - (_font.Measure(tickLabels[index], _chart.FontSize) / 2), plotBottom + 3, _chart.TextColor);
        }

        // The first category is at the top.
        for (var category = 0; category < _categories; category++)
        {
            var label = Fit(Label(category), labelWidth);
            var middle = plotTop + ((category + 0.5) * bandHeight);

            Text(label, _font, _chart.FontSize, plotLeft - 6 - _font.Measure(label, _chart.FontSize), middle - (LineHeight / 2), _chart.TextColor);
        }

        var groupHeight = bandHeight * GroupShare;
        var slotHeight = IsStacked
            ? groupHeight
            : groupHeight / _chart.Series.Count;

        for (var category = 0; category < _categories; category++)
        {
            var positive = 0d;
            var negative = 0d;

            for (var index = 0; index < _chart.Series.Count; index++)
            {
                if (Value(index, category) is not { } value || value == 0)
                {
                    continue;
                }

                var slotTop = plotTop + (category * bandHeight) + ((bandHeight - groupHeight) / 2);

                if (!IsStacked)
                {
                    slotTop += index * slotHeight;
                }

                var (from, to) = Extent(value, ref positive, ref negative);
                var barLeft = X(Math.Min(from, to));
                var barRight = X(Math.Max(from, to));
                var barTop = slotTop + (slotHeight * (1 - BarShare) / 2);

                _canvas.DrawRectangle(barLeft, barTop, barRight - barLeft, slotHeight * BarShare, new PdfBrush(_chart.Series[index].Color));

                if (_chart.ShowDataLabels && !IsStacked)
                {
                    Text(Format(value), _font, _chart.FontSize, barRight + 3, barTop + ((slotHeight * BarShare) - LineHeight) / 2, _chart.TextColor);
                }
            }
        }

        _canvas.DrawLine(X(0), plotTop, X(0), plotBottom, new PdfPen(_chart.AxisColor, 0.75));

        if (valueTitle)
        {
            var width = _font.Measure(_chart.ValueAxisTitle, _chart.FontSize);

            Text(_chart.ValueAxisTitle, _font, _chart.FontSize, plotLeft + ((plotRight - plotLeft - width) / 2), plotBottom + LineHeight + 7, _chart.TextColor);
        }

        if (categoryTitle)
        {
            UprightText(_chart.CategoryAxisTitle, box.Left, plotTop + ((plotBottom - plotTop) / 2));
        }
    }

    private void DrawPie(Box box)
    {
        var values = Enumerable.Range(0, _categories).Select(category => Value(0, category)).ToList();
        var total = values.Where(value => value > 0).Sum(value => value.Value);

        if (total <= 0)
        {
            return;
        }

        var legendWidth = _chart.ShowLegend
            ? DrawPieLegend(box)
            : 0;

        var areaWidth = box.Width - (legendWidth > 0 ? legendWidth + 12 : 0);
        var labelRoom = _chart.ShowDataLabels
            ? LineHeight + 10
            : 6;

        var radius = Math.Max(10, (Math.Min(areaWidth, box.Height) / 2) - labelRoom);
        var centerX = box.Left + (areaWidth / 2);
        var centerY = box.Top + (box.Height / 2);
        var exploded = _chart.Kind == PdfChartKind.ExplodedPie;
        var angle = -90d;

        // Slices go clockwise from twelve o'clock.
        for (var category = 0; category < values.Count; category++)
        {
            if (values[category] is not { } value || value <= 0)
            {
                continue;
            }

            var share = value / total;
            var sweep = share * 360;
            var middle = (angle + (sweep / 2)) * Math.PI / 180;
            var offset = exploded
                ? radius * 0.06
                : 0;

            var x = centerX + (offset * Math.Cos(middle));
            var y = centerY + (offset * Math.Sin(middle));
            var slice = new PdfPath().MoveTo(x, y).AddArc(x, y, radius, angle, sweep, startFigure: false).Close();

            _canvas.DrawPath(slice, new PdfBrush(_chart.SliceColor(category)), new PdfPen(new PdfColor(255, 255, 255), 0.75));

            // The share of each slice is written outside it; a sliver too thin to point at is left unlabeled.
            if (_chart.ShowDataLabels && share >= 0.02)
            {
                var text = (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
                var width = _font.Measure(text, _chart.FontSize);
                var distance = radius + offset + 4 + (Math.Max(width, LineHeight) / 2);

                Text(text, _font, _chart.FontSize, centerX + (distance * Math.Cos(middle)) - (width / 2), centerY + (distance * Math.Sin(middle)) - (LineHeight / 2), _chart.TextColor);
            }

            angle += sweep;
        }
    }

    private double DrawPieLegend(Box box)
    {
        // The slices of a pie are named in a column at its right.
        var maxWidth = box.Width * 0.4;
        var rowHeight = LineHeight * 1.3;
        var items = Enumerable.Range(0, _categories).Select(index => (Text: Fit(Label(index), maxWidth - Swatch - 3), Color: _chart.SliceColor(index))).ToList();
        var width = Math.Min(maxWidth, items.Max(item => Swatch + 3 + _font.Measure(item.Text, _chart.FontSize)));
        var visible = Math.Max(1, Math.Min(items.Count, (int)Math.Floor(box.Height / rowHeight)));
        var left = box.Right - width;
        var top = box.Top + ((box.Height - (visible * rowHeight)) / 2);

        for (var index = 0; index < visible; index++)
        {
            LegendItem(items[index].Text, items[index].Color, left, top + (index * rowHeight));
        }

        return width;
    }

    private double DrawSeriesLegend(Box box)
    {
        // The series are named in rows under the chart, centred, wrapping when they do not fit across it.
        var rows = new List<List<(string Text, PdfColor Color, double Width)>> { new() };
        var rowWidth = 0d;

        foreach (var series in _chart.Series)
        {
            var text = Fit(series.Name ?? string.Empty, box.Width - Swatch - 3);
            var width = Swatch + 3 + _font.Measure(text, _chart.FontSize);

            if (rows[^1].Count > 0 && rowWidth + LegendGap + width > box.Width)
            {
                rows.Add([]);
                rowWidth = 0;
            }

            if (rows[^1].Count > 0)
            {
                rowWidth += LegendGap;
            }

            rowWidth += width;
            rows[^1].Add((text, series.Color, width));
        }

        var rowHeight = LineHeight * 1.3;
        var top = box.Bottom - (rows.Count * rowHeight);

        foreach (var row in rows)
        {
            var x = box.Left + ((box.Width - (row.Sum(item => item.Width) + (LegendGap * (row.Count - 1)))) / 2);

            foreach (var (text, color, width) in row)
            {
                LegendItem(text, color, x, top);
                x += width + LegendGap;
            }

            top += rowHeight;
        }

        return (rows.Count * rowHeight) + 4;
    }

    private void LegendItem(string text, PdfColor color, double left, double top)
    {
        _canvas.DrawRectangle(left, top + ((LineHeight - Swatch) / 2), Swatch, Swatch, new PdfBrush(color));
        Text(text, _font, _chart.FontSize, left + Swatch + 3, top, _chart.TextColor);
    }

    private void Text(string text, PdfFont font, double size, double left, double top, PdfColor color)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _canvas.DrawText(text, font, size, left, top + (font.Ascent * size), new PdfBrush(color));
        }
    }

    private void UprightText(string text, double left, double middle)
    {
        // Turned a quarter anticlockwise, so it reads from bottom to top beside the axis.
        var width = _font.Measure(text, _chart.FontSize);
        var x = left + (_font.Ascent * _chart.FontSize);
        var y = middle + (width / 2);

        _canvas.Save();
        _canvas.RotateAt(-90, x, y);
        _canvas.DrawText(text, _font, _chart.FontSize, x, y, new PdfBrush(_chart.TextColor));
        _canvas.Restore();
    }

    private (double From, double To) Extent(double value, ref double positive, ref double negative)
    {
        if (!IsStacked)
        {
            return (0, value);
        }

        // Stacked values pile up from zero, positive ones above it and negative ones below.
        if (value > 0)
        {
            var from = positive;

            positive += value;

            return (from, positive);
        }

        var start = negative;

        negative += value;

        return (start, negative);
    }

    private double? Value(int series, int category)
    {
        var values = _chart.Series[series].Values;

        return category < values.Count && values[category] is { } value && double.IsFinite(value)
            ? value
            : null;
    }

    private string Label(int category)
    {
        return category < _chart.Labels.Count
            ? (_chart.Labels[category] ?? string.Empty).Replace('\n', ' ')
            : string.Empty;
    }

    private string Fit(string text, double width)
    {
        if (string.IsNullOrEmpty(text) || _font.Measure(text, _chart.FontSize) <= width)
        {
            return text;
        }

        var ellipsis = _font.HasGlyph('…')
            ? "…"
            : "...";

        var available = width - _font.Measure(ellipsis, _chart.FontSize);
        var length = text.Length;

        while (length > 0 && _font.Measure(text[..length], _chart.FontSize) > available)
        {
            length--;
        }

        return text[..length].TrimEnd() + ellipsis;
    }

    private int GetLabelStep(double bandWidth)
    {
        var widest = Enumerable.Range(0, _categories).Max(index => Math.Min(MaxCategoryLabelWidth, _font.Measure(Label(index), _chart.FontSize)));

        return Math.Max(1, (int)Math.Ceiling((widest + 4) / bandWidth));
    }

    private string Format(double value)
    {
        try
        {
            return value.ToString(_chart.NumberFormat, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return value.ToString("#,##0.##", CultureInfo.InvariantCulture);
        }
    }

    private Scale GetScale()
    {
        var values = new List<double>();

        for (var index = 0; index < _chart.Series.Count; index++)
        {
            for (var category = 0; category < _categories; category++)
            {
                if (Value(index, category) is { } value)
                {
                    values.Add(value);
                }
            }
        }

        // Stacked bars are read against the totals of their categories.
        if (IsStacked)
        {
            for (var category = 0; category < _categories; category++)
            {
                var categoryValues = Enumerable.Range(0, _chart.Series.Count).Select(index => Value(index, category) ?? 0).ToList();

                values.Add(categoryValues.Where(value => value > 0).Sum());
                values.Add(categoryValues.Where(value => value < 0).Sum());
            }
        }

        return Scale.Create(values, values.All(value => Math.Abs(value - Math.Round(value)) < 1e-9));
    }

    /// <summary>
    /// The area a part of the chart is drawn in.
    /// </summary>
    /// <param name="Left">The left edge.</param>
    /// <param name="Top">The top edge.</param>
    /// <param name="Width">The width.</param>
    /// <param name="Height">The height.</param>
    private readonly record struct Box(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;

        public double Bottom => Top + Height;
    }

    /// <summary>
    /// The value axis: its range, and the round values its gridlines are drawn at.
    /// </summary>
    private sealed class Scale
    {
        private Scale(double min, double max, double step)
        {
            Min = min;
            Max = max;

            for (var index = 0; min + (index * step) <= max + (step / 2); index++)
            {
                Ticks.Add(Math.Round(min + (index * step), 10));
            }
        }

        public double Min { get; }

        public double Max { get; }

        public List<double> Ticks { get; } = [];

        public static Scale Create(List<double> values, bool integers)
        {
            var min = Math.Min(0, values.Count == 0 ? 0 : values.Min());
            var max = Math.Max(0, values.Count == 0 ? 0 : values.Max());

            if (max - min < 1e-12)
            {
                max = min + 1;
            }

            var step = NiceNumber((max - min) / 5);

            // Counts are not divided into fractions.
            if (integers)
            {
                step = Math.Max(1, step);
            }

            return new Scale(Math.Floor((min / step) + 1e-9) * step, Math.Ceiling((max / step) - 1e-9) * step, step);
        }

        private static double NiceNumber(double value)
        {
            var exponent = Math.Floor(Math.Log10(value));
            var fraction = value / Math.Pow(10, exponent);
            var nice = fraction switch
            {
                < 1.5 => 1,
                < 3 => 2,
                < 7 => 5,
                _ => 10,
            };

            return nice * Math.Pow(10, exponent);
        }
    }
}
