using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Speech;

/// <summary>
/// Resolves and tracks the language of a conversation under a profile's <see cref="AIProfileLanguageSettings"/>.
/// </summary>
public interface IConversationLanguageService
{
    /// <summary>
    /// Gets the language a conversation starts in: <paramref name="conversationLanguage"/> when the profile allows it,
    /// otherwise the profile's starting language, otherwise <see langword="null"/>.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="conversationLanguage">The value of the conversation language variable, if any.</param>
    string ResolveStartingLanguage(AIProfile profile, string conversationLanguage = null);

    /// <summary>
    /// Reports a detected caller language. When the profile allows it and it differs from
    /// <paramref name="currentLanguage"/>, every <see cref="IConversationLanguageHandler"/> is notified.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="sessionId">The session id, if any.</param>
    /// <param name="currentLanguage">The language the conversation is currently in.</param>
    /// <param name="detectedLanguage">The detected language.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The context handlers saw, or <see langword="null"/> when the language was not switched.</returns>
    Task<DetectedLanguageContext> ReportDetectedLanguageAsync(
        AIProfile profile,
        string sessionId,
        string currentLanguage,
        string detectedLanguage,
        CancellationToken cancellationToken = default);
}
