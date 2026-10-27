namespace CrestApps.Core.AI.Models;

/// <summary>
/// Settings stored on <see cref="AIProfile.Settings"/> that decide which languages a profile converses in.
/// </summary>
public sealed class AIProfileLanguageSettings
{
    /// <summary>
    /// Gets or sets the languages the profile may converse in, as BCP-47 tags. Empty means no restriction.
    /// </summary>
    public List<string> AllowedLanguages { get; set; } = [];

    /// <summary>
    /// Gets or sets the language a conversation starts in. The conversation language a host puts in the completion
    /// context's additional properties under <c>AICompletionContextKeys.ConversationLanguage</c> (for example from a
    /// conversation variable) overrides it.
    /// </summary>
    public string StartingLanguage { get; set; }
}
