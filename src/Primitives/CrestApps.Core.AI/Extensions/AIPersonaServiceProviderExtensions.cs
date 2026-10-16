using CrestApps.Core.AI.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Profiles;

/// <summary>
/// Resolves personas through a service provider where the persona feature may not be registered. Without it, a
/// resource has no persona and a session speaks with its explicit or default voice.
/// </summary>
public static class AIPersonaServiceProviderExtensions
{
    /// <summary>
    /// Resolves the persona a resource links to, or returns <see langword="null"/> when the persona feature is not
    /// registered or the resource links to none.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="resource">The AI profile, chat interaction or other entity that may link to a persona.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static Task<ResolvedAIPersona> ResolvePersonaAsync(
        this IServiceProvider services,
        ExtensibleEntity resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var resolver = services.GetService<IAIPersonaResolver>();

        if (resolver is null || resource is null)
        {
            return Task.FromResult<ResolvedAIPersona>(null);
        }

        return resolver.ResolveAsync(resource, cancellationToken);
    }

    /// <summary>
    /// Chooses the voice for a session: the explicit voice when one is given, otherwise the persona's voice when the
    /// persona feature is registered and the voice fits the speaking deployment, otherwise the default voice.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="persona">The resolved persona, or <see langword="null"/> when there is none.</param>
    /// <param name="explicitVoice">A voice chosen on the profile, interaction, call or request, which always wins.</param>
    /// <param name="speakingDeploymentName">The name of the deployment that will speak.</param>
    /// <param name="defaultVoice">The voice to use when neither the explicit nor the persona voice applies.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static Task<string> SelectPersonaVoiceAsync(
        this IServiceProvider services,
        ResolvedAIPersona persona,
        string explicitVoice,
        string speakingDeploymentName,
        string defaultVoice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var resolver = services.GetService<IAIPersonaResolver>();

        if (resolver is null)
        {
            return Task.FromResult(!string.IsNullOrWhiteSpace(explicitVoice) ? explicitVoice : defaultVoice);
        }

        return resolver.SelectVoiceAsync(persona, explicitVoice, speakingDeploymentName, defaultVoice, cancellationToken);
    }
}
