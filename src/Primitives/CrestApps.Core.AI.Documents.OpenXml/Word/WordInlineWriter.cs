using CrestApps.Core.AI.Documents.Generation.RichText;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.OpenXml.Word;

/// <summary>
/// Writes inline text into a paragraph: the emphasis, code and links of inline Markdown become formatted runs
/// and real hyperlinks, line breaks become breaks and tabs become tabs.
/// </summary>
internal static class WordInlineWriter
{
    /// <summary>
    /// Appends inline Markdown to a paragraph.
    /// </summary>
    /// <param name="paragraph">The paragraph, or any container runs can be appended to.</param>
    /// <param name="text">The text, with inline Markdown such as <c>**bold**</c>, <c>*italic*</c>, <c>`code`</c>, <c>~~struck~~</c> and <c>[label](https://…)</c>.</param>
    /// <param name="part">The part the paragraph belongs to, which owns any hyperlink relationship.</param>
    /// <param name="baseFormat">Formatting applied to every run, or <see langword="null"/>.</param>
    public static void AppendMarkdown(OpenXmlCompositeElement paragraph, string text, OpenXmlPart part, WordRunFormat baseFormat = null)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        AppendSpans(paragraph, RichTextParser.ParseInline(text), part, baseFormat);
    }

    /// <summary>
    /// Appends parsed spans to a paragraph.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="spans">The spans.</param>
    /// <param name="part">The part the paragraph belongs to.</param>
    /// <param name="baseFormat">Formatting applied to every run, or <see langword="null"/>.</param>
    public static void AppendSpans(OpenXmlCompositeElement paragraph, IEnumerable<RichTextSpan> spans, OpenXmlPart part, WordRunFormat baseFormat = null)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        if (spans is null)
        {
            return;
        }

        foreach (var span in spans)
        {
            if (string.IsNullOrEmpty(span.Text))
            {
                continue;
            }

            var run = CreateRun(span.Text, baseFormat);
            var properties = run.RunProperties ?? run.PrependChild(new RunProperties());

            if (span.Code)
            {
                properties.RunStyle = new RunStyle { Val = EnsureCharacterStyle(part, WordStyleSheet.InlineCode) };
            }

            if (span.Bold)
            {
                properties.Bold = new Bold();
            }

            if (span.Italic)
            {
                properties.Italic = new Italic();
            }

            if (span.Strikethrough)
            {
                properties.Strike = new Strike();
            }

            if (!properties.HasChildren)
            {
                properties.Remove();
            }

            var hyperlink = string.IsNullOrWhiteSpace(span.Link) ? null : CreateHyperlink(part, span.Link);

            if (hyperlink is null)
            {
                paragraph.Append(run);

                continue;
            }

            properties = run.RunProperties ?? run.PrependChild(new RunProperties());
            properties.RunStyle ??= new RunStyle { Val = EnsureCharacterStyle(part, WordStyleSheet.Hyperlink) };
            hyperlink.Append(run);
            paragraph.Append(hyperlink);
        }
    }

    /// <summary>
    /// Creates a run of plain text, turning line breaks into breaks and tabs into tabs.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="format">The formatting, or <see langword="null"/>.</param>
    /// <returns>The run.</returns>
    public static Run CreateRun(string text, WordRunFormat format = null)
    {
        var run = new Run();

        if (format is { IsEmpty: false })
        {
            var properties = new RunProperties();

            format.ApplyTo(properties);

            if (properties.HasChildren)
            {
                run.Append(properties);
            }
        }

        AppendText(run, text);

        return run;
    }

    /// <summary>
    /// Appends text to a run, turning line breaks into breaks and tabs into tabs.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="text">The text.</param>
    public static void AppendText(Run run, string text)
    {
        ArgumentNullException.ThrowIfNull(run);

        var lines = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (lineIndex > 0)
            {
                run.Append(new Break());
            }

            var pieces = lines[lineIndex].Split('\t');

            for (var pieceIndex = 0; pieceIndex < pieces.Length; pieceIndex++)
            {
                if (pieceIndex > 0)
                {
                    run.Append(new TabChar());
                }

                if (pieces[pieceIndex].Length > 0)
                {
                    run.Append(new Text(pieces[pieceIndex]) { Space = SpaceProcessingModeValues.Preserve });
                }
            }
        }
    }

    /// <summary>
    /// Creates a hyperlink to a web address, an e-mail address, or a bookmark (<c>#name</c>).
    /// </summary>
    /// <param name="part">The part that owns the relationship.</param>
    /// <param name="target">The target.</param>
    /// <returns>The empty hyperlink, or <see langword="null"/> when the target is not one a document may link to.</returns>
    public static Hyperlink CreateHyperlink(OpenXmlPart part, string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var trimmed = target.Trim();

        if (trimmed.StartsWith('#'))
        {
            var anchor = trimmed[1..];

            return anchor.Length == 0 ? null : new Hyperlink { Anchor = anchor, History = true };
        }

        if (part is null || !IsSafeExternalTarget(trimmed, out var uri))
        {
            return null;
        }

        var relationship = part.AddHyperlinkRelationship(uri, true);

        return new Hyperlink { Id = relationship.Id, History = true };
    }

    /// <summary>
    /// Returns whether an address is one a document may link to: a web address or an e-mail address. A script
    /// or file address is refused, because the reader would follow it from a trusted-looking document.
    /// </summary>
    /// <param name="target">The address.</param>
    /// <param name="uri">The parsed address.</param>
    /// <returns><see langword="true"/> for an <c>http</c>, <c>https</c> or <c>mailto</c> address.</returns>
    public static bool IsSafeExternalTarget(string target, out Uri uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var candidate = target.Trim();

        if (!candidate.Contains(':', StringComparison.Ordinal) && candidate.Contains('@', StringComparison.Ordinal))
        {
            candidate = "mailto:" + candidate;
        }
        else if (candidate.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            candidate = "https://" + candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeMailto)
        {
            return false;
        }

        uri = parsed;

        return true;
    }

    private static string EnsureCharacterStyle(OpenXmlPart part, string styleId)
    {
        var mainPart = part switch
        {
            MainDocumentPart main => main,
            _ => part?.GetParentParts().OfType<MainDocumentPart>().FirstOrDefault(),
        };

        return mainPart is null
            ? styleId
            : WordStyleSheet.Ensure(mainPart, styleId, null);
    }
}
