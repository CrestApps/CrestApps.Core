namespace CrestApps.Core.AI.WebCrawlers.Strategies;

/// <summary>
/// Resolves a registered <see cref="IWebCrawlerStrategy"/> by its identifier.
/// </summary>
public interface IWebCrawlerStrategyResolver
{
    /// <summary>
    /// Gets the strategy registered under the specified identifier, or <see langword="null"/> when none is
    /// registered.
    /// </summary>
    /// <param name="strategy">The strategy identifier.</param>
    /// <returns>The strategy, or <see langword="null"/>.</returns>
    IWebCrawlerStrategy Get(string strategy);
}
