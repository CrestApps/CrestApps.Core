using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Narrows a search to particular kinds of knowledge.
/// </summary>
/// <remarks>
/// Two paths reach a knowledge base with a restriction on what kind of thing to return: a tool instance
/// pinned to figures, and a data source attached straight to a profile. They ask in different ways and run at
/// different points, but "only these kinds" is one question and gets one answer here.
/// <para>
/// The column keeps the name <c>contentType</c>, which is what is written into every indexed row. The concept
/// is <see cref="KnowledgeObjectTypes"/>; only the stored column name differs, and it is pinned rather than
/// renamed because it is read back verbatim from rows that already exist.
/// </para>
/// </remarks>
internal static class KnowledgeObjectTypeFilter
{
    /// <summary>
    /// Builds the OData clause that keeps only the supplied kinds.
    /// </summary>
    /// <param name="objectTypes">The kinds to keep. Empty means every kind, and produces no clause.</param>
    /// <returns>The clause, or <see langword="null"/> when nothing is to be narrowed.</returns>
    /// <remarks>
    /// Asking for text also asks for rows with no type at all. Every row written before typed knowledge
    /// existed has none, and every reader treats those as text; a clause that omitted them would make
    /// "only text" return nothing at all on exactly the data that is all text.
    /// </remarks>
    public static string BuildClause(IReadOnlyList<string> objectTypes)
    {
        if (objectTypes is not { Count: > 0 })
        {
            return null;
        }

        var clauses = objectTypes
            .Where(objectType => !string.IsNullOrWhiteSpace(objectType))
            .Select(objectType => $"{DataSourceConstants.ColumnNames.ContentType} eq '{objectType.Replace("'", "''", StringComparison.Ordinal)}'")
            .ToList();

        if (clauses.Count == 0)
        {
            return null;
        }

        if (objectTypes.Any(objectType => string.Equals(objectType, KnowledgeObjectTypes.Text, StringComparison.OrdinalIgnoreCase)))
        {
            clauses.Add($"{DataSourceConstants.ColumnNames.ContentType} eq null");
        }

        return clauses.Count == 1 ? clauses[0] : $"({string.Join(" or ", clauses)})";
    }

    /// <summary>
    /// Joins the caller's filter and the kind clause into the one filter to translate.
    /// </summary>
    /// <param name="filter">The caller's OData filter, when it has one.</param>
    /// <param name="clause">The kind clause, when there is one.</param>
    /// <returns>The filter to translate, or <see langword="null"/> when there is nothing to filter on.</returns>
    public static string Combine(string filter, string clause)
    {
        if (string.IsNullOrWhiteSpace(clause))
        {
            return filter;
        }

        return string.IsNullOrWhiteSpace(filter) ? clause : $"({filter}) and {clause}";
    }

    /// <summary>
    /// Determines whether a restriction admits a given kind.
    /// </summary>
    /// <param name="objectTypes">The kinds the caller restricted to. Empty admits everything.</param>
    /// <param name="objectType">The kind being asked about.</param>
    /// <returns><see langword="true"/> when the kind may be returned.</returns>
    /// <remarks>
    /// Anything that fetches a kind of row on its own, outside the filtered search, has to ask this first. A
    /// restriction expressed as a filter only binds the search it was passed to, so a second search that
    /// never saw it will happily return what the operator excluded.
    /// </remarks>
    public static bool Admits(IReadOnlyList<string> objectTypes, string objectType)
    {
        if (objectTypes is not { Count: > 0 })
        {
            return true;
        }

        return objectTypes.Any(candidate => string.Equals(candidate, objectType, StringComparison.OrdinalIgnoreCase));
    }
}
