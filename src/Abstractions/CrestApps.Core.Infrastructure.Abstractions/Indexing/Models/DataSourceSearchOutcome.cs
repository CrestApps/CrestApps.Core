namespace CrestApps.Core.Infrastructure.Indexing.Models;

/// <summary>
/// What one search of a data source index did: the rows it matched, and whether it ran at all.
/// </summary>
/// <remarks>
/// A search that could not run and a search that matched nothing both hand back no rows, and for a long time
/// that emptiness was the only thing a caller could read. The two mean opposite things to whoever answers
/// with them: "this data source holds nothing on the subject" is a fact worth stating, while "the index could
/// not be reached" is a fact about the deployment that must never be reported as the first one.
/// </remarks>
public sealed class DataSourceSearchOutcome
{
    private static readonly DataSourceSearchOutcome _failure = new()
    {
        Succeeded = false,
    };

    /// <summary>
    /// Gets a value indicating whether the search ran. A search that did not run carries no results, and its
    /// emptiness says nothing at all about what the index holds.
    /// </summary>
    public bool Succeeded { get; private init; } = true;

    /// <summary>
    /// Gets the rows the search matched, which is empty when it matched nothing and when it did not run.
    /// </summary>
    public IEnumerable<DataSourceSearchResult> Results { get; private init; } = [];

    /// <summary>
    /// Creates the outcome of a search that ran.
    /// </summary>
    /// <param name="results">The rows the search matched, which may be none.</param>
    /// <returns>The outcome.</returns>
    public static DataSourceSearchOutcome Success(IEnumerable<DataSourceSearchResult> results)
    {
        return new DataSourceSearchOutcome
        {
            Results = results ?? [],
        };
    }

    /// <summary>
    /// Creates the outcome of a search that could not run: an unreachable index, a refused credential, a
    /// query the provider rejected.
    /// </summary>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// Why it failed is deliberately not carried here. The provider has already logged the failure with its
    /// own detail, and text built out of a provider exception — host names, connection strings, credentials —
    /// is exactly what must not end up in a model's context.
    /// </remarks>
    public static DataSourceSearchOutcome Failure()
    {
        return _failure;
    }
}
