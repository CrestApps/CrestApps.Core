namespace CrestApps.Core.Azure.AISearch.Services;

/// <summary>
/// Prepares document identifiers and OData filters for Azure AI Search document reads.
/// </summary>
internal static class DataSourceAzureAISearchDocumentIdFilterBuilder
{
    /// <summary>
    /// Copies non-null and non-empty document identifiers while preserving their order and identity.
    /// </summary>
    /// <param name="documentIds">The document identifiers to filter.</param>
    /// <returns>The filtered document identifiers.</returns>
    public static List<string> FilterDocumentIds(IEnumerable<string> documentIds)
    {
        var result = documentIds.TryGetNonEnumeratedCount(out var count)
            ? new List<string>(count)
            : [];

        foreach (var documentId in documentIds)
        {
            if (!string.IsNullOrEmpty(documentId))
            {
                result.Add(documentId);
            }
        }

        return result;
    }

    /// <summary>
    /// Builds an OData equality filter for the specified document identifiers.
    /// </summary>
    /// <param name="documentIds">The document identifiers to include.</param>
    /// <param name="keyFieldName">The explicit key field name, or <see langword="null"/> to use direct key lookup.</param>
    /// <returns>The OData filter, or <see langword="null"/> when no key field name is provided.</returns>
    public static string BuildFilter(IReadOnlyList<string> documentIds, string keyFieldName)
    {
        if (string.IsNullOrEmpty(keyFieldName))
        {
            return null;
        }

        return ODataFilter.AnyEqual(keyFieldName, documentIds);
    }
}
