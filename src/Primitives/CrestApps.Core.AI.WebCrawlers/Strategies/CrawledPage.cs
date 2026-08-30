namespace CrestApps.Core.AI.WebCrawlers.Strategies;

/// <summary>
/// The cleaned title and plain-text body of a fetched page.
/// </summary>
/// <param name="Title">The page title, or <see langword="null"/>.</param>
/// <param name="Content">The plain-text body with markup removed.</param>
public sealed record CrawledPage(string Title, string Content);
