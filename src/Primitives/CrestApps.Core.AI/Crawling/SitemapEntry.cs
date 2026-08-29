namespace CrestApps.Core.AI.Crawling;

/// <summary>
/// Represents a single page discovered while walking a site's sitemap graph, together with the change
/// metadata a sitemap can advertise. The change metadata (<see cref="LastModifiedUtc"/> and
/// <see cref="ChangeFrequency"/>) lets a re-indexing process decide which pages actually need to be
/// fetched again instead of re-crawling the whole site.
/// </summary>
/// <param name="Url">The canonical, absolute URL of the page.</param>
/// <param name="LastModifiedUtc">
/// The UTC timestamp advertised by the sitemap's <c>&lt;lastmod&gt;</c> element (or an equivalent feed
/// field), or <see langword="null"/> when the sitemap does not report one.
/// </param>
/// <param name="ChangeFrequency">
/// The advertised <c>&lt;changefreq&gt;</c> hint (for example <c>daily</c> or <c>weekly</c>), or
/// <see langword="null"/> when the sitemap does not report one.
/// </param>
/// <param name="Priority">
/// The advertised <c>&lt;priority&gt;</c> value between 0.0 and 1.0, or <see langword="null"/> when the
/// sitemap does not report one.
/// </param>
public sealed record SitemapEntry(
    string Url,
    DateTimeOffset? LastModifiedUtc = null,
    string ChangeFrequency = null,
    double? Priority = null);
