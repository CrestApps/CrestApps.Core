namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Flags numeric columns whose name reads as a grand total of sibling numeric columns, so the model
/// is nudged toward the real total instead of picking a component column (or re-deriving the total by
/// hand) when several plausible-looking numeric columns share a topic.
/// </summary>
internal static class TabularColumnAmbiguityDetector
{
    /// <summary>
    /// Describes any "total" column found among numeric siblings that share its last name segment
    /// (for example <c>Projected_Revenue</c>, <c>Ancillary_Revenue</c>, and <c>Total_Revenue</c> all
    /// end in <c>Revenue</c>). Only fires when exactly one sibling in the group looks like a total,
    /// since a group with none or several is not disambiguated by picking one.
    /// </summary>
    /// <param name="columns">The table's columns, with a flag for whether each is numeric.</param>
    /// <returns>A one-line note per ambiguous group, or <see langword="null"/> when none are found.</returns>
    public static string DescribeAmbiguousTotals(IReadOnlyList<(string Name, bool IsNumeric)> columns)
    {
        if (columns is not { Count: > 1 })
        {
            return null;
        }

        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            if (!column.IsNumeric)
            {
                continue;
            }

            var key = GetLastNamedSegment(column.Name);

            if (key is null)
            {
                continue;
            }

            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups[key] = group;
            }

            group.Add(column.Name);
        }

        List<string> notes = null;

        foreach (var group in groups.Values)
        {
            if (group.Count < 2)
            {
                continue;
            }

            var totalColumns = group.Where(IsTotalColumn).ToList();

            if (totalColumns.Count != 1)
            {
                continue;
            }

            var total = totalColumns[0];
            var components = string.Join(" + ", group.Where(name => name != total));

            notes ??= [];
            notes.Add($"{total} looks like the sum of {components} — prefer it over adding the others yourself.");
        }

        return notes is null ? null : "NOTE: " + string.Join(" ", notes);
    }

    // A trailing all-digit segment is a date or index suffix disambiguating duplicate headers (for
    // example three "Total Proj. Revnue" columns, one per month, become Total_Proj__Revnue_2026_09_01
    // and so on), not a shared topic — grouping on it would lump unrelated months together.
    private static string GetLastNamedSegment(string name)
    {
        var segment = name[(name.LastIndexOf('_') + 1)..];

        return segment.Any(char.IsLetter) ? segment : null;
    }

    private static bool IsTotalColumn(string name)
    {
        return name.Split('_').Any(segment => string.Equals(segment, "Total", StringComparison.OrdinalIgnoreCase));
    }
}
