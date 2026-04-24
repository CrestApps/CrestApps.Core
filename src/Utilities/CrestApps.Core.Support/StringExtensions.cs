namespace CrestApps.Core.Support;

public static class StringExtensions
{
    /// <summary>
    /// Sanitizes a string value for safe inclusion in log messages by removing
    /// carriage return and newline characters that could be used for log injection.
    /// </summary>
    public static string SanitizeForLog(this string value)
    {
        return value?.Replace("\r", "").Replace("\n", "") ?? string.Empty;
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

}
