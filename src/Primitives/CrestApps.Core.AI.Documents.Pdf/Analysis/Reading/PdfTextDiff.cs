using System.Runtime.InteropServices;
using System.Text;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// The kind of an edit a diff reports.
/// </summary>
internal enum PdfDiffKind
{
    /// <summary>
    /// Items both sequences share.
    /// </summary>
    Equal,

    /// <summary>
    /// Items only the baseline has.
    /// </summary>
    Delete,

    /// <summary>
    /// Items only the other sequence has.
    /// </summary>
    Insert,
}

/// <summary>
/// One run of a diff: a number of consecutive items that are shared, removed or added.
/// </summary>
/// <param name="Kind">What happened to the items.</param>
/// <param name="BaselineStart">Where the run starts in the baseline; for an insert, where it would go.</param>
/// <param name="OtherStart">Where the run starts in the other sequence; for a delete, where it would have been.</param>
/// <param name="Length">The number of items.</param>
internal readonly record struct PdfDiffEdit(PdfDiffKind Kind, int BaselineStart, int OtherStart, int Length);

/// <summary>
/// Finds the differences between two sequences of text — the lines of two documents, or the words of two
/// lines — as the shortest run of removals and additions that turns one into the other.
/// </summary>
/// <remarks>
/// This is Myers' O(ND) algorithm in its linear-space form: the middle snake is found by searching forward
/// and backward at once and the halves on either side are solved in turn, after the common prefix and suffix
/// are set aside. Memory stays proportional to the input, and time to the input times the number of edits,
/// so a few thousand lines with a handful of changes compare instantly. A pair of long, wholly different
/// inputs would still take long, so the work is capped: past the cap, a region is reported as replaced.
/// </remarks>
internal static class PdfTextDiff
{
    /// <summary>
    /// The default cap on the steps one diff takes before the rest is reported as replaced.
    /// </summary>
    public const long DefaultMaxSteps = 40_000_000;

    /// <summary>
    /// Diffs two sequences of text.
    /// </summary>
    /// <param name="baseline">The baseline.</param>
    /// <param name="other">The sequence compared with it.</param>
    /// <param name="maxSteps">The most steps taken before a region is reported as replaced.</param>
    /// <returns>The edits, in order, adjacent runs of the same kind merged.</returns>
    public static List<PdfDiffEdit> Diff(IReadOnlyList<string> baseline, IReadOnlyList<string> other, long maxSteps = DefaultMaxSteps)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(other);

        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var a = ToIds(baseline, ids);
        var b = ToIds(other, ids);
        var state = new DiffState(a, b, maxSteps);

        state.DiffRange(0, a.Length, 0, b.Length);

        return state.Edits;
    }

    /// <summary>
    /// Shows how one piece of text became another, word by word: removed words as <c>[-old-]</c> and added
    /// ones as <c>{+new+}</c>.
    /// </summary>
    /// <param name="before">The baseline text.</param>
    /// <param name="after">The other text.</param>
    /// <param name="context">The most unchanged words kept on each side of a change; the rest of a long unchanged run is shown as an ellipsis. Zero or less keeps every word.</param>
    /// <returns>The marked-up text.</returns>
    public static string WordDiff(string before, string after, int context = 8)
    {
        var first = Words(before);
        var second = Words(after);
        var edits = Diff(first, second);
        var parts = new List<string>();

        for (var index = 0; index < edits.Count; index++)
        {
            var edit = edits[index];

            switch (edit.Kind)
            {
                case PdfDiffKind.Equal:
                    parts.Add(Context(first, edit.BaselineStart, edit.Length, context, index == 0, index == edits.Count - 1));

                    break;

                case PdfDiffKind.Delete:
                    parts.Add("[-" + string.Join(' ', first.Skip(edit.BaselineStart).Take(edit.Length)) + "-]");

                    break;

                case PdfDiffKind.Insert:
                    parts.Add("{+" + string.Join(' ', second.Skip(edit.OtherStart).Take(edit.Length)) + "+}");

                    break;
            }
        }

        return string.Join(' ', parts.Where(part => part.Length > 0));
    }

    /// <summary>
    /// Measures how alike two pieces of text are, by the words they share.
    /// </summary>
    /// <param name="first">The first text.</param>
    /// <param name="second">The second text.</param>
    /// <returns>From 0, nothing in common, to 1, the same words.</returns>
    public static double Similarity(string first, string second)
    {
        var a = Words(first);
        var b = Words(second);

        if (a.Count == 0 && b.Count == 0)
        {
            return 1;
        }

        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in a)
        {
            CollectionsMarshal.GetValueRefOrAddDefault(counts, word, out _)++;
        }

        var shared = 0;

        foreach (var word in b)
        {
            if (counts.TryGetValue(word, out var count) && count > 0)
            {
                counts[word] = count - 1;
                shared++;
            }
        }

        return 2.0 * shared / (a.Count + b.Count);
    }

    /// <summary>
    /// Splits text into words at whitespace.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The words.</returns>
    public static List<string> Words(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)];
    }

    private static string Context(List<string> words, int start, int length, int context, bool isFirst, bool isLast)
    {
        if (context <= 0 || length <= context * 2)
        {
            return string.Join(' ', words.Skip(start).Take(length));
        }

        var builder = new StringBuilder();

        // The words next to a change are what locate it; the rest of a long unchanged run is elided.
        if (!isFirst)
        {
            builder.Append(string.Join(' ', words.Skip(start).Take(context))).Append(' ');
        }

        builder.Append('…');

        if (!isLast)
        {
            builder.Append(' ').Append(string.Join(' ', words.Skip(start + length - context).Take(context)));
        }

        return builder.ToString();
    }

    private static int[] ToIds(IReadOnlyList<string> items, Dictionary<string, int> ids)
    {
        var result = new int[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index] ?? string.Empty;

            if (!ids.TryGetValue(item, out var id))
            {
                id = ids.Count;
                ids[item] = id;
            }

            result[index] = id;
        }

        return result;
    }

    /// <summary>
    /// The sequences being compared, the edits found so far and the work done.
    /// </summary>
    private sealed class DiffState
    {
        private readonly int[] _a;
        private readonly int[] _b;
        private readonly long _maxSteps;
        private long _steps;

        /// <summary>
        /// Initializes a new instance of the <see cref="DiffState"/> class.
        /// </summary>
        /// <param name="a">The baseline, as item identifiers.</param>
        /// <param name="b">The other sequence, as item identifiers.</param>
        /// <param name="maxSteps">The most steps taken.</param>
        public DiffState(int[] a, int[] b, long maxSteps)
        {
            _a = a;
            _b = b;
            _maxSteps = maxSteps <= 0
                ? long.MaxValue
                : maxSteps;
        }

        /// <summary>
        /// Gets the edits found so far.
        /// </summary>
        public List<PdfDiffEdit> Edits { get; } = [];

        /// <summary>
        /// Diffs one region of both sequences, appending its edits in order.
        /// </summary>
        /// <param name="aStart">The region's start in the baseline.</param>
        /// <param name="aEnd">The region's end in the baseline, exclusive.</param>
        /// <param name="bStart">The region's start in the other sequence.</param>
        /// <param name="bEnd">The region's end in the other sequence, exclusive.</param>
        public void DiffRange(int aStart, int aEnd, int bStart, int bEnd)
        {
            var prefix = 0;

            while (aStart + prefix < aEnd && bStart + prefix < bEnd && _a[aStart + prefix] == _b[bStart + prefix])
            {
                prefix++;
            }

            Emit(PdfDiffKind.Equal, aStart, bStart, prefix);

            aStart += prefix;
            bStart += prefix;

            var suffix = 0;

            while (aEnd - suffix > aStart && bEnd - suffix > bStart && _a[aEnd - suffix - 1] == _b[bEnd - suffix - 1])
            {
                suffix++;
            }

            aEnd -= suffix;
            bEnd -= suffix;

            if (aStart == aEnd)
            {
                Emit(PdfDiffKind.Insert, aStart, bStart, bEnd - bStart);
            }
            else if (bStart == bEnd)
            {
                Emit(PdfDiffKind.Delete, aStart, bStart, aEnd - aStart);
            }
            else
            {
                var (x, y) = Bisect(aStart, aEnd, bStart, bEnd);

                if (x < 0)
                {
                    // Nothing in common, or the work cap was reached: the region is replaced.
                    Emit(PdfDiffKind.Delete, aStart, bStart, aEnd - aStart);
                    Emit(PdfDiffKind.Insert, aEnd, bStart, bEnd - bStart);
                }
                else
                {
                    DiffRange(aStart, x, bStart, y);
                    DiffRange(x, aEnd, y, bEnd);
                }
            }

            Emit(PdfDiffKind.Equal, aEnd, bEnd, suffix);
        }

        private (int X, int Y) Bisect(int aStart, int aEnd, int bStart, int bEnd)
        {
            var n = aEnd - aStart;
            var m = bEnd - bStart;
            var maxD = (n + m + 1) / 2;
            var offset = maxD;
            var length = (2 * maxD) + 2;
            var forward = new int[length];
            var backward = new int[length];

            Array.Fill(forward, -1);
            Array.Fill(backward, -1);

            forward[offset + 1] = 0;
            backward[offset + 1] = 0;

            var delta = n - m;
            var front = delta % 2 != 0;
            var k1Start = 0;
            var k1End = 0;
            var k2Start = 0;
            var k2End = 0;

            for (var d = 0; d < maxD; d++)
            {
                for (var k1 = -d + k1Start; k1 <= d - k1End; k1 += 2)
                {
                    var k1Offset = offset + k1;
                    var x1 = k1 == -d || (k1 != d && forward[k1Offset - 1] < forward[k1Offset + 1])
                        ? forward[k1Offset + 1]
                        : forward[k1Offset - 1] + 1;
                    var y1 = x1 - k1;

                    while (x1 < n && y1 < m && _a[aStart + x1] == _b[bStart + y1])
                    {
                        x1++;
                        y1++;
                    }

                    forward[k1Offset] = x1;

                    if (++_steps > _maxSteps)
                    {
                        return (-1, -1);
                    }

                    if (x1 > n)
                    {
                        k1End += 2;
                    }
                    else if (y1 > m)
                    {
                        k1Start += 2;
                    }
                    else if (front)
                    {
                        var k2Offset = offset + delta - k1;

                        if (k2Offset >= 0 && k2Offset < length && backward[k2Offset] != -1 && x1 >= n - backward[k2Offset])
                        {
                            return (aStart + x1, bStart + y1);
                        }
                    }
                }

                for (var k2 = -d + k2Start; k2 <= d - k2End; k2 += 2)
                {
                    var k2Offset = offset + k2;
                    var x2 = k2 == -d || (k2 != d && backward[k2Offset - 1] < backward[k2Offset + 1])
                        ? backward[k2Offset + 1]
                        : backward[k2Offset - 1] + 1;
                    var y2 = x2 - k2;

                    while (x2 < n && y2 < m && _a[aEnd - x2 - 1] == _b[bEnd - y2 - 1])
                    {
                        x2++;
                        y2++;
                    }

                    backward[k2Offset] = x2;

                    if (++_steps > _maxSteps)
                    {
                        return (-1, -1);
                    }

                    if (x2 > n)
                    {
                        k2End += 2;
                    }
                    else if (y2 > m)
                    {
                        k2Start += 2;
                    }
                    else if (!front)
                    {
                        var k1Offset = offset + delta - k2;

                        if (k1Offset >= 0 && k1Offset < length && forward[k1Offset] != -1)
                        {
                            var x1 = forward[k1Offset];
                            var y1 = offset + x1 - k1Offset;

                            if (x1 >= n - x2)
                            {
                                return (aStart + x1, bStart + y1);
                            }
                        }
                    }
                }
            }

            return (-1, -1);
        }

        private void Emit(PdfDiffKind kind, int aStart, int bStart, int length)
        {
            if (length <= 0)
            {
                return;
            }

            if (Edits.Count > 0 && Edits[^1].Kind == kind)
            {
                var last = Edits[^1];

                Edits[^1] = last with { Length = last.Length + length };

                return;
            }

            Edits.Add(new PdfDiffEdit(kind, aStart, bStart, length));
        }
    }
}
