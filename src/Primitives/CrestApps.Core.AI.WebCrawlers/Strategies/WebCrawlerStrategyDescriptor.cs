using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.WebCrawlers.Strategies;

/// <summary>
/// Describes a selectable web-crawl strategy shown to operators when they create a crawler.
/// </summary>
public sealed class WebCrawlerStrategyDescriptor
{
    /// <summary>
    /// Gets or sets the technical strategy identifier.
    /// </summary>
    public string Strategy { get; set; }

    /// <summary>
    /// Gets or sets the display name shown to operators.
    /// </summary>
    public LocalizedString DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the descriptive text shown to operators.
    /// </summary>
    public LocalizedString Description { get; set; }
}
