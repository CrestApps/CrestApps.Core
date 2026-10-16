using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Profiles;

/// <summary>
/// The single place that turns a persona link into a resolved persona and decides which voice a session
/// speaks with. Every consumer goes through it so a persona behaves the same in chat, chat interactions,
/// realtime sessions and voice calls.
/// </summary>
public interface IAIPersonaResolver
{
    /// <summary>
    /// Lists every persona template, saved and file-based. A saved template replaces a file-based one with
    /// the same name.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask<IEnumerable<AIProfileTemplate>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a persona template by its technical name.
    /// </summary>
    /// <param name="personaId">The technical name of the persona template.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The template, or <see langword="null"/> when no persona has that name.</returns>
    ValueTask<AIProfileTemplate> FindAsync(string personaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the persona linked to a resource through its <see cref="AIPersonaMetadata"/>.
    /// </summary>
    /// <param name="resource">The AI profile, chat interaction or other entity that may link to a persona.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The resolved persona, or <see langword="null"/> when the resource links to none or the linked persona no
    /// longer exists. A missing persona is logged and never throws, so a deleted persona does not break chats.
    /// </returns>
    Task<AIPersona> ResolveAsync(ExtensibleEntity resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a persona by its technical name and renders its instructions.
    /// </summary>
    /// <param name="personaId">The technical name of the persona template.</param>
    /// <param name="parameters">The Liquid arguments used to render the instructions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved persona, or <see langword="null"/> when no persona has that name.</returns>
    Task<AIPersona> ResolveAsync(string personaId, IDictionary<string, object> parameters = null, CancellationToken cancellationToken = default);

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
        AIPersona persona,
        string explicitVoice,
        string speakingDeploymentName,
        string defaultVoice,
        CancellationToken cancellationToken = default);
}
