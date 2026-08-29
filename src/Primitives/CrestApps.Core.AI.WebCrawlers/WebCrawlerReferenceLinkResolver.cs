using CrestApps.Core.AI.Profiles;

namespace CrestApps.Core.AI.WebCrawlers;

/// <summary>
/// Resolves citation links for <c>Web</c> data source references. The reference id is the scraped page's
/// own URL, so the link is the reference id itself when it is a valid absolute HTTP(S) URL.
/// </summary>
public sealed class WebCrawlerReferenceLinkResolver : IAIReferenceLinkResolver
{
    /// <summary>
    /// Resolves the citation link for a scraped page.
    /// </summary>
    /// <param name="referenceId">The reference id, which is the scraped page URL.</param>
    /// <param name="metadata">Optional reference metadata (unused).</param>
    /// <returns>The page URL when valid; otherwise, <see langword="null"/>.</returns>
    public string ResolveLink(string referenceId, IDictionary<string, object> metadata)
    {
        if (string.IsNullOrWhiteSpace(referenceId))
        {
            return null;
        }

        if (Uri.TryCreate(referenceId.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.ToString();
        }

        return null;
    }
}
