using CrestApps.Core.AI.Tooling.Instances;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI;

/// <summary>
/// Service-collection extensions for registering the built-in HTTP API request tool instance definition.
/// </summary>
public static class HttpApiRequestToolServiceCollectionExtensions
{
    /// <summary>
    /// Registers the built-in HTTP API request tool instance definition and its named HTTP client. After
    /// calling this, users can create one or more configured instances that call external HTTP APIs and
    /// attach them to AI profiles.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddApiRequestToolInstance(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(HttpApiRequestToolConstants.HttpClientName);

        services.AddAIToolInstanceDefinition<HttpApiRequestToolDefinition>(HttpApiRequestToolConstants.DefinitionName)
            .WithDisplayName(new LocalizedString("HTTP API Request", "HTTP API Request"))
            .WithDescription(new LocalizedString(
                "HTTP API Request Description",
                "Call an external HTTP API with a preconfigured endpoint, method, authentication, and headers. The AI model only supplies the open arguments you allow (path, query, body)."))
            .WithCategory("Integrations");

        return services;
    }
}
