using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.DataIngestion;

/// <summary>
/// Reads an HTML document into an <see cref="IngestionDocument"/> for normalization and chunking. The
/// content comes from public websites and is therefore untrusted, so the document is parsed with a
/// standards-compliant HTML5 parser (AngleSharp) rather than pattern matching: script, style, and other
/// non-content nodes are removed together with their contents, and only the resulting text is kept.
/// AngleSharp is a parser only — it never executes scripts — so no markup or code survives into the text
/// that is embedded and stored. The page <c>&lt;title&gt;</c> is exposed separately through
/// <see cref="ExtractTitle(string)"/>.
/// </summary>
public sealed partial class HtmlIngestionDocumentReader : IngestionDocumentReader
{
    // Elements whose text content must never be indexed. Their nodes (and everything inside them) are
    // removed before any text is read, so executable or presentational payloads cannot leak through.
    private const string NonContentSelector = "script, style, noscript, template, iframe, object, embed, svg, canvas, head";

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
    /// Extracts the page title from the parsed <c>&lt;title&gt;</c> element.
    /// </summary>
    /// <param name="html">The raw HTML.</param>
    /// <returns>The plain-text title, or <see langword="null"/> when the page has no title.</returns>
    public static string ExtractTitle(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);

        // The title is RCDATA, so it cannot carry executable content, but it may contain angle-bracket text
        // (for example "Hi <b>there</b>"). Strip any tag-like sequences so nothing HTML-looking is stored.
        var title = CollapseWhitespace(TagRegex().Replace(document.Title ?? string.Empty, " "));

        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    /// <summary>
    /// Parses the HTML and returns the plain text of its body, with all script, style, and other
    /// non-content nodes removed.
    /// </summary>
    /// <param name="html">The raw HTML.</param>
    /// <returns>The plain-text body.</returns>
    public static string ExtractText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);

        foreach (var node in document.QuerySelectorAll(NonContentSelector).ToArray())
        {
            node.Remove();
        }

        var root = (INode)document.Body ?? document.DocumentElement;
        var text = CollapseWhitespace(root?.TextContent);

        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string CollapseWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();
}
