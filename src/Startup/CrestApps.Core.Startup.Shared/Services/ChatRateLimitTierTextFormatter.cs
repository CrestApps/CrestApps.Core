using System.Globalization;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.Startup.Shared.Services;

/// <summary>
/// Converts a list of <see cref="ChatRateLimitTier"/> to and from the multi-line text format used
/// by the admin settings editors. Each tier is one line formatted as <c>limit, window</c>, where the
/// window is a <see cref="TimeSpan"/> string such as <c>00:00:30</c>, <c>01:00:00</c>, or
/// <c>1.00:00:00</c> (a day).
/// </summary>
public static class ChatRateLimitTierTextFormatter
{
    /// <summary>
    /// Formats the tiers as one <c>limit, window</c> line per tier.
    /// </summary>
    public static string Format(IEnumerable<ChatRateLimitTier> tiers)
    {
        if (tiers is null)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            tiers
                .Where(tier => tier is not null)
                .Select(tier => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{tier.Limit}, {tier.Window.ToString("c", CultureInfo.InvariantCulture)}")));
    }

    /// <summary>
    /// Parses the multi-line text into tiers. Blank lines are ignored. Returns <see langword="true"/>
    /// when every non-blank line is valid; otherwise <paramref name="error"/> describes the first
    /// problem and <paramref name="tiers"/> is empty.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="tiers">The parsed tiers, or an empty list when the text is blank or invalid.</param>
    /// <param name="error">The first validation error, or <see langword="null"/> when valid.</param>
    public static bool TryParse(string text, out List<ChatRateLimitTier> tiers, out string error)
    {
        tiers = [];
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lineNumber = 0;

        foreach (var line in lines)
        {
            lineNumber++;

            var separatorIndex = line.IndexOf(',');

            if (separatorIndex < 0)
            {
                error = $"Line {lineNumber}: use the format 'limit, window' (for example '5, 00:00:30').";
                tiers = [];

                return false;
            }

            var limitText = line[..separatorIndex].Trim();
            var windowText = line[(separatorIndex + 1)..].Trim();

            if (!int.TryParse(limitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit) || limit <= 0)
            {
                error = $"Line {lineNumber}: '{limitText}' is not a positive whole number of requests.";
                tiers = [];

                return false;
            }

            if (!TimeSpan.TryParse(windowText, CultureInfo.InvariantCulture, out var window) || window <= TimeSpan.Zero)
            {
                error = $"Line {lineNumber}: '{windowText}' is not a valid window (use hh:mm:ss, e.g. 00:00:30 or 1.00:00:00 for a day).";
                tiers = [];

                return false;
            }

            tiers.Add(new ChatRateLimitTier(limit, window));
        }

        return true;
    }
}
