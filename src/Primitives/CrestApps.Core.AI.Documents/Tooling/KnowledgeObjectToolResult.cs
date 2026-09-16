using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Documents.Tooling;

/// <summary>
/// One knowledge object as a tool result: always its text, and for a figure the picture itself.
/// </summary>
/// <remarks>
/// A chat turn reads the text. A client that can show pictures gets the picture, which is the whole point of
/// asking for a figure by identifier rather than reading about it.
/// </remarks>
public sealed class KnowledgeObjectToolResult : IAIToolContentProvider
{
    /// <summary>
    /// Gets the text describing the object.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    /// Gets the picture bytes, when the object is a figure small enough to return inline.
    /// </summary>
    public ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>
    /// Gets the media type of the picture.
    /// </summary>
    public string MediaType { get; init; }

    /// <summary>
    /// Gets the address of the picture when it is too large to return inline, so the caller can fetch it.
    /// </summary>
    public string Uri { get; init; }

    /// <summary>
    /// Returns the text, so the result also works as a plain chat tool result.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        return Text;
    }

    /// <summary>
    /// Renders the result as its text plus the picture, inline or as a link.
    /// </summary>
    /// <returns>The contents.</returns>
    public IReadOnlyList<AIContent> ToContents()
    {
        var contents = new List<AIContent>
        {
            new TextContent(Text),
        };

        if (!Content.IsEmpty)
        {
            contents.Add(new DataContent(Content, string.IsNullOrWhiteSpace(MediaType) ? "image/png" : MediaType));
        }
        else if (!string.IsNullOrWhiteSpace(Uri) && System.Uri.TryCreate(Uri, UriKind.Absolute, out var uri))
        {
            contents.Add(new UriContent(uri, string.IsNullOrWhiteSpace(MediaType) ? "image/png" : MediaType));
        }

        return contents;
    }
}
