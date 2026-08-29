using System.Text.RegularExpressions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Crawling;

namespace CrestApps.Core.AI.WebCrawlers.Strategies.Sitemap;

/// <summary>
/// Resolves the effective sitemap-strategy configuration for a crawler by merging its
/// <see cref="SitemapWebCrawlerMetadata"/> with the global <see cref="WebCrawlerOptions"/>, and builds the
/// derived crawl request and URL filter.
/// </summary>
internal static class SitemapWebCrawlerHelper
{
    /// <summary>
    /// Attempts to read the sitemap metadata from the crawler.
    /// </summary>
    /// <param name="crawler">The crawler.</param>
    /// <param name="metadata">The resolved metadata when present.</param>
    /// <returns><see langword="true"/> when metadata is present and specifies a site; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetMetadata(WebCrawler crawler, out SitemapWebCrawlerMetadata metadata)
    {
        if (crawler.TryGet(out metadata) &&
            (!string.IsNullOrWhiteSpace(metadata.BaseUrl) || !string.IsNullOrWhiteSpace(metadata.SitemapUrl)))
        {
            return true;
        }

        metadata = null;

        return false;
    }

    /// <summary>
    /// Builds the sitemap discovery request for the resolved configuration.
    /// </summary>
    /// <param name="metadata">The crawler metadata.</param>
    /// <param name="options">The global options.</param>
    /// <returns>The crawl request.</returns>
    public static SitemapCrawlRequest CreateCrawlRequest(SitemapWebCrawlerMetadata metadata, WebCrawlerOptions options)
    {
        return new SitemapCrawlRequest
        {
            BaseUrl = metadata.BaseUrl,
            SitemapUrl = metadata.SitemapUrl,
            MaxPages = Math.Max(1, metadata.MaxPages ?? options.DefaultMaxPages),
        };
    }

    /// <summary>
    /// Resolves the effective per-request fetch timeout.
    /// </summary>
    /// <param name="metadata">The crawler metadata.</param>
    /// <param name="options">The global options.</param>
    /// <returns>The effective timeout.</returns>
    public static TimeSpan ResolveRequestTimeout(SitemapWebCrawlerMetadata metadata, WebCrawlerOptions options)
    {
        var seconds = Math.Max(1, metadata.RequestTimeoutSeconds ?? options.DefaultRequestTimeoutSeconds);

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Resolves the effective <c>User-Agent</c> header.
    /// </summary>
    /// <param name="metadata">The crawler metadata.</param>
    /// <param name="options">The global options.</param>
    /// <returns>The effective user agent.</returns>
    public static string ResolveUserAgent(SitemapWebCrawlerMetadata metadata, WebCrawlerOptions options)
    {
        return string.IsNullOrWhiteSpace(metadata.UserAgent) ? options.DefaultUserAgent : metadata.UserAgent;
    }

    /// <summary>
    /// Compiles the include/exclude URL patterns into a predicate. Invalid patterns are skipped. When no
    /// include patterns are configured, every URL is eligible; exclude patterns are always applied.
    /// </summary>
    /// <param name="metadata">The crawler metadata.</param>
    /// <returns>A predicate returning <see langword="true"/> when a URL should be scraped.</returns>
    public static Func<string, bool> CreateUrlFilter(SitemapWebCrawlerMetadata metadata)
    {
        var includes = CompilePatterns(metadata.IncludeUrlPatterns);
        var excludes = CompilePatterns(metadata.ExcludeUrlPatterns);

        if (includes.Count == 0 && excludes.Count == 0)
        {
            return static _ => true;
        }

        return url =>
        {
            if (includes.Count > 0 && !includes.Any(pattern => pattern.IsMatch(url)))
            {
                return false;
            }

            return !excludes.Any(pattern => pattern.IsMatch(url));
        };
    }

    private static List<Regex> CompilePatterns(IEnumerable<string> patterns)
    {
        var compiled = new List<Regex>();

        if (patterns is null)
        {
            return compiled;
        }

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            try
            {
                compiled.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }
            catch (ArgumentException)
            {
                // A malformed pattern is ignored rather than failing the whole crawl.
            }
        }

        return compiled;
    }
}
