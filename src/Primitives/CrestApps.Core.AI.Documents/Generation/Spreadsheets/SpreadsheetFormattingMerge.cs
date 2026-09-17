namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// Combines a new formatting request with the formatting already recorded for a table.
/// <para>
/// Formatting arrives one request at a time — currency first, then a gradient, then a total row — and
/// each request only describes what is changing. Merging means the user never has to restate earlier
/// instructions, and never sees an earlier instruction silently disappear.
/// </para>
/// </summary>
public static class SpreadsheetFormattingMerge
{
    /// <summary>
    /// Merges a request into an existing specification.
    /// </summary>
    /// <param name="existing">The recorded specification. May be <see langword="null"/>.</param>
    /// <param name="request">The new request.</param>
    /// <returns>The merged specification.</returns>
    public static SpreadsheetFormatting Merge(SpreadsheetFormatting existing, SpreadsheetFormatting request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (existing is null)
        {
            return request;
        }

        var merged = new SpreadsheetFormatting
        {
            SheetName = Coalesce(request.SheetName, existing.SheetName),
            BandColor = Coalesce(request.BandColor, existing.BandColor),
            StyleHeader = request.StyleHeader ?? existing.StyleHeader,
            FreezeHeader = request.FreezeHeader ?? existing.FreezeHeader,
            AutoFilter = request.AutoFilter ?? existing.AutoFilter,
            BandedRows = request.BandedRows ?? existing.BandedRows,
            HeaderStyle = request.HeaderStyle ?? existing.HeaderStyle,

            // A total row and a chart set are each described in full when they are described at all, so
            // a new one replaces the old rather than merging into it and leaving a stale aggregate or a
            // duplicate chart behind.
            TotalRow = request.TotalRow ?? existing.TotalRow,
            Charts = request.Charts is { Count: > 0 } ? request.Charts : existing.Charts,
            ProtectSheet = request.ProtectSheet ?? existing.ProtectSheet,

            // Merges and named ranges are geometry: a new set describes the whole layout, so it replaces
            // the old one rather than accumulating stale ranges against a sheet that has since changed.
            MergedCells = request.MergedCells is { Count: > 0 } ? request.MergedCells : existing.MergedCells,
            NamedRanges = request.NamedRanges is { Count: > 0 } ? request.NamedRanges : existing.NamedRanges,
        };

        MergeColumns(existing, request, merged);
        MergeConditionalFormats(existing, request, merged);

        return merged;
    }

    private static void MergeColumns(
        SpreadsheetFormatting existing,
        SpreadsheetFormatting request,
        SpreadsheetFormatting merged)
    {
        // The requested columns keep their order and win outright, so restating a column replaces its
        // whole format instead of leaving half of the previous one behind.
        foreach (var column in request.Columns)
        {
            if (column is not null && !string.IsNullOrWhiteSpace(column.Column))
            {
                merged.Columns.Add(column);
            }
        }

        foreach (var column in existing.Columns)
        {
            if (column is null || string.IsNullOrWhiteSpace(column.Column))
            {
                continue;
            }

            if (!ContainsColumn(request.Columns, column.Column))
            {
                merged.Columns.Add(column);
            }
        }
    }

    private static void MergeConditionalFormats(
        SpreadsheetFormatting existing,
        SpreadsheetFormatting request,
        SpreadsheetFormatting merged)
    {
        foreach (var conditional in request.ConditionalFormats)
        {
            if (conditional is not null && !string.IsNullOrWhiteSpace(conditional.Column))
            {
                merged.ConditionalFormats.Add(conditional);
            }
        }

        foreach (var conditional in existing.ConditionalFormats)
        {
            if (conditional is null || string.IsNullOrWhiteSpace(conditional.Column))
            {
                continue;
            }

            // A rule is replaced only by a rule of the same kind on the same column. A gradient and a
            // negatives-in-red rule on one column are different requests and both survive.
            if (!ContainsRule(request.ConditionalFormats, conditional))
            {
                merged.ConditionalFormats.Add(conditional);
            }
        }
    }

    private static bool ContainsColumn(IList<SpreadsheetColumnFormat> columns, string name)
    {
        foreach (var column in columns)
        {
            if (SpreadsheetFormatting.NameMatches(column?.Column, name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsRule(
        IList<SpreadsheetConditionalFormat> conditionals,
        SpreadsheetConditionalFormat candidate)
    {
        foreach (var conditional in conditionals)
        {
            if (conditional is not null &&
                conditional.Rule == candidate.Rule &&
                SpreadsheetFormatting.NameMatches(conditional.Column, candidate.Column))
            {
                return true;
            }
        }

        return false;
    }

    private static string Coalesce(string preferred, string fallback)
    {
        return string.IsNullOrWhiteSpace(preferred)
            ? fallback
            : preferred;
    }
}
