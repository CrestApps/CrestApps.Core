namespace CrestApps.Core.AI.WebCrawlers.Strategies;

/// <summary>
/// A page discovered by a crawl strategy, together with the change metadata used to decide whether the
/// page needs to be re-fetched.
/// </summary>
/// <param name="Url">The canonical, absolute URL of the page.</param>
/// <param name="LastModifiedUtc">The advertised last-modified timestamp, or <see langword="null"/>.</param>
/// <param name="ChangeFrequency">The advertised change-frequency hint, or <see langword="null"/>.</param>
public sealed record CrawledPageRef(
    string Url,
    DateTimeOffset? LastModifiedUtc = null,
    string ChangeFrequency = null);
