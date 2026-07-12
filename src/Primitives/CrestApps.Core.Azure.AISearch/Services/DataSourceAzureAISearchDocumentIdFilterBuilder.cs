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

        var filterLength = 0;

        checked
        {
            for (var index = 0; index < documentIds.Count; index++)
            {
                filterLength += keyFieldName.Length + documentIds[index].Length + 6;

                if (index > 0)
                {
                    filterLength += 4;
                }

                foreach (var character in documentIds[index])
                {
                    if (character == '\'')
                    {
                        filterLength++;
                    }
                }
            }
        }

        return string.Create(
            filterLength,
            (DocumentIds: documentIds, KeyFieldName: keyFieldName),
            static (destination, state) =>
            {
                var position = 0;

                for (var index = 0; index < state.DocumentIds.Count; index++)
                {
                    if (index > 0)
                    {
                        " or ".AsSpan().CopyTo(destination[position..]);
                        position += 4;
                    }

                    state.KeyFieldName.AsSpan().CopyTo(destination[position..]);
                    position += state.KeyFieldName.Length;
                    " eq '".AsSpan().CopyTo(destination[position..]);
                    position += 5;

                    foreach (var character in state.DocumentIds[index])
                    {
                        destination[position++] = character;

                        if (character == '\'')
                        {
                            destination[position++] = '\'';
                        }
                    }

                    destination[position++] = '\'';
                }
            });
    }
}
