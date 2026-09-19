using System.Text;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Ingestion.Pdf.Services;

/// <summary>
/// Finds the tables a page draws.
/// </summary>
/// <remarks>
/// A table read as prose is a row of numbers with nothing saying which column each belongs to, and a
/// question about one of them is answered with whichever number happened to land nearby. Reading the rules
/// the page draws recovers what the layout already said: this value is in this column of this row.
/// <para>
/// Only tables the page actually rules are found here. A table laid out with whitespace alone is recovered
/// separately and more cautiously, because three columns of running text look identical to three columns of
/// data until something says otherwise.
/// </para>
/// <para>
/// Rules that touch each other belong to one grid, and rules that do not belong to different ones. A page
/// holding two tables, or a table beside a boxed advertisement, is therefore two groups of rules read as two
/// things, not one grid spanning both with a band of nonsense between them.
/// </para>
/// </remarks>
internal static class PdfTableDetector
{
    /// <summary>
    /// How far apart two rules may be and still be taken for the same rule.
    /// </summary>
    private const double RuleTolerance = 2.5;

    /// <summary>
    /// How much of the table's span a rule has to cross before it is believed to be a grid line rather than
    /// an underline inside one cell.
    /// </summary>
    private const double MinimumSpanRatio = 0.6;

    /// <summary>
    /// The smallest grid worth reporting. Two rows and two columns is a table; anything less is a box.
    /// </summary>
    private const int MinimumLines = 3;

    /// <summary>
    /// How near a rule a word may be printed and still count as inside the cell it borders.
    /// </summary>
    private const double CellPadding = 1.0;

    /// <summary>
    /// Finds every ruled table on the page.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="segments">The lines the page draws.</param>
    /// <returns>The tables, top to bottom.</returns>
    public static List<DetectedTable> Detect(Page page, IReadOnlyList<PdfSegment> segments)
    {
        var tables = new List<DetectedTable>();

        if (segments is null || segments.Count == 0)
        {
            return tables;
        }

        var rules = segments
            .Where(segment => segment.Length > RuleTolerance && (IsHorizontal(segment) || IsVertical(segment)))
            .ToList();

        if (rules.Count < MinimumLines * 2)
        {
            return tables;
        }

        List<Word> words = null;

        foreach (var grid in GroupTouchingRules(rules))
        {
            var table = ReadGrid(grid, ref words, page);

            if (table is not null)
            {
                tables.Add(table);
            }
        }

        return tables
            .OrderByDescending(table => table.Bounds[3])
            .ThenBy(table => table.Bounds[0])
            .ToList();
    }

    /// <summary>
    /// Reads one group of touching rules as a grid, or as nothing when the group is not a table.
    /// </summary>
    /// <param name="rules">The rules that touch each other.</param>
    /// <param name="words">The page's words, read on first use.</param>
    /// <param name="page">The page.</param>
    /// <returns>The table, or <see langword="null"/>.</returns>
    private static DetectedTable ReadGrid(List<PdfSegment> rules, ref List<Word> words, Page page)
    {
        var horizontal = rules.Where(IsHorizontal).ToList();
        var vertical = rules.Where(IsVertical).ToList();

        if (horizontal.Count < MinimumLines || vertical.Count < MinimumLines)
        {
            return null;
        }

        var rows = Cluster(horizontal.Select(segment => (segment.Y1 + segment.Y2) / 2));
        var columns = Cluster(vertical.Select(segment => (segment.X1 + segment.X2) / 2));

        if (rows.Count < MinimumLines || columns.Count < MinimumLines)
        {
            return null;
        }

        var width = columns[^1] - columns[0];
        var height = rows[^1] - rows[0];

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        // A rule that crosses only part of the table is an underline inside a cell, not a grid line.
        rows = rows.Where(y => Spans(horizontal, segment => Math.Abs(((segment.Y1 + segment.Y2) / 2) - y) <= RuleTolerance, segment => segment.Right - segment.Left, width)).ToList();
        columns = columns.Where(x => Spans(vertical, segment => Math.Abs(((segment.X1 + segment.X2) / 2) - x) <= RuleTolerance, segment => segment.Top - segment.Bottom, height)).ToList();

        if (rows.Count < MinimumLines || columns.Count < MinimumLines)
        {
            return null;
        }

        words ??= page.GetWords().ToList();

        var cells = new string[rows.Count - 1][];

        // Rows are read top to bottom, and PDF user space grows upward.
        for (var rowIndex = 0; rowIndex < rows.Count - 1; rowIndex++)
        {
            var rowTop = rows[rows.Count - 1 - rowIndex];
            var rowBottom = rows[rows.Count - 2 - rowIndex];

            cells[rowIndex] = new string[columns.Count - 1];

            for (var columnIndex = 0; columnIndex < columns.Count - 1; columnIndex++)
            {
                cells[rowIndex][columnIndex] = ReadCell(
                    words,
                    columns[columnIndex] - CellPadding,
                    rowBottom - CellPadding,
                    columns[columnIndex + 1] + CellPadding,
                    rowTop + CellPadding);
            }
        }

        return new DetectedTable
        {
            Cells = cells,
            Bounds = [columns[0], rows[0], columns[^1], rows[^1]],
        };
    }

    /// <summary>
    /// Groups the rules into the grids they form, by joining every pair of rules that touch or cross.
    /// </summary>
    /// <param name="rules">The horizontal and vertical rules on the page.</param>
    /// <returns>The groups.</returns>
    /// <remarks>
    /// Two rules belong together when one reaches the other: a horizontal rule that ends where a vertical one
    /// runs, or crosses it. Rules that never meet are different drawings, however close they sit.
    /// </remarks>
    private static List<List<PdfSegment>> GroupTouchingRules(List<PdfSegment> rules)
    {
        var parent = new int[rules.Count];

        for (var index = 0; index < parent.Length; index++)
        {
            parent[index] = index;
        }

        for (var left = 0; left < rules.Count; left++)
        {
            for (var right = left + 1; right < rules.Count; right++)
            {
                if (Touches(rules[left], rules[right]))
                {
                    Union(parent, left, right);
                }
            }
        }

        var groups = new Dictionary<int, List<PdfSegment>>();

        for (var index = 0; index < rules.Count; index++)
        {
            var root = Find(parent, index);

            if (!groups.TryGetValue(root, out var group))
            {
                group = [];
                groups[root] = group;
            }

            group.Add(rules[index]);
        }

        return groups.Values.ToList();
    }

    private static bool Touches(PdfSegment left, PdfSegment right)
    {
        var horizontalGap = Math.Max(0, Math.Max(left.Left - right.Right, right.Left - left.Right));
        var verticalGap = Math.Max(0, Math.Max(left.Bottom - right.Top, right.Bottom - left.Top));

        return horizontalGap <= RuleTolerance && verticalGap <= RuleTolerance;
    }

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }

        return index;
    }

    private static void Union(int[] parent, int left, int right)
    {
        var leftRoot = Find(parent, left);
        var rightRoot = Find(parent, right);

        if (leftRoot != rightRoot)
        {
            parent[rightRoot] = leftRoot;
        }
    }

    private static bool IsHorizontal(PdfSegment segment)
    {
        return Math.Abs(segment.Y1 - segment.Y2) <= RuleTolerance;
    }

    private static bool IsVertical(PdfSegment segment)
    {
        return Math.Abs(segment.X1 - segment.X2) <= RuleTolerance;
    }

    private static bool Spans(
        List<PdfSegment> segments,
        Func<PdfSegment, bool> selector,
        Func<PdfSegment, double> measure,
        double total)
    {
        var covered = segments.Where(selector).Sum(measure);

        return covered >= total * MinimumSpanRatio;
    }

    private static string ReadCell(List<Word> words, double left, double bottom, double right, double top)
    {
        var builder = new StringBuilder();

        foreach (var word in words)
        {
            var box = word.BoundingBox;
            var centreX = (box.Left + box.Right) / 2;
            var centreY = (box.Bottom + box.Top) / 2;

            if (centreX < left || centreX > right || centreY < bottom || centreY > top)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(word.Text);
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Groups nearly identical coordinates into one, so a rule drawn as two overlapping strokes counts once.
    /// </summary>
    /// <param name="values">The coordinates.</param>
    /// <returns>The distinct coordinates, ascending.</returns>
    private static List<double> Cluster(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(value => value).ToList();
        var clusters = new List<double>();
        var current = new List<double>();

        foreach (var value in sorted)
        {
            if (current.Count > 0 && value - current[^1] > RuleTolerance)
            {
                clusters.Add(current.Average());
                current.Clear();
            }

            current.Add(value);
        }

        if (current.Count > 0)
        {
            clusters.Add(current.Average());
        }

        return clusters;
    }
}
