using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Memory;

/// <summary>
/// Searches the current user's durable memory entries for relevant context.
/// Hosts can provide their own backing implementation while reusing the shared
/// orchestration and tool behavior.
/// </summary>
public interface IAIMemorySearchService
{
    /// <summary>
    /// Searches the current user's durable memory entries for entries relevant to the provided queries.
    /// </summary>
    /// <param name="userId">The identifier of the user whose memories are searched.</param>
    /// <param name="queries">One or more query strings to match against memory content.</param>
    /// <param name="requestedTopN">The maximum number of results to return, or <see langword="null"/> for the provider default.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The matching memory search results ranked by relevance.</returns>
    Task<IEnumerable<AIMemorySearchResult>> SearchAsync(
        string userId,
        IEnumerable<string> queries,
        int? requestedTopN,
        CancellationToken cancellationToken = default);
}
