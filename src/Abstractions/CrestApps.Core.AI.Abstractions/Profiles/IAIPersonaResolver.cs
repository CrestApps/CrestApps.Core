using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Profiles;

/// <summary>
/// The single place that turns a persona link into a resolved persona and decides which voice a session
/// speaks with. Every consumer goes through it so a persona behaves the same in chat, chat interactions,
/// realtime sessions and voice calls. Listing and editing personas goes through <see cref="IAIPersonaManager"/>.
/// It is registered only when the host adds the persona feature, so a consumer that resolves it optionally
/// treats its absence as "no persona".
/// </summary>
public interface IAIPersonaResolver
{
    /// <summary>
    /// Resolves the persona linked to a resource through its <see cref="AIPersonaMetadata"/>.
    /// </summary>
    /// <param name="resource">The AI profile, chat interaction or other entity that may link to a persona.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The resolved persona, or <see langword="null"/> when the resource links to none or the linked persona no
    /// longer exists. A missing persona is logged and never throws, so a deleted persona does not break chats.
    /// </returns>
    Task<ResolvedAIPersona> ResolveAsync(ExtensibleEntity resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a persona by its technical name and renders its instructions.
    /// </summary>
    /// <param name="personaId">The technical name of the persona.</param>
    /// <param name="parameters">The Liquid arguments used to render the instructions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved persona, or <see langword="null"/> when no persona has that name.</returns>
    Task<ResolvedAIPersona> ResolveAsync(string personaId, IDictionary<string, object> parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chooses the voice for a session: the explicit voice when one is given, otherwise the persona's voice when
    /// the speaking deployment belongs to the same provider it was chosen from, otherwise the default voice.
    /// </summary>
    /// <param name="persona">The resolved persona, or <see langword="null"/> when there is none.</param>
    /// <param name="explicitVoice">A voice chosen on the profile, interaction, call or request, which always wins.</param>
    /// <param name="speakingDeploymentName">The name of the deployment that will speak, used to check the persona voice belongs to its provider.</param>
    /// <param name="defaultVoice">The voice to use when neither the explicit nor the persona voice applies.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<string> SelectVoiceAsync(
        ResolvedAIPersona persona,
        string explicitVoice,
        string speakingDeploymentName,
        string defaultVoice,
        CancellationToken cancellationToken = default);
}
