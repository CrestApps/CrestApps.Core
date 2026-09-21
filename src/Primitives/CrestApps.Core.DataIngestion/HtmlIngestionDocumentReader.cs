using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CrestApps.Core.Ingestion;
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
    // The elements a page is written in, as opposed to the ones it is laid out with. A div wraps; a
    // paragraph, a list item or a heading says something, and each becomes one element of the document.
    private const string BlockSelector = "h1, h2, h3, h4, h5, h6, p, li, blockquote, pre, figcaption, td, th, dd, dt";

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
        var blocks = ExtractBlocks(html);

        if (blocks.Count > 0)
        {
            var section = new IngestionDocumentSection();

            foreach (var block in blocks)
            {
                var element = new IngestionDocumentParagraph(block.Text)
                {
                    Text = block.Text,
                };

                if (block.HeadingLevel > 0)
                {
                    element.Metadata[ElementMetadataKeys.HeadingLevel] = block.HeadingLevel;
                }

                section.Elements.Add(element);
            }

            document.Sections.Add(section);

            return document;
        }

        // A page with no block structure at all -- a fragment, or a body of bare text nodes -- still has
        // text worth reading, and losing it to a stricter walk would be a regression.
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
    /// Splits a page into the blocks it is written in, marking the ones that are headings.
    /// </summary>
    /// <param name="html">The raw HTML.</param>
    /// <returns>The blocks in document order.</returns>
    /// <remarks>
    /// HTML states which of its text is a heading and how deeply that heading nests, which is the thing a
    /// PDF has to be interrogated about. Reading the page as one run of text threw that away and made every
    /// crawled page one undivided article no matter how it was written.
    /// <para>
    /// Only the blocks that carry text of their own are emitted. Walking every element would emit a section
    /// wrapping a heading and the heading again, and the page's text would be stored as many times as it is
    /// nested deep.
    /// </para>
    /// </remarks>
    private static List<(string Text, int HeadingLevel)> ExtractBlocks(string html)
    {
        var blocks = new List<(string Text, int HeadingLevel)>();

        if (string.IsNullOrWhiteSpace(html))
        {
            return blocks;
        }

        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);

        foreach (var node in document.QuerySelectorAll(NonContentSelector).ToArray())
        {
            node.Remove();
        }

        var root = document.Body ?? document.DocumentElement;

        if (root is null)
        {
            return blocks;
        }

        foreach (var element in root.QuerySelectorAll(BlockSelector))
        {
            // An element that contains another block is a wrapper around it; the block itself is emitted
            // when the walk reaches it.
            if (element.QuerySelector(BlockSelector) is not null)
            {
                continue;
            }

            var text = CollapseWhitespace(element.TextContent);

            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            blocks.Add((text, GetHeadingLevel(element.LocalName)));
        }

        return blocks;
    }

    /// <summary>
    /// Reads the heading level a tag states.
    /// </summary>
    /// <param name="localName">The tag name.</param>
    /// <returns>The one-based level, or zero when the tag is not a heading.</returns>
    private static int GetHeadingLevel(string localName)
    {
        if (string.IsNullOrEmpty(localName) || localName.Length != 2)
        {
            return 0;
        }

        if (localName[0] is not ('h' or 'H'))
        {
            return 0;
        }

        return localName[1] is >= '1' and <= '6' ? localName[1] - '0' : 0;
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
