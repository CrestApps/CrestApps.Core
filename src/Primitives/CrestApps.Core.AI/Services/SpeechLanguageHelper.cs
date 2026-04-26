using System.Globalization;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Provides functionality for speech Language Helper.
/// </summary>
public static class SpeechLanguageHelper
{
    private static readonly HashSet<string> _knownCultureNames = CultureInfo.GetCultures(CultureTypes.AllCultures)
        .Select(culture => culture.Name)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalize or defaults or default.
    /// </summary>
    /// <param name="language">The language.</param>
    /// <param name="fallbackLanguage">The fallback language.</param>
    public static string NormalizeOrDefault(string language, string fallbackLanguage = "en-US")
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return fallbackLanguage;
        }

        language = language.Trim();
        if (!_knownCultureNames.Contains(language))
        {
            return fallbackLanguage;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(language);

            return culture.IsNeutralCulture
                ? CultureInfo.CreateSpecificCulture(culture.Name).Name
                : culture.Name;
        }
        catch (CultureNotFoundException)
        {
            return fallbackLanguage;
        }
    }
}
