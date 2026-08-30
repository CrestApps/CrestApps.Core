using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.WebCrawlers.Strategies;

/// <summary>
/// The default <see cref="IWebCrawlerStrategyResolver"/>. It resolves strategies from the keyed service
/// registrations by their identifier.
/// </summary>
public sealed class WebCrawlerStrategyResolver : IWebCrawlerStrategyResolver
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebCrawlerStrategyResolver"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    public WebCrawlerStrategyResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public IWebCrawlerStrategy Get(string strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy))
        {
            return null;
        }

        return _serviceProvider.GetKeyedService<IWebCrawlerStrategy>(strategy);
    }
}
