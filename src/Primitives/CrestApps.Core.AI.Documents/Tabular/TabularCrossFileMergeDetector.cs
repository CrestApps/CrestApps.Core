using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Notices when a turn produces a second key/measure result drawn from a different uploaded file, which
/// is the point where the two result sets are about to be merged by hand. That merge cannot be observed
/// once it happens, because it takes place in the answer text rather than in a tool call, and it fails
/// quietly: a key present on only one side reads as a zero rather than as a mismatch, and every total
/// has to be added up unaided. The comparison tool does the same join in code, so the caller is pointed
/// at it while both queries are still available.
/// </summary>
internal static class TabularCrossFileMergeDetector
{
    private const string TrackedDocumentsKey = nameof(TabularCrossFileMergeDetector) + ".KeyMeasureDocuments";

    /// <summary>
    /// Records a query result for the current turn and reports whether it completes a cross-file pair.
    /// </summary>
    /// <param name="result">The query result.</param>
    /// <param name="sql">The query that produced the result.</param>
    /// <param name="tables">The tables loaded in the workspace.</param>
    /// <returns>Guidance to append to the result, or <see langword="null"/> when nothing is pending.</returns>
    public static string Track(TabularQueryResult result, string sql, IReadOnlyList<TabularTableInfo> tables)
    {
        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null || !TabularResultAnalyzer.IsKeyMeasureShape(result))
        {
            return null;
        }

        var documentIds = TabularResultAnalyzer.FindReferencedTables(sql, tables)
            .Select(table => table.SourceDocumentId)
            .Where(documentId => !string.IsNullOrEmpty(documentId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (documentIds.Count == 0)
        {
            return null;
        }

        if (!invocationContext.Items.TryGetValue(TrackedDocumentsKey, out var trackedObject) ||
            trackedObject is not HashSet<string> tracked)
        {
            tracked = new HashSet<string>(StringComparer.Ordinal);
            invocationContext.Items[TrackedDocumentsKey] = tracked;
        }

        // A query that already spans both files joined them in SQL, which is the outcome being steered
        // toward, so record it and stay quiet.
        if (documentIds.Count > 1)
        {
            foreach (var documentId in documentIds)
            {
                tracked.Add(documentId);
            }

            return null;
        }

        // Only a document that was not already aggregated this turn signals a pending merge; re-running
        // one side to harmonize its key names is a normal step and must not be interrupted.
        var isNewDocument = documentIds.Any(documentId => !tracked.Contains(documentId));
        var hasEarlierDocument = tracked.Count > 0;

        foreach (var documentId in documentIds)
        {
            tracked.Add(documentId);
        }

        if (!hasEarlierDocument || !isNewDocument)
        {
            return null;
        }

        return "NOTE: this is an aggregated key/value result from a different uploaded file than an earlier result in this same request, so a comparison is likely being assembled. Do not combine the two result sets in your answer. Doing so requires adding the numbers up unaided, and a key that appears on only one side would be reported as a zero even when the two files simply name or group that key differently. Call " +
            TabularToolNames.CompareTabularData +
            " with the two queries as left_sql and right_sql: it joins them, computes each difference and the totals, and lists any key it could not match.";
    }
}
