using System.Diagnostics;
using CrestApps.Core.AI.Documents.Pdf.Analysis;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class PdfTextDiffTests
{
    [Fact]
    public void Diff_FindsTheShortestEdit()
    {
        string[] baseline = ["a", "b", "c", "d", "e"];
        string[] other = ["a", "c", "d", "x", "e", "f"];

        var edits = PdfTextDiff.Diff(baseline, other);

        Assert.Equal(
            [
                new PdfDiffEdit(PdfDiffKind.Equal, 0, 0, 1),
                new PdfDiffEdit(PdfDiffKind.Delete, 1, 1, 1),
                new PdfDiffEdit(PdfDiffKind.Equal, 2, 1, 2),
                new PdfDiffEdit(PdfDiffKind.Insert, 4, 3, 1),
                new PdfDiffEdit(PdfDiffKind.Equal, 4, 4, 1),
                new PdfDiffEdit(PdfDiffKind.Insert, 5, 5, 1),
            ],
            edits);
    }

    [Fact]
    public void Diff_EmptySides_AreWholeInsertsOrDeletes()
    {
        Assert.Equal([new PdfDiffEdit(PdfDiffKind.Insert, 0, 0, 2)], PdfTextDiff.Diff([], ["a", "b"]));
        Assert.Equal([new PdfDiffEdit(PdfDiffKind.Delete, 0, 0, 2)], PdfTextDiff.Diff(["a", "b"], []));
        Assert.Empty(PdfTextDiff.Diff([], []));
    }

    [Fact]
    public void Diff_ThousandsOfLines_ReconstructsTheOtherSideQuickly()
    {
        var random = new Random(42);
        var baseline = Enumerable.Range(0, 4_000).Select(index => "line " + (index % 900).ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var other = new List<string>(baseline);

        for (var change = 0; change < 60; change++)
        {
            var position = random.Next(other.Count);

            switch (change % 3)
            {
                case 0:
                    other.RemoveAt(position);

                    break;
                case 1:
                    other.Insert(position, "inserted " + change.ToString(System.Globalization.CultureInfo.InvariantCulture));

                    break;
                default:
                    other[position] = "changed " + change.ToString(System.Globalization.CultureInfo.InvariantCulture);

                    break;
            }
        }

        var watch = Stopwatch.StartNew();
        var edits = PdfTextDiff.Diff(baseline, other);

        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"The diff took {watch.Elapsed}.");
        Assert.Equal(other, Apply(baseline, other, edits));
        Assert.True(edits.Where(edit => edit.Kind != PdfDiffKind.Equal).Sum(edit => edit.Length) <= 120, "The edit is no longer than the changes made.");
    }

    [Fact]
    public void Diff_WorkCap_ReportsTheRegionAsReplaced()
    {
        var baseline = Enumerable.Range(0, 300).Select(index => "a" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var other = Enumerable.Range(0, 300).Select(index => "b" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();

        var edits = PdfTextDiff.Diff(baseline, other, maxSteps: 10);

        Assert.Equal([new PdfDiffEdit(PdfDiffKind.Delete, 0, 0, 300), new PdfDiffEdit(PdfDiffKind.Insert, 300, 0, 300)], edits);
    }

    [Fact]
    public void WordDiff_MarksRemovedAndAddedWords()
    {
        var diff = PdfTextDiff.WordDiff("Revenue rose by twelve percent", "Revenue rose by fifteen percent");

        Assert.Equal("Revenue rose by [-twelve-] {+fifteen+} percent", diff);
    }

    [Fact]
    public void WordDiff_LongUnchangedRuns_AreElided()
    {
        var shared = string.Join(' ', Enumerable.Range(1, 30).Select(index => "w" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var diff = PdfTextDiff.WordDiff(shared + " old", shared + " new", context: 3);

        Assert.Equal("… w28 w29 w30 [-old-] {+new+}", diff);
    }

    [Fact]
    public void Similarity_ComparesSharedWords()
    {
        Assert.Equal(1, PdfTextDiff.Similarity("a b c", "c b a"));
        Assert.Equal(0, PdfTextDiff.Similarity("a b", "c d"));
        Assert.Equal(0.5, PdfTextDiff.Similarity("a b", "a c"));
    }

    private static List<string> Apply(List<string> baseline, List<string> other, List<PdfDiffEdit> edits)
    {
        var result = new List<string>();

        foreach (var edit in edits)
        {
            switch (edit.Kind)
            {
                case PdfDiffKind.Equal:
                    result.AddRange(baseline.Skip(edit.BaselineStart).Take(edit.Length));

                    break;
                case PdfDiffKind.Insert:
                    result.AddRange(other.Skip(edit.OtherStart).Take(edit.Length));

                    break;
            }
        }

        return result;
    }
}
