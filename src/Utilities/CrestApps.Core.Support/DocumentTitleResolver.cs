namespace CrestApps.Core.Support;

/// <summary>
/// Resolves safe document titles when a data source does not map a dedicated title field.
/// </summary>
public static class DocumentTitleResolver
{
    /// <summary>
    /// Resolves a display title for a document without ever exposing a serialized document as the title.
    /// </summary>
    /// <param name="mappedTitle">The value read from the configured title field, when a title field is mapped.</param>
    /// <param name="content">The document content.</param>
    /// <param name="contentIsSerializedDocument">A value indicating whether <paramref name="content"/> is a serialized representation of the whole document rather than a mapped content field.</param>
    /// <param name="documentKey">The document key used as the fallback title.</param>
    /// <returns>The resolved title, or <see langword="null"/> when no safe title is available.</returns>
    public static string Resolve(string mappedTitle, string content, bool contentIsSerializedDocument, string documentKey)
    {
        if (!string.IsNullOrWhiteSpace(mappedTitle))
        {
            return mappedTitle;
        }

        if (!contentIsSerializedDocument && !string.IsNullOrWhiteSpace(content) && !LooksLikeSerializedDocument(content))
        {
            return content.ExtractTitleFromContent();
        }

        return string.IsNullOrWhiteSpace(documentKey)
            ? null
            : documentKey.Trim();
    }

    /// <summary>
    /// Determines whether a value looks like a serialized JSON document or object graph.
    /// </summary>
    /// <param name="value">The value to inspect.</param>
    /// <returns><see langword="true"/> when the value looks like a serialized document; otherwise, <see langword="false"/>.</returns>
    public static bool LooksLikeSerializedDocument(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.AsSpan().TrimStart();

        return span.Length > 0 && (span[0] == '{' || span[0] == '[');
    }
}
