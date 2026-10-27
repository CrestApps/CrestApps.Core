namespace CrestApps.Core.AI.Speech;

/// <summary>
/// Reacts when the caller's language is detected, so a host can switch the recognition language, the text-to-speech
/// voice and the reply language together.
/// </summary>
public interface IConversationLanguageHandler
{
    /// <summary>
    /// Called when a language other than the conversation's current one is detected and the profile allows it.
    /// </summary>
    /// <param name="context">The detection details. Set <see cref="DetectedLanguageContext.VoiceName"/> to change the voice.</param>
    Task LanguageDetectedAsync(DetectedLanguageContext context);
}
