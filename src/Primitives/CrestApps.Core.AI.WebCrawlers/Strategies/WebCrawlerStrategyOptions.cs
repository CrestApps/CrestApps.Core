using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.WebCrawlers.Strategies;

/// <summary>
/// Holds the registered set of web-crawl strategy descriptors, used to populate the strategy dropdown in
/// the crawler editor.
/// </summary>
public sealed class WebCrawlerStrategyOptions
{
    /// <summary>
    /// Gets the configured strategy descriptors.
    /// </summary>
    public List<WebCrawlerStrategyDescriptor> Strategies { get; } = [];

    /// <summary>
    /// Adds or updates a strategy descriptor.
    /// </summary>
    /// <param name="strategy">The strategy identifier.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="description">The description.</param>
    public void AddOrUpdate(string strategy, LocalizedString displayName, LocalizedString description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategy);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(description.Value);

        var descriptor = Strategies.FirstOrDefault(item =>
            string.Equals(item.Strategy, strategy, StringComparison.OrdinalIgnoreCase));

        descriptor ??= new WebCrawlerStrategyDescriptor();
        descriptor.Strategy = strategy;
        descriptor.DisplayName = displayName;
        descriptor.Description = description;

        if (!Strategies.Contains(descriptor))
        {
            Strategies.Add(descriptor);
        }
    }
}
