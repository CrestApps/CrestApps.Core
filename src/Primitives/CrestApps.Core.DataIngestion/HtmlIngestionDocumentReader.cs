using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.DataIngestion;

/// <summary>
/// Reads an HTML document into an <see cref="IngestionDocument"/> for normalization and chunking. It
/// removes <c>&lt;script&gt;</c> and <c>&lt;style&gt;</c> blocks, strips the remaining tags, decodes HTML
/// entities, and collapses insignificant whitespace, producing a single plain-text paragraph. The page
/// <c>&lt;title&gt;</c> is exposed separately through <see cref="ExtractTitle(string)"/>.
/// </summary>
public sealed partial class HtmlIngestionDocumentReader : IngestionDocumentReader
{
    /// <summary>
    /// Reads an HTML stream into an <see cref="IngestionDocument"/>.
    /// </summary>
    /// <param name="source">The HTML source stream.</param>
    /// <param name="identifier">The document identifier (typically the page URL).</param>
    /// <param name="mediaType">The media type (ignored; the content is treated as HTML).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        cancellationToken.ThrowIfCancellationRequested();

        using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var html = await reader.ReadToEndAsync(cancellationToken);

        return Read(html, identifier);
    }

    /// <summary>
    /// Reads an in-memory HTML string into an <see cref="IngestionDocument"/>.
    /// </summary>
    /// <param name="html">The raw HTML.</param>
    /// <param name="identifier">The document identifier (typically the page URL).</param>
    /// <returns>The ingestion document.</returns>
    public static IngestionDocument Read(string html, string identifier)
    {
        var document = new IngestionDocument(identifier);
        var text = ExtractText(html);

        if (!string.IsNullOrWhiteSpace(text))
        {
            var section = new IngestionDocumentSection();
            section.Elements.Add(new IngestionDocumentParagraph(text)
            {
                Text = text,
            });

            document.Sections.Add(section);
        }

        return document;
    }

    /// <summary>
    /// Extracts the page title from the <c>&lt;title&gt;</c> element.
    /// </summary>
    /// <param name="html">The raw HTML.</param>
    /// <returns>The decoded, trimmed title, or <see langword="null"/> when the page has no title.</returns>
    public static string ExtractTitle(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        var match = TitleRegex().Match(html);

        if (!match.Success)
        {
            return null;
        }

        return WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
    }

    /// <summary>
    /// Strips scripts, styles, and markup from HTML and returns the collapsed plain text.
    /// </summary>
    /// <param name="html">The raw HTML.</param>
    /// <returns>The plain-text body.</returns>
    public static string ExtractText(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        var withoutScripts = ScriptRegex().Replace(html, " ");
        var withoutStyles = StyleRegex().Replace(withoutScripts, " ");
        var withoutTags = TagRegex().Replace(withoutStyles, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);

        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex(@"<script\b[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(@"<style\b[^>]*>.*?</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleRegex();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
