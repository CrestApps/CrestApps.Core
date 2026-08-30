namespace CrestApps.Core.AI.WebCrawlers;

/// <summary>
/// Well-known identifiers for the web-crawler feature.
/// </summary>
public static class WebCrawlerConstants
{
    /// <summary>
    /// The name of the named <see cref="System.Net.Http.HttpClient"/> used to crawl and fetch pages.
    /// </summary>
    public const string HttpClientName = "CrestApps.WebCrawler";

    /// <summary>
    /// The knowledge-base filter field that stores the scraped page URL, used to build citations.
    /// </summary>
    public const string UrlFieldName = "url";

    /// <summary>
    /// The knowledge-base filter field that stores the scraped page host.
    /// </summary>
    public const string HostFieldName = "host";

    /// <summary>
    /// Well-known crawl strategy identifiers.
    /// </summary>
    public static class Strategies
    {
        /// <summary>
        /// Discovers pages through a site's sitemap(s).
        /// </summary>
        public const string Sitemap = "Sitemap";
    }
}
