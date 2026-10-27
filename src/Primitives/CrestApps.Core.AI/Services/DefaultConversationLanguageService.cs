using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default <see cref="IConversationLanguageService"/>. Matches languages by BCP-47 tag, treating a regional tag
/// (<c>es-MX</c>) and its neutral language (<c>es</c>) as the same language.
/// </summary>
public sealed class DefaultConversationLanguageService : IConversationLanguageService
{
    private readonly IEnumerable<IConversationLanguageHandler> _handlers;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultConversationLanguageService"/> class.
    /// </summary>
    /// <param name="handlers">The handlers notified when the language switches.</param>
    public DefaultConversationLanguageService(IEnumerable<IConversationLanguageHandler> handlers)
    {
        _handlers = handlers;
    }

    /// <inheritdoc />
    public string ResolveStartingLanguage(AIProfile profile, string conversationLanguage = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        profile.TryGetSettings<AIProfileLanguageSettings>(out var settings);

        if (!string.IsNullOrWhiteSpace(conversationLanguage))
        {
            var allowed = Match(settings, conversationLanguage);

            if (allowed is not null)
            {
                return allowed;
            }
        }

        return string.IsNullOrWhiteSpace(settings?.StartingLanguage) ? null : settings.StartingLanguage.Trim();
    }

    /// <inheritdoc />
    public async Task<DetectedLanguageContext> ReportDetectedLanguageAsync(
        AIProfile profile,
        string sessionId,
        string currentLanguage,
        string detectedLanguage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(detectedLanguage))
        {
            return null;
        }

        profile.TryGetSettings<AIProfileLanguageSettings>(out var settings);

        var language = Match(settings, detectedLanguage);

        if (language is null || SameLanguage(language, currentLanguage))
        {
            return null;
        }

        var context = new DetectedLanguageContext
        {
            Profile = profile,
            SessionId = sessionId,
            PreviousLanguage = currentLanguage,
            Language = language,
        };

        foreach (var handler in _handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await handler.LanguageDetectedAsync(context);
        }

        return context;
    }

    /// <summary>
    /// Finds the allowed spelling of a language, or the tag itself when the profile allows every language. Returns
    /// <see langword="null"/> when the language is not allowed.
    /// </summary>
    /// <param name="settings">The profile's language settings, if any.</param>
    /// <param name="language">The language to check.</param>
    public static string Match(AIProfileLanguageSettings settings, string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        language = language.Trim();

        if (settings?.AllowedLanguages is not { Count: > 0 } allowed)
        {
            return language;
        }

        return allowed.FirstOrDefault(candidate => string.Equals(candidate?.Trim(), language, StringComparison.OrdinalIgnoreCase))?.Trim()
            ?? allowed.FirstOrDefault(candidate => SameLanguage(candidate, language))?.Trim();
    }

    /// <summary>
    /// Determines whether two tags name the same language, ignoring region.
    /// </summary>
    public static bool SameLanguage(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(Primary(left), Primary(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string Primary(string tag)
    {
        var trimmed = tag.Trim();
        var separator = trimmed.IndexOfAny(['-', '_']);

        return separator < 0 ? trimmed : trimmed[..separator];
    }
}
