using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.WebMcp.Services;

/// <summary>
/// Turns what an AI tool answers into the text a browser agent reads.
/// </summary>
internal static class WebMcpToolResultFormatter
{
    /// <summary>
    /// Formats a tool's answer: the text of a result that renders itself or of AI contents, text as it is, a JSON
    /// string as its text, and anything else as JSON.
    /// </summary>
    /// <param name="result">The tool's answer.</param>
    /// <returns>The text.</returns>
    public static string Format(object result)
    {
        return AIToolResultText.Normalize(result) switch
        {
            null => string.Empty,
            string text => text,
            JsonElement element => element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.GetRawText(),
            JsonNode node => node.ToJsonString(),
            TextContent content => content.Text,
            IEnumerable<AIContent> contents => string.Join('\n', contents.OfType<TextContent>().Select(content => content.Text)),
            var other => JsonSerializer.Serialize(other, other.GetType()),
        };
    }
}
