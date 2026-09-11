using System.IO;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Filters and trims data-source search results before they are injected into prompts or returned by tools.
/// </summary>
internal static class DataSourceSearchResultSelector
{
    private static readonly HashSet<string> NonPageExtensions =
    [
        ".atom",
        ".csv",
        ".gz",
        ".ics",
        ".json",
        ".kml",
        ".pdf",
        ".rss",
        ".txt",
        ".xml",
        ".zip",
    ];

    /// <summary>
    /// The damping constant used by Reciprocal Rank Fusion. 60 is the value from the original paper and the
    /// one every mainstream implementation uses.
    /// </summary>
    private const int RankFusionConstant = 60;

    /// <summary>
    /// Gets the candidate count to request from the vector store so result filtering has enough headroom.
    /// </summary>
    /// <param name="topN">The final result count requested by the caller.</param>
    /// <returns>The expanded candidate count.</returns>
    public static int GetCandidateCount(int topN)
    {
        if (topN <= 0)
        {
            return 0;
        }

        return Math.Min(
            AIDataSourceOptions.MaxTopNDocuments,
            Math.Max(topN, topN * 3));
    }

    /// <summary>
    /// Selects the highest-quality results after applying the minimum score and web-page quality filters.
    /// </summary>
    /// <param name="results">The raw search results.</param>
    /// <param name="topN">The maximum number of results to return.</param>
    /// <param name="minimumScore">The minimum score threshold.</param>
    /// <returns>The filtered and trimmed results.</returns>
    public static IReadOnlyList<DataSourceSearchResult> SelectTopResults(
        IEnumerable<DataSourceSearchResult> results,
        int topN,
        float minimumScore)
    {
        if (results == null || topN <= 0)
        {
            return [];
        }

        return results
            .Where(result => IsAcceptable(result, minimumScore))
            .OrderByDescending(result => result.Score)
            .Take(topN)
            .ToList();
    }

    /// <summary>
    /// Fuses the result sets of several queries into one ranking with Reciprocal Rank Fusion, keeping each
    /// matching chunk once.
    /// </summary>
    /// <remarks>
    /// Similarity scores are not comparable across query vectors: a broad phrase whose best match scores 0.6
    /// and a narrow phrase whose best match scores 0.9 are both returning their own best answer. Ranking the
    /// union by raw score therefore lets whichever phrase happens to produce higher similarities crowd the
    /// others out. RRF ranks by each result's <em>position</em> within its own query's list instead, so a
    /// result that placed first for any phrase competes on equal footing. Strictness still applies as an
    /// absolute quality floor before fusion, using each chunk's best raw score across the phrases that
    /// returned it — it answers "was this ever a good match?", while RRF decides the order among survivors.
    /// With a single query set the fused order is the score order, so one phrase behaves exactly as before.
    /// </remarks>
    /// <param name="resultSets">The per-query result sets, one per searched phrase.</param>
    /// <param name="topN">The maximum number of results to return.</param>
    /// <param name="minimumScore">The minimum score threshold.</param>
    /// <returns>The fused, filtered, and trimmed results.</returns>
    public static IReadOnlyList<DataSourceSearchResult> FuseTopResults(
        IEnumerable<IEnumerable<DataSourceSearchResult>> resultSets,
        int topN,
        float minimumScore)
    {
        if (resultSets == null || topN <= 0)
        {
            return [];
        }

        var fused = new Dictionary<string, (DataSourceSearchResult Best, double Score)>(StringComparer.Ordinal);

        foreach (var resultSet in resultSets)
        {
            if (resultSet == null)
            {
                continue;
            }

            var ranked = resultSet
                .Where(result => IsAcceptable(result, minimumScore))
                .OrderByDescending(result => result.Score)
                .ToList();

            for (var index = 0; index < ranked.Count; index++)
            {
                var result = ranked[index];
                var key = GetChunkKey(result);

                // The standard RRF damping constant. It keeps a result that placed first for one phrase from
                // dominating one that placed second for several.
                var contribution = 1d / (RankFusionConstant + index + 1);

                if (fused.TryGetValue(key, out var existing))
                {
                    fused[key] = (existing.Best.Score >= result.Score ? existing.Best : result, existing.Score + contribution);
                }
                else
                {
                    fused[key] = (result, contribution);
                }
            }
        }

        return fused.Values
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.Best.Score)
            .Take(topN)
            .Select(entry => entry.Best)
            .ToList();
    }

    /// <summary>
    /// Builds the identity used to recognize the same chunk returned by more than one query.
    /// </summary>
    /// <param name="result">The search result.</param>
    /// <returns>The chunk identity.</returns>
    private static string GetChunkKey(DataSourceSearchResult result)
    {
        // A result with no reference id cannot be identified by document, so its content stands in. That is
        // what a caller would compare by eye, and it still collapses the same passage returned twice.
        return string.IsNullOrEmpty(result.ReferenceId)
            ? $"content:{result.Content}"
            : $"ref:{result.ReferenceId}:{result.ChunkIndex}";
    }

    private static bool IsAcceptable(DataSourceSearchResult result, float minimumScore)
    {
        return result != null &&
            !string.IsNullOrWhiteSpace(result.Content) &&
            (minimumScore <= 0 || result.Score >= minimumScore) &&
            !ShouldExclude(result);
    }

    private static bool ShouldExclude(DataSourceSearchResult result)
    {
        if (!string.Equals(result.ReferenceType, AIDataSourceSourceTypes.Web, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (LooksLikeNonPageAsset(result.ReferenceId))
        {
            return true;
        }

        return LooksLikeErrorPage(result.Title, result.Content);
    }

    private static bool LooksLikeNonPageAsset(string referenceId)
    {
        if (string.IsNullOrWhiteSpace(referenceId))
        {
            return false;
        }

        var path = referenceId;

        if (Uri.TryCreate(referenceId, UriKind.Absolute, out var uri))
        {
            path = uri.AbsolutePath;
        }
        else
        {
            var fragmentIndex = path.IndexOf('#');

            if (fragmentIndex >= 0)
            {
                path = path[..fragmentIndex];
            }

            var queryIndex = path.IndexOf('?');

            if (queryIndex >= 0)
            {
                path = path[..queryIndex];
            }
        }

        var extension = Path.GetExtension(path);

        return !string.IsNullOrEmpty(extension) &&
            NonPageExtensions.Contains(extension);
    }

    private static bool LooksLikeErrorPage(string title, string content)
    {
        if (!string.IsNullOrWhiteSpace(title) &&
            (title.StartsWith("404", StringComparison.OrdinalIgnoreCase) ||
             title.Contains("Page Not Found", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(content) &&
            content.Contains("404 - Page Not Found", StringComparison.OrdinalIgnoreCase);
    }
}
