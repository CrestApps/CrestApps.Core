namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// Runtime limits that control how a built-in documentation search source crawls and ranks a single
/// documentation site. A fresh instance with sensible defaults is created for each configured
/// documentation search tool instance.
/// </summary>
public sealed class DocumentationSearchOptions
{
    /// <summary>
    /// Gets or sets the default maximum number of results a single site contributes to a search.
    /// </summary>
    public int MaxResultsPerSite { get; set; } = 5;

    /// <summary>
    /// Gets or sets the default maximum number of pages the crawler indexes per site.
    /// </summary>
    public int MaxPagesPerSite { get; set; } = 200;

    /// <summary>
    /// Gets or sets the maximum number of concurrent page requests the crawler issues per site.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 4;

    /// <summary>
    /// Gets or sets how long a crawled site corpus is cached before it is refreshed.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the longest a single search waits for a not-yet-built corpus before it reports the
    /// index as still pending. The corpus keeps building in the background, so a subsequent search returns
    /// results from the warmed cache. This keeps a slow first crawl from blocking the caller — and, in a
    /// tool-calling loop, from being retried until the model exhausts its iteration budget. A site whose
    /// crawl finishes within this budget still returns results on the very first search.
    /// </summary>
    public TimeSpan FirstSearchWaitBudget { get; set; } = TimeSpan.FromSeconds(8);
}
