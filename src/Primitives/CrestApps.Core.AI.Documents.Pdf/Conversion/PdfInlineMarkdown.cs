using System.Text;
using CrestApps.Core.AI.Documents.Generation.RichText;

namespace CrestApps.Core.AI.Documents.Pdf.Conversion;

/// <summary>
/// Writes formatted text as the inline Markdown a paragraph block reads: <c>**bold**</c>, <c>*italic*</c>,
/// <c>~~struck~~</c>, <c>`code`</c> and <c>[links](https://…)</c>.
/// </summary>
/// <remarks>
/// Text from a converted file is escaped first, so an asterisk or bracket the author typed stays a character
/// instead of turning the rest of the paragraph bold.
/// </remarks>
internal sealed class PdfInlineMarkdown
{
    private readonly StringBuilder _builder = new();
    private readonly StringBuilder _pending = new();
    private (bool Bold, bool Italic, bool Strike, bool Code, string Link) _style;

    /// <summary>
    /// Gets a value indicating whether nothing but white space has been written.
    /// </summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(_builder.ToString()) && string.IsNullOrWhiteSpace(_pending.ToString());

    /// <summary>
    /// Writes the text of one run. Consecutive runs in the same style are joined before they are marked up,
    /// the way a word processor splits one bold phrase into many runs.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="bold">Whether it is bold.</param>
    /// <param name="italic">Whether it is italic.</param>
    /// <param name="strike">Whether it is struck through.</param>
    /// <param name="code">Whether it is code.</param>
    /// <param name="link">The address it links to, or <see langword="null"/>.</param>
    public void Append(string text, bool bold = false, bool italic = false, bool strike = false, bool code = false, string link = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var style = (bold, italic, strike, code, IsWebLink(link) ? link : null);

        if (style != _style)
        {
            Flush();
            _style = style;
        }

        _pending.Append(text);
    }

    /// <summary>
    /// Returns the Markdown written so far.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        Flush();

        return _builder.ToString().Trim();
    }

    /// <summary>
    /// Writes parsed spans as inline Markdown.
    /// </summary>
    /// <param name="spans">The spans.</param>
    /// <returns>The text.</returns>
    public static string FromSpans(IEnumerable<RichTextSpan> spans)
    {
        var writer = new PdfInlineMarkdown();

        foreach (var span in spans ?? [])
        {
            writer.Append(span.Text, span.Bold, span.Italic, span.Strikethrough, span.Code, span.Link);
        }

        return writer.ToString();
    }

    /// <summary>
    /// Escapes the characters inline Markdown gives a meaning to.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    public static string Escape(string text)
    {
        var builder = new StringBuilder(text?.Length ?? 0);

        foreach (var character in text ?? string.Empty)
        {
            if (character is '\\' or '*' or '_' or '`' or '[' or ']' or '~')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private void Flush()
    {
        if (_pending.Length == 0)
        {
            return;
        }

        var text = _pending.ToString();
        _pending.Clear();

        // Markers go around the words, not the spaces beside them: "** bold**" is not bold.
        var start = 0;
        var end = text.Length;

        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        _builder.Append(text, 0, start);

        if (end > start)
        {
            var core = text[start..end];
            var (bold, italic, strike, code, link) = _style;
            var marked = code && !core.Contains('`', StringComparison.Ordinal) ? "`" + core + "`" : Escape(core);

            if (strike)
            {
                marked = "~~" + marked + "~~";
            }

            if (italic)
            {
                marked = "*" + marked + "*";
            }

            if (bold)
            {
                marked = "**" + marked + "**";
            }

            if (link is not null)
            {
                marked = "[" + marked + "](" + link.Replace(")", "%29", StringComparison.Ordinal).Replace(" ", "%20", StringComparison.Ordinal) + ")";
            }

            _builder.Append(marked);
        }

        _builder.Append(text, end, text.Length - end);
    }

    private static bool IsWebLink(string link)
    {
        return Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto";
    }
}
