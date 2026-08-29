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
            .Where(result => result != null)
            .Where(result => !string.IsNullOrWhiteSpace(result.Content))
            .Where(result => minimumScore <= 0 || result.Score >= minimumScore)
            .Where(result => !ShouldExclude(result))
            .OrderByDescending(result => result.Score)
            .Take(topN)
            .ToList();
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
