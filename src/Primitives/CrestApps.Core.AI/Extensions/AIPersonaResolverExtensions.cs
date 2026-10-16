using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Profiles;

/// <summary>
/// Provides extension methods for <see cref="IAIPersonaResolver"/>.
/// </summary>
public static class AIPersonaResolverExtensions
{
    /// <summary>
    /// Resolves the persona a profile or chat interaction links to and maps it to what a chat surface shows.
    /// </summary>
    /// <param name="resolver">The persona resolver.</param>
    /// <param name="resource">The AI profile, chat interaction or other entity that may link to a persona.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The display values; <see cref="AIChatAssistantDisplay.Default"/> when no persona applies.</returns>
    public static async Task<AIChatAssistantDisplay> ResolveChatAssistantDisplayAsync(
        this IAIPersonaResolver resolver,
        ExtensibleEntity resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        if (resource is null)
        {
            return AIChatAssistantDisplay.Default;
        }

        var persona = await resolver.ResolveAsync(resource, cancellationToken);

        return persona.ToChatAssistantDisplay();
    }

    /// <summary>
    /// Resolves the persona a profile or chat interaction links to and maps it to what a chat surface shows. Personas
    /// are an opt-in feature, so when the host did not add it the surface keeps its default look.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="resource">The AI profile, chat interaction or other entity that may link to a persona.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The display values; <see cref="AIChatAssistantDisplay.Default"/> when no persona applies.</returns>
    public static async Task<AIChatAssistantDisplay> ResolveChatAssistantDisplayAsync(
        this IServiceProvider services,
        ExtensibleEntity resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var persona = await services.ResolvePersonaAsync(resource, cancellationToken);

        return persona.ToChatAssistantDisplay();
    }
}
