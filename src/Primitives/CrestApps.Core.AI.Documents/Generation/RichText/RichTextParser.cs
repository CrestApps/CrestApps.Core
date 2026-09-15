using System.Globalization;
using System.Text;

namespace CrestApps.Core.AI.Documents.Generation.RichText;

/// <summary>
/// Parses the body text of a <see cref="GeneratedFileContent"/> into blocks a document writer can lay
/// out.
/// <para>
/// <see cref="GeneratedFileContent.Text"/> is documented as plain text or Markdown, and a model may
/// answer with HTML instead. A writer that emits the string verbatim shows the reader raw markup, so
/// the markup is interpreted here once and every writer renders the resulting structure.
/// </para>
/// <para>
/// This understands the constructs that actually appear in generated documents — headings, lists,
/// quotes, code, rules, tables, and inline emphasis — and treats anything it does not recognize as
/// ordinary text. It is not a complete Markdown implementation, and unrecognized syntax degrades to
/// readable prose rather than to visible markup.
/// </para>
/// </summary>
public static class RichTextParser
{
    /// <summary>
    /// Parses body text into blocks.
    /// </summary>
    /// <param name="text">The body text, as plain text, Markdown, or HTML.</param>
    /// <returns>The parsed blocks, in reading order.</returns>
    public static IReadOnlyList<RichTextBlock> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var source = HtmlToMarkupConverter.LooksLikeHtml(text)
            ? HtmlToMarkupConverter.Convert(text)
            : text;

        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var blocks = new List<RichTextBlock>();
        var paragraph = new List<string>();
        var numberedPosition = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                FlushParagraph(blocks, paragraph);
                numberedPosition = 0;

                continue;
            }

            if (IsFence(trimmed))
            {
                FlushParagraph(blocks, paragraph);
                numberedPosition = 0;
                index = ReadCodeBlock(lines, index, blocks);

                continue;
            }

            if (IsHorizontalRule(trimmed))
            {
                FlushParagraph(blocks, paragraph);
                numberedPosition = 0;
                blocks.Add(new RichTextBlock { Kind = RichTextBlockKind.HorizontalRule });

                continue;
            }

            if (TryReadHeading(trimmed, out var level, out var headingText))
            {
                FlushParagraph(blocks, paragraph);
                numberedPosition = 0;
                blocks.Add(new RichTextBlock
                {
                    Kind = RichTextBlockKind.Heading,
                    Level = level,
                    Spans = ParseInline(headingText),
                });

                continue;
            }

            if (IsTableStart(lines, index))
            {
                FlushParagraph(blocks, paragraph);
                numberedPosition = 0;
                index = ReadTable(lines, index, blocks);

                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph(blocks, paragraph);
                numberedPosition = 0;
                blocks.Add(new RichTextBlock
                {
                    Kind = RichTextBlockKind.Quote,
                    Spans = ParseInline(trimmed.TrimStart('>').Trim()),
                });

                continue;
            }

            if (TryReadListItem(line, out var ordered, out var itemText))
            {
                FlushParagraph(blocks, paragraph);

                blocks.Add(new RichTextBlock
                {
                    Kind = ordered ? RichTextBlockKind.NumberedItem : RichTextBlockKind.BulletItem,
                    Number = ordered ? ++numberedPosition : 0,
                    Spans = ParseInline(itemText),
                });

                if (!ordered)
                {
                    numberedPosition = 0;
                }

                continue;
            }

            // A plain line continues the paragraph being built. Joining wrapped lines is what turns a
            // hard-wrapped answer into flowing text instead of one stranded paragraph per line.
            paragraph.Add(trimmed);
        }

        FlushParagraph(blocks, paragraph);

        return blocks;
    }

    private static void FlushParagraph(List<RichTextBlock> blocks, List<string> paragraph)
    {
        if (paragraph.Count == 0)
        {
            return;
        }

        blocks.Add(new RichTextBlock
        {
            Kind = RichTextBlockKind.Paragraph,
            Spans = ParseInline(string.Join(' ', paragraph)),
        });

        paragraph.Clear();
    }

    private static bool IsFence(string trimmed)
    {
        return trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }

    private static int ReadCodeBlock(string[] lines, int start, List<RichTextBlock> blocks)
    {
        var builder = new StringBuilder();
        var index = start + 1;

        while (index < lines.Length && !IsFence(lines[index].Trim()))
        {
            builder.AppendLine(lines[index]);
            index++;
        }

        blocks.Add(new RichTextBlock
        {
            Kind = RichTextBlockKind.Code,
            Text = builder.ToString().TrimEnd('\n', '\r'),
        });

        return index;
    }

    private static bool IsHorizontalRule(string trimmed)
    {
        if (trimmed.Length < 3)
        {
            return false;
        }

        var marker = trimmed[0];

        if (marker is not ('-' or '*' or '_'))
        {
            return false;
        }

        foreach (var character in trimmed)
        {
            if (character != marker && character != ' ')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadHeading(string trimmed, out int level, out string text)
    {
        level = 0;
        text = null;

        while (level < trimmed.Length && trimmed[level] == '#')
        {
            level++;
        }

        if (level is 0 or > 6 || level >= trimmed.Length || trimmed[level] != ' ')
        {
            return false;
        }

        text = trimmed[(level + 1)..].Trim().TrimEnd('#').Trim();

        return text.Length > 0;
    }

    private static bool TryReadListItem(string line, out bool ordered, out string text)
    {
        ordered = false;
        text = null;

        var trimmed = line.TrimStart();

        if (trimmed.Length < 2)
        {
            return false;
        }

        if (trimmed[0] is '-' or '*' or '+')
        {
            if (trimmed[1] != ' ')
            {
                return false;
            }

            text = trimmed[2..].Trim();

            return text.Length > 0;
        }

        var digits = 0;

        while (digits < trimmed.Length && char.IsAsciiDigit(trimmed[digits]))
        {
            digits++;
        }

        if (digits == 0 ||
            digits + 1 >= trimmed.Length ||
            trimmed[digits] is not ('.' or ')') ||
            trimmed[digits + 1] != ' ')
        {
            return false;
        }

        ordered = true;
        text = trimmed[(digits + 2)..].Trim();

        return text.Length > 0;
    }

    private static bool IsTableStart(string[] lines, int index)
    {
        // A pipe table is only a table once a separator row follows it. Without that check, an ordinary
        // sentence containing a pipe would start a one-column table.
        return lines[index].Contains('|', StringComparison.Ordinal) &&
            index + 1 < lines.Length &&
            IsTableSeparator(lines[index + 1]);
    }

    private static bool IsTableSeparator(string line)
    {
        var trimmed = line.Trim();

        if (trimmed.Length == 0 || !trimmed.Contains('-', StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in trimmed)
        {
            if (character is not ('|' or '-' or ':' or ' '))
            {
                return false;
            }
        }

        return true;
    }

    private static int ReadTable(string[] lines, int start, List<RichTextBlock> blocks)
    {
        var table = new RichTextTable
        {
            Header = ReadTableRow(lines[start]),
        };

        var index = start + 2;

        while (index < lines.Length &&
            lines[index].Contains('|', StringComparison.Ordinal) &&
            lines[index].Trim().Length > 0)
        {
            table.Rows.Add(ReadTableRow(lines[index]));
            index++;
        }

        blocks.Add(new RichTextBlock
        {
            Kind = RichTextBlockKind.Table,
            Table = table,
        });

        return index - 1;
    }

    private static RichTextRow ReadTableRow(string line)
    {
        var trimmed = line.Trim();

        // The leading and trailing pipes are delimiters, not empty cells.
        if (trimmed.StartsWith('|'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith('|'))
        {
            trimmed = trimmed[..^1];
        }

        var row = new RichTextRow();

        foreach (var cell in trimmed.Split('|'))
        {
            row.Cells.Add(new RichTextCell
            {
                Spans = ParseInline(cell.Trim()),
            });
        }

        return row;
    }

    /// <summary>
    /// Parses the inline markup within a line into formatted runs.
    /// </summary>
    /// <param name="text">The line to parse.</param>
    /// <returns>The runs that make up the line.</returns>
    public static IList<RichTextSpan> ParseInline(string text)
    {
        var spans = new List<RichTextSpan>();

        if (string.IsNullOrEmpty(text))
        {
            return spans;
        }

        var literal = new StringBuilder();
        var bold = false;
        var italic = false;
        var strikethrough = false;
        var index = 0;

        void FlushLiteral()
        {
            if (literal.Length == 0)
            {
                return;
            }

            spans.Add(new RichTextSpan(literal.ToString())
            {
                Bold = bold,
                Italic = italic,
                Strikethrough = strikethrough,
            });

            literal.Clear();
        }

        while (index < text.Length)
        {
            var character = text[index];

            // An escape makes the next character literal, which is how a model writes an asterisk or an
            // underscore it does not mean as emphasis.
            if (character == '\\' && index + 1 < text.Length)
            {
                literal.Append(text[index + 1]);
                index += 2;

                continue;
            }

            if (character == '`')
            {
                var close = text.IndexOf('`', index + 1);

                if (close > index)
                {
                    FlushLiteral();
                    spans.Add(new RichTextSpan(text[(index + 1)..close]) { Code = true });
                    index = close + 1;

                    continue;
                }
            }

            if (character == '[' && TryReadLink(text, index, out var linkText, out var linkTarget, out var linkLength))
            {
                FlushLiteral();
                spans.Add(new RichTextSpan(linkText)
                {
                    Bold = bold,
                    Italic = italic,
                    Strikethrough = strikethrough,
                    Link = linkTarget,
                });

                index += linkLength;

                continue;
            }

            if (Matches(text, index, "~~"))
            {
                FlushLiteral();
                strikethrough = !strikethrough;
                index += 2;

                continue;
            }

            if (Matches(text, index, "**") || Matches(text, index, "__"))
            {
                FlushLiteral();
                bold = !bold;
                index += 2;

                continue;
            }

            if ((character == '*' || character == '_') && IsEmphasisMarker(text, index))
            {
                FlushLiteral();
                italic = !italic;
                index++;

                continue;
            }

            literal.Append(character);
            index++;
        }

        FlushLiteral();

        return spans;
    }

    private static bool Matches(string text, int index, string marker)
    {
        return index + marker.Length <= text.Length &&
            text.AsSpan(index, marker.Length).SequenceEqual(marker);
    }

    private static bool IsEmphasisMarker(string text, int index)
    {
        // An underscore inside a word belongs to an identifier such as a column name, not to emphasis.
        if (text[index] == '_')
        {
            var beforeIsWord = index > 0 && char.IsLetterOrDigit(text[index - 1]);
            var afterIsWord = index + 1 < text.Length && char.IsLetterOrDigit(text[index + 1]);

            if (beforeIsWord && afterIsWord)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadLink(string text, int index, out string linkText, out string target, out int length)
    {
        linkText = null;
        target = null;
        length = 0;

        var closeBracket = text.IndexOf(']', index + 1);

        if (closeBracket < 0 || closeBracket + 1 >= text.Length || text[closeBracket + 1] != '(')
        {
            return false;
        }

        var closeParenthesis = text.IndexOf(')', closeBracket + 2);

        if (closeParenthesis < 0)
        {
            return false;
        }

        linkText = text[(index + 1)..closeBracket];
        target = text[(closeBracket + 2)..closeParenthesis].Trim();
        length = closeParenthesis - index + 1;

        if (linkText.Length == 0)
        {
            // A link with no text should still show its destination rather than disappearing.
            linkText = target;
        }

        return true;
    }

    /// <summary>
    /// Renders blocks back to plain text, for writers that cannot express formatting at all.
    /// </summary>
    /// <param name="blocks">The parsed blocks.</param>
    /// <returns>The plain-text rendering.</returns>
    public static string ToPlainText(IReadOnlyList<RichTextBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var builder = new StringBuilder();

        foreach (var block in blocks)
        {
            switch (block.Kind)
            {
                case RichTextBlockKind.HorizontalRule:
                    builder.AppendLine("----------");

                    break;

                case RichTextBlockKind.Code:
                    builder.AppendLine(block.Text);

                    break;

                case RichTextBlockKind.BulletItem:
                    builder.Append("- ").AppendLine(Flatten(block.Spans));

                    break;

                case RichTextBlockKind.NumberedItem:
                    builder
                        .Append(block.Number.ToString(CultureInfo.InvariantCulture))
                        .Append(". ")
                        .AppendLine(Flatten(block.Spans));

                    break;

                case RichTextBlockKind.Table:
                    AppendTable(builder, block.Table);

                    break;

                default:
                    builder.AppendLine(Flatten(block.Spans));

                    break;
            }

            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static void AppendTable(StringBuilder builder, RichTextTable table)
    {
        AppendTableRow(builder, table.Header);

        foreach (var row in table.Rows)
        {
            AppendTableRow(builder, row);
        }
    }

    private static void AppendTableRow(StringBuilder builder, RichTextRow row)
    {
        builder.AppendLine(string.Join("\t", row.Cells.Select(cell => Flatten(cell.Spans))));
    }

    private static string Flatten(IEnumerable<RichTextSpan> spans)
    {
        return string.Concat(spans.Select(span => span.Text));
    }
}
