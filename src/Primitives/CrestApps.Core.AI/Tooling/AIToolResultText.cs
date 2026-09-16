using System.Text;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Turns a tool result into what a chat model should read.
/// </summary>
/// <remarks>
/// A function-invoking chat client serializes any result that is not a string as JSON before handing it to
/// the model. That is right for a plain data object and wrong for a result that knows how to render itself:
/// a search result would arrive as a JSON envelope around its text, and a figure result would arrive with its
/// picture inlined as a base64 string many kilobytes long. The rich shape exists for transports that can use
/// it, such as an MCP server; a chat turn gets the text.
/// </remarks>
public static class AIToolResultText
{
    /// <summary>
    /// Normalizes a tool result for a chat model.
    /// </summary>
    /// <param name="result">Whatever the tool returned.</param>
    /// <returns>
    /// The text of a result that renders itself, the text parts of AI content, or the result unchanged when it
    /// is already a string or a plain object the caller meant to serialize.
    /// </returns>
    public static object Normalize(object result)
    {
        switch (result)
        {
            case null:
            case string:
                return result;

            case IAIToolContentProvider provider:
                return provider.ToString();

            case AIContent content:
                return Render([content]) ?? result;

            case IEnumerable<AIContent> contents:
                return Render(contents) ?? result;

            default:
                return result;
        }
    }

    /// <summary>
    /// Joins the text parts of a set of contents.
    /// </summary>
    /// <param name="contents">The contents.</param>
    /// <returns>The joined text, or <see langword="null"/> when nothing in the set was text.</returns>
    private static string Render(IEnumerable<AIContent> contents)
    {
        var builder = new StringBuilder();

        foreach (var content in contents)
        {
            if (content is not TextContent text || string.IsNullOrEmpty(text.Text))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(text.Text);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
