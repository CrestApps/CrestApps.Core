using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Tooling.Instances;

/// <summary>
/// Convenience registration for the built-in <see cref="HttpApiRequestToolInstanceSource"/>, a generic
/// "call any HTTP API" tool that users configure per instance (endpoint, HTTP method, authentication, and
/// static headers). The AI model only supplies the open arguments the instance's settings allow.
/// </summary>
public static class HttpApiRequestToolServiceCollectionExtensions
{
    /// <summary>
    /// Registers the named <see cref="System.Net.Http.HttpClient"/> and the built-in HTTP API request
    /// source on the tool instances builder so users can create configured instances from it.
    /// </summary>
    /// <param name="builder">The tool instances builder.</param>
    /// <param name="configure">
    /// An optional delegate used to override the source display metadata (display name, description,
    /// category). Sensible defaults are applied when not overridden.
    /// </param>
    /// <returns>The tool instances builder, for chaining.</returns>
    public static CrestAppsAIToolInstancesBuilder AddHttpApiRequestSource(
        this CrestAppsAIToolInstancesBuilder builder,
        Action<AIToolInstanceSourceEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHttpClient(HttpApiRequestToolConstants.HttpClientName);

        builder.AddSource<HttpApiRequestToolInstanceSource>(HttpApiRequestToolConstants.SourceName, entry =>
        {
            entry.DisplayName = new LocalizedString(HttpApiRequestToolConstants.SourceName, "HTTP API Request");
            entry.Description = new LocalizedString(
                HttpApiRequestToolConstants.SourceName,
                "Calls an external HTTP API using preconfigured settings (endpoint, authentication, headers).");
            entry.Category = new LocalizedString("Integrations", "Integrations");

            // Advertise the placements this source knows how to honor, which is what enables the
            // parameter editor for instances built from it.
            entry.Parameters = HttpApiRequestParameterBindings.CreateCapabilities();

            configure?.Invoke(entry);
        });

        return builder;
    }
}
