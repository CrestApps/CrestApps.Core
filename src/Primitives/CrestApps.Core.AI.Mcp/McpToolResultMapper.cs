using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// Turns whatever a tool returned into the content blocks an MCP client receives.
/// </summary>
/// <remarks>
/// Every tool result used to be flattened to <c>ToString()</c>. That is right for prose and wrong for
/// anything else: a search that found a chart was reduced to a sentence saying a chart exists, with no way
/// for the client to see it.
/// </remarks>
public static class McpToolResultMapper
{
    /// <summary>
    /// Maps a tool result to a call result.
    /// </summary>
    /// <param name="result">Whatever the tool returned.</param>
    /// <returns>The call result.</returns>
    public static CallToolResult ToCallToolResult(object result)
    {
        return new CallToolResult
        {
            Content = ToContentBlocks(result),
        };
    }

    /// <summary>
    /// Maps a tool result to the blocks it is made of.
    /// </summary>
    /// <param name="result">Whatever the tool returned.</param>
    /// <returns>The content blocks. Never empty: a result with nothing in it is one empty text block.</returns>
    public static List<ContentBlock> ToContentBlocks(object result)
    {
        switch (result)
        {
            case null:

                return [Text(string.Empty)];

            case string text:

                return [Text(text)];

            case IMcpToolContentProvider provider:
                {
                    var blocks = provider.ToContentBlocks();

                    return blocks is { Count: > 0 } ? [.. blocks] : [Text(string.Empty)];
                }

            case IAIToolContentProvider provider:

                return Map(provider.ToContents());

            case AIContent content:

                return Map([content]);

            case IEnumerable<AIContent> contents:

                return Map(contents);

            default:

                return [Text(result.ToString() ?? string.Empty)];
        }
    }

    private static List<ContentBlock> Map(IEnumerable<AIContent> contents)
    {
        var blocks = new List<ContentBlock>();

        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent text when !string.IsNullOrEmpty(text.Text):

                    blocks.Add(Text(text.Text));

                    break;

                case DataContent data when data.HasTopLevelMediaType("image"):

                    blocks.Add(new ImageContentBlock
                    {
                        Data = data.Data,
                        MimeType = data.MediaType,
                    });

                    break;

                case DataContent data:

                    blocks.Add(new BlobResourceContents
                    {
                        Uri = data.Uri,
                        MimeType = data.MediaType,
                        Blob = data.Data,
                    }.ToResourceBlock());

                    break;

                case UriContent uri:

                    blocks.Add(new ResourceLinkBlock
                    {
                        Uri = uri.Uri.ToString(),
                        Name = ReadName(uri) ?? uri.Uri.ToString(),
                        MimeType = uri.MediaType,
                    });

                    break;
            }
        }

        return blocks.Count == 0 ? [Text(string.Empty)] : blocks;
    }

    /// <summary>
    /// Reads the display name a caller attached to a link, so a figure is listed by its caption rather than
    /// by an identifier no one can read.
    /// </summary>
    /// <param name="content">The link.</param>
    /// <returns>The name, or <see langword="null"/> when none was attached.</returns>
    private static string ReadName(AIContent content)
    {
        if (content.AdditionalProperties is null ||
            !content.AdditionalProperties.TryGetValue("name", out var value))
        {
            return null;
        }

        return value as string;
    }

    private static TextContentBlock Text(string text)
    {
        return new TextContentBlock
        {
            Text = text ?? string.Empty,
        };
    }

    private static EmbeddedResourceBlock ToResourceBlock(this ResourceContents contents)
    {
        return new EmbeddedResourceBlock
        {
            Resource = contents,
        };
    }
}
