using System.Text;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Ingestion.Pdf.Services;

/// <summary>
/// Finds the tables a page lays out with whitespace rather than rules.
/// </summary>
/// <remarks>
/// Plenty of tables are drawn with nothing but alignment. Read as prose they come out as a run of numbers
/// with nothing saying which column each belongs to, which is exactly the failure ruled-table detection
/// exists to prevent.
/// <para>
/// The danger is the opposite one: three columns of running text look identical to three columns of data
/// until something says otherwise. So the bar is deliberately high — several consecutive lines that all
/// start words at the same few x positions, with real gaps between them — and a region that fails it is left
/// as prose, which is what it was.
/// </para>
/// </remarks>
internal static class PdfGridTableDetector
{
    /// <summary>
    /// The fewest columns a grid needs. Two columns of aligned text is a definition list or an indent.
    /// </summary>
    private const int MinimumColumns = 3;

    /// <summary>
    /// The fewest rows a grid needs. Two aligned lines happen by accident.
    /// </summary>
    private const int MinimumRows = 3;

    /// <summary>
    /// How far two word starts may differ and still count as the same column, in points.
    /// </summary>
    private const double ColumnTolerance = 4.0;

    /// <summary>
    /// How wide the gap before a column has to be, as a multiple of the text height, before the alignment is
    /// taken for a column boundary rather than a word space.
    /// </summary>
    private const double MinimumGapRatio = 0.6;

    /// <summary>
    /// What share of a candidate's lines a column has to appear on. A table has the occasional empty cell;
    /// a paragraph that happens to align once does not repeat.
    /// </summary>
    private const double ColumnSupport = 0.6;

    /// <summary>
    /// The most words a cell may average before the run reads as running text rather than as a table.
    /// </summary>
    /// <remarks>
    /// Words are grouped into lines across the whole page, so a page set in columns produces lines that hold
    /// one fragment from each column. Those fragments start at the same offsets on every line, which is
    /// exactly the evidence a whitespace-aligned table is recognized by — a magazine page would be emitted
    /// as a table forty rows deep whose cells are the halves of sentences.
    /// <para>
    /// What separates them is how much each cell says. A table aligned by whitespace holds labels and
    /// numbers; a column of prose holds a line of a sentence. Refusing the wordy case costs a genuine table
    /// with descriptive cells its structure and leaves its text to be read as text, which is what happened
    /// before any table was detected at all. Accepting it corrupts the page.
    /// </para>
    /// </remarks>
    private const double MaxWordsPerCell = 4.0;

    /// <summary>
    /// Finds every whitespace-aligned table on the page.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="excluded">Regions already accounted for, such as ruled tables.</param>
    /// <returns>The tables.</returns>
    public static List<DetectedTable> Detect(Page page, IReadOnlyList<double[]> excluded)
    {
        var tables = new List<DetectedTable>();
        var lines = BuildLines(page);

        if (lines.Count < MinimumRows)
        {
            return tables;
        }

        var index = 0;

        while (index < lines.Count)
        {
            var run = GrowRun(lines, index);

            if (run.Count < MinimumRows)
            {
                index++;

                continue;
            }

            var columns = SolveColumns(run);

            if (columns.Count < MinimumColumns)
            {
                index++;

                continue;
            }

            if (IsRunningText(run, columns.Count))
            {
                index++;

                continue;
            }

            var bounds = GetBounds(run);

            if (excluded is not null && excluded.Any(region => Overlaps(bounds, region)))
            {
                index += run.Count;

                continue;
            }

            tables.Add(Build(run, columns, bounds));
            index += run.Count;
        }

        return tables;
    }

    /// <summary>
    /// Says whether an aligned run is a page set in columns rather than a table.
    /// </summary>
    /// <param name="run">The aligned lines.</param>
    /// <param name="columnCount">How many columns were solved for the run.</param>
    /// <returns><see langword="true"/> when the run holds running text.</returns>
    private static bool IsRunningText(List<List<Word>> run, int columnCount)
    {
        if (run.Count == 0 || columnCount == 0)
        {
            return false;
        }

        var words = 0;

        foreach (var line in run)
        {
            words += line.Count;
        }

        return (double)words / run.Count / columnCount > MaxWordsPerCell;
    }

    /// <summary>
    /// Groups the page's words into lines, top to bottom.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The lines, each holding its words left to right.</returns>
    private static List<List<Word>> BuildLines(Page page)
    {
        var words = page.GetWords()
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .OrderByDescending(word => word.BoundingBox.Bottom)
            .ToList();

        var lines = new List<List<Word>>();

        foreach (var word in words)
        {
            var height = Math.Abs(word.BoundingBox.Height);
            var tolerance = Math.Max(2, height * 0.5);
            var line = lines.Count > 0 ? lines[^1] : null;

            if (line is not null && Math.Abs(line[0].BoundingBox.Bottom - word.BoundingBox.Bottom) <= tolerance)
            {
                line.Add(word);

                continue;
            }

            lines.Add([word]);
        }

        foreach (var line in lines)
        {
            line.Sort((left, right) => left.BoundingBox.Left.CompareTo(right.BoundingBox.Left));
        }

        return lines;
    }

    /// <summary>
    /// Grows the longest run of consecutive lines that could be one table.
    /// </summary>
    /// <param name="lines">Every line on the page.</param>
    /// <param name="start">Where to start.</param>
    /// <returns>The run.</returns>
    private static List<List<Word>> GrowRun(List<List<Word>> lines, int start)
    {
        var run = new List<List<Word>>();

        for (var index = start; index < lines.Count; index++)
        {
            // A line that cannot contribute three columns ends the run. Anything less is prose.
            if (CountColumnStarts(lines[index]) < MinimumColumns)
            {
                break;
            }

            run.Add(lines[index]);
        }

        return run;
    }

    /// <summary>
    /// Counts how many column starts a line has: its first word, plus every word after a gap wide enough to
    /// be a column boundary rather than a word space.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>The count.</returns>
    private static int CountColumnStarts(List<Word> line)
    {
        return GetColumnStarts(line).Count;
    }

    private static List<double> GetColumnStarts(List<Word> line)
    {
        var starts = new List<double>();

        for (var index = 0; index < line.Count; index++)
        {
            var word = line[index];

            if (index == 0)
            {
                starts.Add(word.BoundingBox.Left);

                continue;
            }

            var previous = line[index - 1];
            var gap = word.BoundingBox.Left - previous.BoundingBox.Right;
            var height = Math.Max(Math.Abs(word.BoundingBox.Height), 1);

            if (gap >= height * MinimumGapRatio)
            {
                starts.Add(word.BoundingBox.Left);
            }
        }

        return starts;
    }

    /// <summary>
    /// Works out the column positions the run agrees on.
    /// </summary>
    /// <param name="run">The candidate lines.</param>
    /// <returns>The column left edges, ascending.</returns>
    private static List<double> SolveColumns(List<List<Word>> run)
    {
        var clusters = new List<(double Sum, int Count, int Lines)>();

        foreach (var line in run)
        {
            var seen = new HashSet<int>();

            foreach (var start in GetColumnStarts(line))
            {
                var matched = -1;

                for (var index = 0; index < clusters.Count; index++)
                {
                    if (Math.Abs((clusters[index].Sum / clusters[index].Count) - start) <= ColumnTolerance)
                    {
                        matched = index;

                        break;
                    }
                }

                if (matched < 0)
                {
                    clusters.Add((start, 1, 1));
                    seen.Add(clusters.Count - 1);

                    continue;
                }

                var cluster = clusters[matched];

                clusters[matched] = (cluster.Sum + start, cluster.Count + 1, cluster.Lines + (seen.Add(matched) ? 1 : 0));
            }
        }

        var required = Math.Max(2, (int)Math.Ceiling(run.Count * ColumnSupport));

        return clusters
            .Where(cluster => cluster.Lines >= required)
            .Select(cluster => cluster.Sum / cluster.Count)
            .OrderBy(value => value)
            .ToList();
    }

    /// <summary>
    /// Puts each word into the column it starts in.
    /// </summary>
    /// <param name="run">The lines.</param>
    /// <param name="columns">The column left edges.</param>
    /// <param name="bounds">The region the table occupies.</param>
    /// <returns>The table.</returns>
    private static DetectedTable Build(List<List<Word>> run, List<double> columns, double[] bounds)
    {
        var cells = new string[run.Count][];

        for (var row = 0; row < run.Count; row++)
        {
            var builders = new StringBuilder[columns.Count];

            for (var column = 0; column < columns.Count; column++)
            {
                builders[column] = new StringBuilder();
            }

            foreach (var word in run[row])
            {
                var column = ResolveColumn(columns, word.BoundingBox.Left);

                if (builders[column].Length > 0)
                {
                    builders[column].Append(' ');
                }

                builders[column].Append(word.Text);
            }

            cells[row] = builders.Select(builder => builder.ToString().Trim()).ToArray();
        }

        return new DetectedTable
        {
            Cells = cells,
            Bounds = bounds,
        };
    }

    private static int ResolveColumn(List<double> columns, double left)
    {
        var column = 0;

        for (var index = 0; index < columns.Count; index++)
        {
            if (left >= columns[index] - ColumnTolerance)
            {
                column = index;
            }
        }

        return column;
    }

    private static double[] GetBounds(List<List<Word>> run)
    {
        var words = run.SelectMany(line => line).ToList();

        return
        [
            words.Min(word => word.BoundingBox.Left),
            words.Min(word => word.BoundingBox.Bottom),
            words.Max(word => word.BoundingBox.Right),
            words.Max(word => word.BoundingBox.Top),
        ];
    }

    private static bool Overlaps(double[] left, double[] right)
    {
        if (left is not { Length: 4 } || right is not { Length: 4 })
        {
            return false;
        }

        return left[0] < right[2] && right[0] < left[2] && left[1] < right[3] && right[1] < left[3];
    }
}
