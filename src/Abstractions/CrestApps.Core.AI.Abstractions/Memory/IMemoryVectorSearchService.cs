using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing.Models;

namespace CrestApps.Core.AI.Memory;

/// <summary>
/// Performs vector similarity search over the memory knowledge-base index for a specific user.
/// </summary>
public interface IMemoryVectorSearchService
{
    /// <summary>
    /// Searches the specified index for the nearest memory entries to the given embedding vector.
    /// </summary>
    /// <param name="indexProfile">The search index profile that identifies the backing vector store.</param>
    /// <param name="embedding">The query embedding vector to compare against stored entries.</param>
    /// <param name="userId">The identifier of the user whose memories are searched.</param>
    /// <param name="topN">The maximum number of results to return.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The top-N memory search results ranked by similarity.</returns>
    Task<IEnumerable<AIMemorySearchResult>> SearchAsync(
        SearchIndexProfile indexProfile,
        float[] embedding,
        string userId,
        int topN,
        CancellationToken cancellationToken = default);
}
