using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Blazor.Web.ViewModels;

/// <summary>
/// Converts the line-based text the profile editor uses for speech list settings to and from the stored model.
/// </summary>
public static class AIProfileSpeechFormatter
{
    private const string MatchCaseSuffix = "|case";

    private static readonly char[] _lineSeparators = ['\n', '\r'];

    public static string FormatLines(IEnumerable<string> values)
    {
        return string.Join(Environment.NewLine, values ?? []);
    }

    public static List<string> ParseLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text.Split(_lineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string FormatPronunciations(IEnumerable<PronunciationEntry> entries)
    {
        return string.Join(Environment.NewLine, (entries ?? []).Select(e => $"{e.Text}={e.Alias}{(e.MatchCase ? MatchCaseSuffix : string.Empty)}"));
    }

    /// <summary>
    /// Parses <c>word=pronunciation</c> lines. A line ending in <c>|case</c> matches case. Lines that are not valid are
    /// reported through <paramref name="errors"/>.
    /// </summary>
    public static List<PronunciationEntry> ParsePronunciations(string text, ICollection<string> errors = null)
    {
        var entries = new List<PronunciationEntry>();

        foreach (var line in ParseLines(text))
        {
            var matchCase = line.EndsWith(MatchCaseSuffix, StringComparison.OrdinalIgnoreCase);
            var body = matchCase ? line[..^MatchCaseSuffix.Length] : line;
            var separator = body.IndexOf('=');

            var word = separator > 0 ? body[..separator].Trim() : string.Empty;
            var alias = separator > 0 ? body[(separator + 1)..].Trim() : string.Empty;

            if (word.Length == 0 || alias.Length == 0)
            {
                errors?.Add($"Pronunciation \"{line}\" must be written as word=pronunciation.");

                continue;
            }

            entries.Add(new PronunciationEntry
            {
                Text = word,
                Alias = alias,
                MatchCase = matchCase,
            });
        }

        return entries;
    }
}
