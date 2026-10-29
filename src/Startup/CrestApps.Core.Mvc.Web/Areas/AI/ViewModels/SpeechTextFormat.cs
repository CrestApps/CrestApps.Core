using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

/// <summary>
/// Converts the profile speech lists between the models they are stored as and the one-item-per-line text the
/// editor shows.
/// </summary>
public static class SpeechTextFormat
{
    private const string MatchCaseSuffix = "|case";

    private static readonly char[] _lineSeparators = ['\r', '\n'];

    public static List<string> ParseLines(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(_lineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string FormatLines(IEnumerable<string> values)
    {
        return values is null ? string.Empty : string.Join(Environment.NewLine, values);
    }

    /// <summary>
    /// Parses <c>word=pronunciation</c> lines, with an optional <c>|case</c> suffix that makes the match case-sensitive.
    /// Lines that cannot be read are returned in <paramref name="invalidLines"/>.
    /// </summary>
    public static List<PronunciationEntry> ParsePronunciations(string value, out List<string> invalidLines)
    {
        invalidLines = [];
        var entries = new List<PronunciationEntry>();

        foreach (var line in ParseLines(value))
        {
            var text = line;
            var matchCase = false;

            if (text.EndsWith(MatchCaseSuffix, StringComparison.OrdinalIgnoreCase))
            {
                matchCase = true;
                text = text[..^MatchCaseSuffix.Length];
            }

            var separator = text.IndexOf('=');

            if (separator <= 0 || separator == text.Length - 1)
            {
                invalidLines.Add(line);

                continue;
            }

            var word = text[..separator].Trim();
            var alias = text[(separator + 1)..].Trim();

            if (word.Length == 0 || alias.Length == 0)
            {
                invalidLines.Add(line);

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

    public static string FormatPronunciations(IEnumerable<PronunciationEntry> entries)
    {
        if (entries is null)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            entries.Select(entry => $"{entry.Text}={entry.Alias}{(entry.MatchCase ? MatchCaseSuffix : string.Empty)}"));
    }
}
