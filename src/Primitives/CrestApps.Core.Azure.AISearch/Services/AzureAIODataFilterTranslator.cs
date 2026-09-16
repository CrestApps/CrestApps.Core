using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;

namespace CrestApps.Core.Azure.AISearch.Services;

/// <summary>
/// Translates OData filter expressions for Azure AI Search.
/// Since Azure AI Search natively supports OData, this translator
/// simply prefixes field names with "filters/" to target the filter fields
/// in the knowledge base index.
/// </summary>
internal sealed class AzureAIODataFilterTranslator : IODataFilterTranslator
{
    /// <summary>
    /// Translates the operation.
    /// </summary>
    /// <param name="odataFilter">The odata filter.</param>
    public string Translate(string odataFilter)
    {
        if (string.IsNullOrWhiteSpace(odataFilter))
        {
            return null;
        }

        // Azure AI Search uses OData natively.
        // Prefix field names with "filters/" to target the correct fields.
        // Note: Azure AI Search uses "/" as nested field separator.

        return PrefixFieldNames(odataFilter);
    }

    private static string PrefixFieldNames(string filter)
    {
        // Simple approach: identify unquoted identifiers and prefix them.
        // OData field names appear before operators (eq, ne, gt, lt, ge, le)
        // and inside function calls (contains, startswith, endswith).
        var result = new System.Text.StringBuilder();
        var i = 0;

        while (i < filter.Length)
        {
            // Skip quoted strings.

            if (filter[i] == '\'')
            {
                var end = filter.IndexOf('\'', i + 1);

                if (end < 0)
                {
                    end = filter.Length - 1;
                }

                result.Append(filter, i, end - i + 1);
                i = end + 1;
                continue;
            }

            // Identify tokens.

            if (char.IsLetter(filter[i]) || filter[i] == '_')
            {
                var start = i;

                while (i < filter.Length && (char.IsLetterOrDigit(filter[i]) || filter[i] == '_' || filter[i] == '.'))
                {
                    i++;
                }

                var token = filter[start..i];

                // Don't prefix OData keywords and function names.

                if (IsODataKeyword(token))
                {
                    result.Append(token);
                }
                else if (DataSourceConstants.ColumnNames.IsTypedColumn(token, IsKnowledgeScope(token)))
                {
                    // The typed discriminators are fields of their own, not entries in the filters bag —
                    // but only when the name is read in the knowledge base's own scope.
                    result.Append(token);
                }
                else if (DataSourceConstants.ColumnNames.IsFilterBagField(token))
                {
                    // The caller already addressed the bag with the dotted spelling the other providers
                    // use. This service separates nested fields with a slash, so it is written that way.
                    result.Append(token.Replace('.', '/'));
                }
                else if (string.Equals(token, DataSourceConstants.ColumnNames.Filters, StringComparison.OrdinalIgnoreCase) &&
                    i < filter.Length &&
                    filter[i] == '/')
                {
                    // The caller already addressed the bag. The field after the slash is copied as written,
                    // or it would be prefixed a second time.
                    result.Append(token);
                    result.Append('/');
                    i++;

                    var fieldStart = i;

                    while (i < filter.Length && (char.IsLetterOrDigit(filter[i]) || filter[i] == '_' || filter[i] == '.'))
                    {
                        i++;
                    }

                    result.Append(filter, fieldStart, i - fieldStart);
                }
                else
                {
                    // This is a field name - prefix with filters/.
                    result.Append($"{DataSourceConstants.ColumnNames.Filters}/{token}");
                }
            }
            else
            {
                result.Append(filter[i]);

                i++;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Determines whether the supplied field is read in the knowledge base's own scope, which is the only
    /// scope where the reserved names mean the typed columns.
    /// </summary>
    /// <param name="field">The field name as written in the filter.</param>
    /// <returns><see langword="true"/> when the reserved names apply.</returns>
    /// <remarks>
    /// A filter only reaches a translator as text, so the scope travels with the field: a caller whose own
    /// documents carry a field of the same name addresses it through the bag, and that is what says the
    /// knowledge column was not meant.
    /// </remarks>
    private static bool IsKnowledgeScope(string field)
    {
        return !DataSourceConstants.ColumnNames.IsFilterBagField(field);
    }

    private static bool IsODataKeyword(string token)
    {
        return token switch
        {
            "eq" or "ne" or "gt" or "lt" or "ge" or "le" or
            "and" or "or" or "not" or
            "contains" or "startswith" or "endswith" or
            "true" or "false" or "null" or
            "in" => true,
            _ => false,
        };
    }
}
