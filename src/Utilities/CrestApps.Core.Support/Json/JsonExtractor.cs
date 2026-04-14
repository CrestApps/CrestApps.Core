using System.Text.RegularExpressions;

namespace CrestApps.Core.Support.Json;

/// <summary>
/// Provides methods for extracting JSON content from AI-generated text that
/// may contain markdown code fences, prose, or other surrounding content.
/// </summary>
public static class JsonExtractor
{
    /// <summary>
    /// Extracts the first valid JSON object from text that may include markdown code
    /// fences, surrounding prose, or other non-JSON content.
    /// </summary>
    /// <remarks>
    /// The extraction proceeds through three strategies in order:
    /// <list type="number">
    ///   <item>Strip markdown code fences (<c>```json</c> or generic <c>```</c>).</item>
    ///   <item>Locate a brace-balanced JSON object using a state machine that
    ///         correctly handles string escapes.</item>
    /// </list>
    /// </remarks>
    /// <param name="text">The raw text that may contain a JSON object.</param>
    /// <returns>The extracted JSON string, or <see langword="null"/> if no valid JSON object was found.</returns>
    public static string ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();

        // Strip markdown code fences if present.
        trimmed = StripCodeFence(trimmed);

        return ExtractBracedObject(trimmed);
    }

    /// <summary>
    /// Extracts the content of a markdown code fence from text.
    /// </summary>
    /// <param name="text">The text that may be wrapped in markdown code fences.</param>
    /// <returns>The content inside the code fence, or <see langword="null"/> if no fence was found.</returns>
    public static string ExtractFromCodeFence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Regex.Match(
            text,
            @"```(?:json)?\s*\n?([\s\S]*?)\n?\s*```",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Strips markdown code fences from text that starts with a fence marker.
    /// </summary>
    private static string StripCodeFence(string text)
    {
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            var endIndex = text.IndexOf("```", 7);
            if (endIndex > 7)
            {
                return text[7..endIndex].Trim();
            }
        }
        else if (text.StartsWith("```"))
        {
            var startIndex = text.IndexOf('\n');
            if (startIndex > 0)
            {
                var endIndex = text.LastIndexOf("```");
                if (endIndex > startIndex)
                {
                    return text[(startIndex + 1)..endIndex].Trim();
                }
            }
        }

        return text;
    }

    /// <summary>
    /// Extracts the first brace-balanced JSON object from text using a state machine
    /// that correctly handles string escape sequences.
    /// </summary>
    private static string ExtractBracedObject(string text)
    {
        var jsonStart = text.IndexOf('{');

        if (jsonStart < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = jsonStart; i < text.Length; i++)
        {
            var c = text[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[jsonStart..(i + 1)];
                }
            }
        }

        return null;
    }
}
