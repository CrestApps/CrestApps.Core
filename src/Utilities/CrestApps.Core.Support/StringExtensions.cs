using System.Text.RegularExpressions;

namespace CrestApps.Core.Support;

public static partial class StringExtensions
{
    private static readonly TimeSpan _regexTimeout = TimeSpan.FromSeconds(2);

    [GeneratedRegex(@"\.|\$|\^|\{|\[|\(|\||\)|\*|\+|\?|\\")]
    private static partial Regex SpecialCharsRegex();

    /// <summary>
    /// Sanitizes a string value for safe inclusion in log messages by removing
    /// carriage return and newline characters that could be used for log injection.
    /// </summary>
    public static string SanitizeForLog(this string value)
    {
        return value?.Replace("\r", "").Replace("\n", "") ?? string.Empty;
    }

    /// <summary>
    /// Sanitizes a string value for safe inclusion in log messages by removing
    /// carriage return and newline characters that could be used for log injection.
    /// </summary>
    [Obsolete("Use SanitizeForLog instead.")]
    public static string SanitizeLogValue(this string value)
    {
        return value.SanitizeForLog();
    }

    /// <summary>
    /// Extracts a title from the first line of the given content, truncating to 200 characters.
    /// </summary>
    public static string ExtractTitleFromContent(this string content)
    {
        var firstLine = content.AsSpan();
        var newlineIndex = firstLine.IndexOfAny('\r', '\n');

        if (newlineIndex > 0)
        {
            firstLine = firstLine[..newlineIndex];
        }

        if (firstLine.Length > 200)
        {
            firstLine = firstLine[..200];
        }

        return firstLine.ToString().Trim();
    }

    public static bool Like(this string toSearch, string toFind)
    {
        ArgumentNullException.ThrowIfNull(toSearch);
        ArgumentNullException.ThrowIfNull(toFind);

        var match = SpecialCharsRegex().Replace(toFind, ch => @"\" + ch).Replace('_', '.').Replace("%", ".*");

        return new Regex(@"\A" + match + @"\z", RegexOptions.Singleline, _regexTimeout).IsMatch(toSearch);
    }

    public static bool Like(this string toSearch, string toFind, StringComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(toSearch);
        ArgumentNullException.ThrowIfNull(toFind);

        if (comparison == StringComparison.CurrentCultureIgnoreCase || comparison == StringComparison.OrdinalIgnoreCase || comparison == StringComparison.InvariantCultureIgnoreCase)
        {
            return Like(toSearch.ToLower(), toFind.ToLower());
        }

        return Like(toSearch, toFind);
    }

    public static string GetControllerName(this string name)
    {
        return Str.TrimEnd(name, "Controller", StringComparison.OrdinalIgnoreCase);
    }
}
