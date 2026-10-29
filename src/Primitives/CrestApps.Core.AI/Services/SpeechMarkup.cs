using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// The provider a piece of marked-up speech text is rendered for.
/// </summary>
public enum SpeechMarkupDialect
{
    /// <summary>
    /// A provider with no markup: pauses become punctuation and spelled text becomes separate letters.
    /// </summary>
    Plain = 0,

    /// <summary>
    /// ElevenLabs: <c>&lt;break time="1.0s" /&gt;</c> pauses; spelled text becomes hyphenated letters.
    /// </summary>
    ElevenLabs = 1,

    /// <summary>
    /// Cartesia: <c>&lt;break time="1s"/&gt;</c> pauses and <c>&lt;spell&gt;</c>.
    /// </summary>
    Cartesia = 2,
}

/// <summary>
/// Provider-neutral markup for speech text, so a prompt or a pronunciation entry can ask for a pause or for text to be
/// spelled out without knowing which provider speaks it.
/// </summary>
/// <remarks>
/// <para>
/// <c>&lt;break time="500ms"/&gt;</c> (or <c>1s</c>) pauses; <c>&lt;spell&gt;AB12&lt;/spell&gt;</c> reads the text one character
/// at a time. <see cref="Render"/> turns them into what the chosen provider understands, and into plain text for one
/// that understands neither, so markup never gets read aloud as "less than break".
/// </para>
/// </remarks>
public static partial class SpeechMarkup
{
    private const double MaximumPauseSeconds = 3;

    [GeneratedRegex("""<\s*break\s+time\s*=\s*["']\s*(?<value>[0-9]*\.?[0-9]+)\s*(?<unit>ms|s)?\s*["']\s*/?\s*>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreakPattern();

    [GeneratedRegex("""<\s*spell\s*>(?<text>.*?)<\s*/\s*spell\s*>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex SpellPattern();

    /// <summary>
    /// Gets whether the text holds any neutral markup.
    /// </summary>
    /// <param name="text">The text.</param>
    public static bool HasMarkup(string text)
        => !string.IsNullOrEmpty(text) && text.Contains('<') && (BreakPattern().IsMatch(text) || SpellPattern().IsMatch(text));

    /// <summary>
    /// Renders the neutral markup for a provider.
    /// </summary>
    /// <param name="text">The text, possibly with markup.</param>
    /// <param name="dialect">The provider to render for.</param>
    /// <returns>The text with its markup replaced; unchanged when it had none.</returns>
    public static string Render(string text, SpeechMarkupDialect dialect)
    {
        if (!HasMarkup(text))
        {
            return text;
        }

        var rendered = BreakPattern().Replace(text, match =>
        {
            var seconds = ParseSeconds(match);

            return dialect switch
            {
                SpeechMarkupDialect.ElevenLabs => $"<break time=\"{seconds.ToString("0.0#", CultureInfo.InvariantCulture)}s\" />",
                SpeechMarkupDialect.Cartesia => $"<break time=\"{seconds.ToString("0.0#", CultureInfo.InvariantCulture)}s\"/>",
                _ => seconds >= 1 ? "... " : ", ",
            };
        });

        return SpellPattern().Replace(rendered, match =>
        {
            var characters = match.Groups["text"].Value.Where(c => !char.IsWhiteSpace(c)).ToArray();

            if (dialect == SpeechMarkupDialect.Cartesia)
            {
                return $"<spell>{new string(characters)}</spell>";
            }

            return string.Join(dialect == SpeechMarkupDialect.ElevenLabs ? "-" : " ", characters);
        });
    }

    /// <summary>
    /// Removes the neutral markup, for a provider that would read it aloud.
    /// </summary>
    /// <param name="text">The text.</param>
    public static string Strip(string text) => Render(text, SpeechMarkupDialect.Plain);

    private static double ParseSeconds(Match match)
    {
        var value = double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        var seconds = string.Equals(match.Groups["unit"].Value, "ms", StringComparison.OrdinalIgnoreCase) ? value / 1000 : value;

        return Math.Clamp(seconds, 0, MaximumPauseSeconds);
    }
}
