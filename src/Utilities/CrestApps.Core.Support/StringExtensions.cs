namespace CrestApps.Core.Support;

public static class StringExtensions
{
    /// <summary>
    /// Sanitizes a string value for safe inclusion in log messages by removing
    /// carriage return and newline characters that could be used for log injection.
    /// </summary>
    /// <param name="value">The value.</param>
    public static string SanitizeForLog(this string value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var valueSpan = value.AsSpan();
        var firstLineBreakIndex = valueSpan.IndexOfAny('\r', '\n');

        if (firstLineBreakIndex < 0)
        {
            return value;
        }

        var lineBreakCount = CountLineBreakCharacters(valueSpan[firstLineBreakIndex..]);

        return string.Create(value.Length - lineBreakCount, value, static (buffer, source) =>
        {
            var destinationIndex = 0;

            foreach (var character in source)
            {
                if (character is '\r' or '\n')
                {
                    continue;
                }

                buffer[destinationIndex] = character;
                destinationIndex++;
            }
        });
    }

    /// <summary>
    /// Counts carriage return and line feed characters in the value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The number of line break characters.</returns>
    private static int CountLineBreakCharacters(ReadOnlySpan<char> value)
    {
        var count = 0;

        foreach (var character in value)
        {
            if (character is '\r' or '\n')
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Extracts a title from the first line of the given content, truncating to 200 characters.
    /// </summary>
    /// <param name="content">The content.</param>
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

        return firstLine.Trim().ToString();
    }
}
