using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Azure.AISearch.Builders;

/// <summary>
/// Represents the crest Apps Azure AI Search Builder.
/// </summary>
public sealed class CrestAppsAzureAISearchBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsAzureAISearchBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsAzureAISearchBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the services.
    /// </summary>
    public IServiceCollection Services { get; }
}
