using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Speech;

/// <summary>
/// A speech provider's voices beyond the short list <see cref="ISpeechVoiceResolver"/> returns: searchable, and for a
/// provider that supports it, extended with voices cloned from recordings.
/// </summary>
public interface IVoiceLibrary
{
    /// <summary>
    /// Gets a value indicating whether <see cref="CloneAsync"/> and <see cref="DeleteAsync"/> work with this provider.
    /// </summary>
    bool SupportsCloning { get; }

    /// <summary>
    /// Searches the voices the connection can speak with.
    /// </summary>
    /// <param name="request">The search.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<VoiceSearchResult> SearchAsync(VoiceSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a voice from recordings and returns it, ready to be used as a deployment's or profile's voice.
    /// </summary>
    /// <param name="request">The voice to create.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="NotSupportedException">The provider cannot clone voices (see <see cref="SupportsCloning"/>).</exception>
    Task<SpeechVoice> CloneAsync(VoiceCloneRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a voice created with <see cref="CloneAsync"/>.
    /// </summary>
    /// <param name="voiceId">The voice's id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="NotSupportedException">The provider cannot clone voices (see <see cref="SupportsCloning"/>).</exception>
    Task DeleteAsync(string voiceId, CancellationToken cancellationToken = default);
}
