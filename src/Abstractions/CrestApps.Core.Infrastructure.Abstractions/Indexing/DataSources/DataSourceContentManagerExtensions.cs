using CrestApps.Core.Infrastructure.Indexing.Models;

namespace CrestApps.Core.Infrastructure.Indexing.DataSources;

/// <summary>
/// Extension methods for <see cref="IDataSourceContentManager"/> that let a caller read one search the same
/// way whatever the provider supports.
/// </summary>
public static class DataSourceContentManagerExtensions
{
    /// <summary>
    /// Searches the index and always reports whether the search ran.
    /// </summary>
    /// <param name="contentManager">The provider's vector search service.</param>
    /// <param name="indexProfile">The index profile describing the target index.</param>
    /// <param name="embedding">The embedding vector to search against.</param>
    /// <param name="dataSourceId">The identifier of the data source to search within.</param>
    /// <param name="topN">The maximum number of results to return.</param>
    /// <param name="filter">An optional filter expression to narrow the search.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the search, which is never <see langword="null"/>.</returns>
    /// <remarks>
    /// A provider that implements <see cref="IDataSourceContentManager.TrySearchAsync"/> answers for itself.
    /// One that does not is read through <see cref="IDataSourceContentManager.SearchAsync"/>, where throwing
    /// is a failure and returning rows is not. The single case that still cannot be told apart is a provider
    /// that swallows its own exceptions and hands back nothing — which is precisely what this contract exists
    /// to stop, and what such a provider fixes by implementing it.
    /// <para>
    /// This is the one place a search is read, so no caller needs a try/catch of its own around one. A
    /// cancelled search is not a failed one and is rethrown, because the caller is already going away.
    /// </para>
    /// </remarks>
    public static async Task<DataSourceSearchOutcome> SearchWithOutcomeAsync(
        this IDataSourceContentManager contentManager,
        IIndexProfileInfo indexProfile,
        float[] embedding,
        string dataSourceId,
        int topN,
        string filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentManager);

        var outcome = await contentManager.TrySearchAsync(indexProfile, embedding, dataSourceId, topN, filter, cancellationToken);

        if (outcome != null)
        {
            return outcome;
        }

        try
        {
            var results = await contentManager.SearchAsync(indexProfile, embedding, dataSourceId, topN, filter, cancellationToken);

            return DataSourceSearchOutcome.Success(results);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return DataSourceSearchOutcome.Failure();
        }
    }
}
