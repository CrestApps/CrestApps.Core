using CrestApps.Core.AI.Tooling.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI;

/// <summary>
/// Service-collection extensions for registering the built-in HTTP API request tool source.
/// </summary>
public static class HttpApiRequestToolServiceCollectionExtensions
{
    /// <summary>
    /// Registers the built-in HTTP API request <see cref="Tooling.AIToolSource"/> and its named HTTP
    /// client. After calling this, users can create one or more configured definitions that call
    /// external HTTP APIs and attach them to AI profiles.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddApiRequestToolSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(HttpApiRequestToolConstants.HttpClientName);

        services.AddAIToolSource<HttpApiRequestToolSource>();

        return services;
    }
}
