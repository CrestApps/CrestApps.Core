using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// How one check of a quality report came out.
/// </summary>
internal enum PdfCheckStatus
{
    /// <summary>
    /// The requirement is met.
    /// </summary>
    Pass,

    /// <summary>
    /// Something worth knowing that is neither right nor wrong.
    /// </summary>
    Info,

    /// <summary>
    /// A likely problem, or one that depends on what the author intended.
    /// </summary>
    Warn,

    /// <summary>
    /// The requirement is not met.
    /// </summary>
    Fail,
}

/// <summary>
/// One check of a quality report.
/// </summary>
/// <param name="Category">The group the check belongs to, or <see langword="null"/>.</param>
/// <param name="Name">The short name of the check.</param>
/// <param name="Status">How it came out.</param>
/// <param name="Finding">What was found, with page numbers.</param>
/// <param name="Fix">How to fix it, or <see langword="null"/>.</param>
internal sealed record PdfCheck(string Category, string Name, PdfCheckStatus Status, string Finding, string Fix);

/// <summary>
/// Collects the checks a quality tool runs and writes them as the checklist the model reads: a one-line
/// verdict with counts, then every check with its status, finding and fix.
/// </summary>
internal sealed class PdfCheckList
{
    private readonly List<PdfCheck> _checks = [];

    /// <summary>
    /// Gets the checks, in the order they were added.
    /// </summary>
    public IReadOnlyList<PdfCheck> Checks => _checks;

    /// <summary>
    /// Gets or sets the category the next checks are filed under.
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    /// Records a check.
    /// </summary>
    /// <param name="status">How it came out.</param>
    /// <param name="name">The short name of the check.</param>
    /// <param name="finding">What was found.</param>
    /// <param name="fix">How to fix it, or <see langword="null"/>.</param>
    /// <returns>The check.</returns>
    public PdfCheck Add(PdfCheckStatus status, string name, string finding, string fix = null)
    {
        var check = new PdfCheck(Category, name, status, finding, fix);
        _checks.Add(check);

        return check;
    }

    /// <summary>
    /// Records a check that passed.
    /// </summary>
    /// <param name="name">The short name of the check.</param>
    /// <param name="finding">What was found.</param>
    /// <returns>The check.</returns>
    public PdfCheck Pass(string name, string finding)
    {
        return Add(PdfCheckStatus.Pass, name, finding);
    }

    /// <summary>
    /// Records something worth knowing.
    /// </summary>
    /// <param name="name">The short name of the check.</param>
    /// <param name="finding">What was found.</param>
    /// <param name="fix">What could be done, or <see langword="null"/>.</param>
    /// <returns>The check.</returns>
    public PdfCheck Info(string name, string finding, string fix = null)
    {
        return Add(PdfCheckStatus.Info, name, finding, fix);
    }

    /// <summary>
    /// Records a likely problem.
    /// </summary>
    /// <param name="name">The short name of the check.</param>
    /// <param name="finding">What was found.</param>
    /// <param name="fix">How to fix it, or <see langword="null"/>.</param>
    /// <returns>The check.</returns>
    public PdfCheck Warn(string name, string finding, string fix = null)
    {
        return Add(PdfCheckStatus.Warn, name, finding, fix);
    }

    /// <summary>
    /// Records a requirement that is not met.
    /// </summary>
    /// <param name="name">The short name of the check.</param>
    /// <param name="finding">What was found.</param>
    /// <param name="fix">How to fix it, or <see langword="null"/>.</param>
    /// <returns>The check.</returns>
    public PdfCheck Fail(string name, string finding, string fix = null)
    {
        return Add(PdfCheckStatus.Fail, name, finding, fix);
    }

    /// <summary>
    /// Counts the checks with a status.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The count.</returns>
    public int Count(PdfCheckStatus status)
    {
        return _checks.Count(check => check.Status == status);
    }

    /// <summary>
    /// Describes the counts, for example <c>1 failed, 2 warnings, 9 passed, 3 info</c>.
    /// </summary>
    /// <returns>The counts.</returns>
    public string DescribeCounts()
    {
        var warnings = Count(PdfCheckStatus.Warn);
        var warningWord = warnings == 1
            ? "warning"
            : "warnings";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Count(PdfCheckStatus.Fail)} failed, {warnings} {warningWord}, {Count(PdfCheckStatus.Pass)} passed, {Count(PdfCheckStatus.Info)} info");
    }

    /// <summary>
    /// Writes the report.
    /// </summary>
    /// <param name="title">The first line, naming the report and the PDF.</param>
    /// <param name="verdict">The one-line verdict.</param>
    /// <param name="bySeverity">Whether checks are listed failures first; otherwise they are listed by category in the order they ran.</param>
    /// <param name="notes">Closing notes, such as what the checks cannot tell.</param>
    /// <returns>The report.</returns>
    public string Render(string title, string verdict, bool bySeverity, IEnumerable<string> notes = null)
    {
        var builder = new StringBuilder();

        builder.AppendLine(title);
        builder.Append("Verdict: ").Append(verdict).Append(" (").Append(DescribeCounts()).AppendLine(").");

        if (bySeverity)
        {
            foreach (var status in new[] { PdfCheckStatus.Fail, PdfCheckStatus.Warn, PdfCheckStatus.Pass, PdfCheckStatus.Info })
            {
                var checks = _checks.Where(check => check.Status == status).ToList();

                if (checks.Count == 0)
                {
                    continue;
                }

                builder.AppendLine().AppendLine(Heading(status));

                foreach (var check in checks)
                {
                    AppendCheck(builder, check);
                }
            }
        }
        else
        {
            string current = null;
            var first = true;

            foreach (var check in _checks)
            {
                if (first || !string.Equals(current, check.Category, StringComparison.Ordinal))
                {
                    builder.AppendLine();

                    if (!string.IsNullOrEmpty(check.Category))
                    {
                        builder.AppendLine(check.Category);
                    }

                    current = check.Category;
                    first = false;
                }

                AppendCheck(builder, check);
            }
        }

        var closing = notes?.Where(note => !string.IsNullOrWhiteSpace(note)).ToList() ?? [];

        if (closing.Count > 0)
        {
            builder.AppendLine();

            foreach (var note in closing)
            {
                builder.Append("Note: ").AppendLine(note);
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Writes a set of pages compactly and boundedly, for example <c>1-3, 5, 9 and 12 more pages</c>.
    /// </summary>
    /// <param name="pages">The one-based pages.</param>
    /// <param name="maxParts">The most ranges written before the rest is counted.</param>
    /// <returns>The pages.</returns>
    public static string Pages(IEnumerable<int> pages, int maxParts = 10)
    {
        var sorted = pages?.Distinct().Order().ToList() ?? [];

        if (sorted.Count == 0)
        {
            return "none";
        }

        var ranges = new List<(int Start, int End)>();

        foreach (var page in sorted)
        {
            if (ranges.Count > 0 && ranges[^1].End == page - 1)
            {
                ranges[^1] = (ranges[^1].Start, page);
            }
            else
            {
                ranges.Add((page, page));
            }
        }

        var text = string.Join(", ", ranges.Take(maxParts).Select(range => range.Start == range.End
            ? range.Start.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{range.Start}-{range.End}")));

        var remaining = ranges.Skip(maxParts).Sum(range => range.End - range.Start + 1);
        var prefix = sorted.Count == 1
            ? "page "
            : "pages ";

        return remaining == 0
            ? prefix + text
            : prefix + text + string.Create(CultureInfo.InvariantCulture, $" and {remaining} more");
    }

    /// <summary>
    /// Writes a set of pages followed by a verb that agrees with it, for example <c>page 2 has</c> or
    /// <c>pages 2-3 have</c>.
    /// </summary>
    /// <param name="pages">The one-based pages.</param>
    /// <param name="singular">The verb for one page.</param>
    /// <param name="plural">The verb for several pages.</param>
    /// <returns>The pages and the verb.</returns>
    public static string Pages(IEnumerable<int> pages, string singular, string plural)
    {
        var distinct = pages?.Distinct().ToList() ?? [];
        var verb = distinct.Count == 1
            ? singular
            : plural;

        return Pages(distinct) + " " + verb;
    }

    /// <summary>
    /// Writes a bounded list, for example <c>a, b, c and 4 more</c>.
    /// </summary>
    /// <param name="items">The items.</param>
    /// <param name="max">The most items written before the rest is counted.</param>
    /// <returns>The list.</returns>
    public static string List(IEnumerable<string> items, int max = 6)
    {
        var all = items?.Where(item => !string.IsNullOrWhiteSpace(item)).ToList() ?? [];

        if (all.Count <= max)
        {
            return string.Join("; ", all);
        }

        return string.Join("; ", all.Take(max)) + string.Create(CultureInfo.InvariantCulture, $" and {all.Count - max} more");
    }

    /// <summary>
    /// Quotes a piece of text from the PDF, shortened and on one line.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="max">The most characters kept.</param>
    /// <returns>The quoted text.</returns>
    public static string Quote(string text, int max = 50)
    {
        var single = string.Join(' ', (text ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        if (single.Length > max)
        {
            single = single[..max].TrimEnd() + "…";
        }

        return "\"" + single + "\"";
    }

    /// <summary>
    /// Writes a length in points as millimetres.
    /// </summary>
    /// <param name="points">The length in points.</param>
    /// <returns>For example <c>4.2 mm</c>.</returns>
    public static string Millimetres(double points)
    {
        return Math.Round(PdfPageSizes.ToMillimetres(points), 1).ToString(CultureInfo.InvariantCulture) + " mm";
    }

    private static string Heading(PdfCheckStatus status)
    {
        return status switch
        {
            PdfCheckStatus.Fail => "Errors",
            PdfCheckStatus.Warn => "Warnings",
            PdfCheckStatus.Pass => "Passed",
            _ => "Information",
        };
    }

    private static void AppendCheck(StringBuilder builder, PdfCheck check)
    {
        var label = check.Status switch
        {
            PdfCheckStatus.Fail => "FAIL",
            PdfCheckStatus.Warn => "WARN",
            PdfCheckStatus.Pass => "PASS",
            _ => "INFO",
        };

        builder.Append("- [").Append(label).Append("] ").Append(check.Name).Append(": ").AppendLine(check.Finding);

        if (!string.IsNullOrWhiteSpace(check.Fix))
        {
            builder.Append("  Fix: ").AppendLine(check.Fix);
        }
    }
}
