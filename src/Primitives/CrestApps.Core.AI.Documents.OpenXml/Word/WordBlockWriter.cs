using CrestApps.Core.AI.Documents.Generation.RichText;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.OpenXml.Word;

/// <summary>
/// Builds the block content of a document — headings, paragraphs, lists, quotes, code, rules, page breaks and
/// tables — in the document's own styles.
/// </summary>
/// <remarks>
/// The generated-file writer and the Word agent both build documents through this class, so a document made
/// in one call and a document built up over a conversation have the same structure, styles and formatting.
/// A style is looked up in the document first and only added when the document has nothing that matches, so
/// content added to an uploaded document takes that document's look.
/// </remarks>
internal sealed class WordBlockWriter
{
    private readonly MainDocumentPart _mainPart;
    private readonly OpenXmlPart _part;
    private readonly Dictionary<string, string> _styleIds = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="WordBlockWriter"/> class.
    /// </summary>
    /// <param name="mainPart">The main document part.</param>
    /// <param name="design">The design missing styles are added with.</param>
    /// <param name="textWidthTwips">The width between the margins, in twips, that tables are sized to.</param>
    /// <param name="part">The part the content is placed in, when it is not the main document.</param>
    public WordBlockWriter(MainDocumentPart mainPart, WordDesign design, int textWidthTwips, OpenXmlPart part = null)
    {
        ArgumentNullException.ThrowIfNull(mainPart);

        _mainPart = mainPart;
        _part = part ?? mainPart;
        Design = design ?? new WordDesign();
        TextWidthTwips = textWidthTwips > 0 ? textWidthTwips : 9360;
    }

    /// <summary>
    /// Gets the design missing styles are added with.
    /// </summary>
    public WordDesign Design { get; }

    /// <summary>
    /// Gets the width between the margins, in twips.
    /// </summary>
    public int TextWidthTwips { get; }

    /// <summary>
    /// Gets the main document part.
    /// </summary>
    public MainDocumentPart MainPart => _mainPart;

    /// <summary>
    /// Gets the part the content is placed in.
    /// </summary>
    public OpenXmlPart Part => _part;

    /// <summary>
    /// Returns the id the document uses for one of the standard styles, adding it when missing.
    /// </summary>
    /// <param name="styleId">The standard style id.</param>
    /// <returns>The id to reference.</returns>
    public string Style(string styleId)
    {
        if (!_styleIds.TryGetValue(styleId, out var resolved))
        {
            resolved = WordStyleSheet.Ensure(_mainPart, styleId, Design);
            _styleIds[styleId] = resolved;
        }

        return resolved;
    }

    /// <summary>
    /// Builds a heading.
    /// </summary>
    /// <param name="text">The heading text, with inline Markdown.</param>
    /// <param name="level">The level, from 1.</param>
    /// <returns>The paragraph.</returns>
    public Paragraph Heading(string text, int level)
    {
        return Paragraph(text, Style(WordStyleSheet.Heading(Math.Clamp(level, 1, 6))));
    }

    /// <summary>
    /// Builds a paragraph.
    /// </summary>
    /// <param name="text">The text, with inline Markdown.</param>
    /// <param name="styleId">The paragraph style id, or <see langword="null"/> for body text.</param>
    /// <param name="alignment">The alignment, or <see langword="null"/> for the style's.</param>
    /// <param name="format">Formatting for every run, or <see langword="null"/>.</param>
    /// <returns>The paragraph.</returns>
    public Paragraph Paragraph(string text, string styleId = null, JustificationValues? alignment = null, WordRunFormat format = null)
    {
        var paragraph = new Paragraph();
        var properties = new ParagraphProperties();

        if (!string.IsNullOrEmpty(styleId))
        {
            properties.ParagraphStyleId = new ParagraphStyleId { Val = styleId };
        }

        if (alignment is not null)
        {
            properties.Justification = new Justification { Val = alignment.Value };
        }

        if (properties.HasChildren)
        {
            paragraph.Append(properties);
        }

        WordInlineWriter.AppendMarkdown(paragraph, text, _part, format);

        return paragraph;
    }

    /// <summary>
    /// Builds a paragraph from parsed spans.
    /// </summary>
    /// <param name="spans">The spans.</param>
    /// <param name="styleId">The paragraph style id, or <see langword="null"/> for body text.</param>
    /// <returns>The paragraph.</returns>
    public Paragraph Paragraph(IEnumerable<RichTextSpan> spans, string styleId = null)
    {
        var paragraph = new Paragraph();

        if (!string.IsNullOrEmpty(styleId))
        {
            paragraph.Append(new ParagraphProperties { ParagraphStyleId = new ParagraphStyleId { Val = styleId } });
        }

        WordInlineWriter.AppendSpans(paragraph, spans, _part);

        return paragraph;
    }

    /// <summary>
    /// Builds a list.
    /// </summary>
    /// <param name="items">The items, each with its nesting level from 0.</param>
    /// <param name="numbered">Whether the list is numbered rather than bulleted.</param>
    /// <param name="start">The first number of a numbered list.</param>
    /// <returns>One paragraph per item.</returns>
    public List<Paragraph> List(IEnumerable<(string Text, int Level)> items, bool numbered, int start = 1)
    {
        ArgumentNullException.ThrowIfNull(items);

        var numberId = WordNumbering.CreateList(_mainPart, numbered, start);
        var style = Style(WordStyleSheet.ListParagraph);
        var paragraphs = new List<Paragraph>();

        foreach (var (text, level) in items)
        {
            var paragraph = ListItem(numberId, Math.Clamp(level, 0, 8), style);

            WordInlineWriter.AppendMarkdown(paragraph, text, _part);
            paragraphs.Add(paragraph);
        }

        return paragraphs;
    }

    /// <summary>
    /// Builds a quotation.
    /// </summary>
    /// <param name="text">The quoted text, with inline Markdown.</param>
    /// <returns>The paragraph.</returns>
    public Paragraph Quote(string text)
    {
        return Paragraph(text, Style(WordStyleSheet.Quote));
    }

    /// <summary>
    /// Builds a code block, one paragraph per line so long code breaks across pages at line boundaries.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <returns>The paragraphs.</returns>
    public List<Paragraph> Code(string code)
    {
        var style = Style(WordStyleSheet.CodeBlock);
        var lines = (code ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var paragraphs = new List<Paragraph>(lines.Length);

        foreach (var line in lines)
        {
            var paragraph = new Paragraph(new ParagraphProperties { ParagraphStyleId = new ParagraphStyleId { Val = style } });

            paragraph.Append(WordInlineWriter.CreateRun(line));
            paragraphs.Add(paragraph);
        }

        return paragraphs;
    }

    /// <summary>
    /// Builds a horizontal rule: an empty paragraph with a bottom border.
    /// </summary>
    /// <returns>The paragraph.</returns>
    public Paragraph Rule()
    {
        return new Paragraph(new ParagraphProperties
        {
            ParagraphBorders = new ParagraphBorders(new BottomBorder
            {
                Val = BorderValues.Single,
                Size = 6U,
                Space = 1U,
                Color = Design.TableBorderColor,
            }),
            SpacingBetweenLines = new SpacingBetweenLines { Before = "120", After = "120" },
        });
    }

    /// <summary>
    /// Builds a page break.
    /// </summary>
    /// <returns>A paragraph holding the break.</returns>
    public static Paragraph PageBreak()
    {
        return new Paragraph(new Run(new Break { Type = BreakValues.Page }));
    }

    /// <summary>
    /// Builds a table.
    /// </summary>
    /// <param name="spec">The table.</param>
    /// <returns>The table.</returns>
    public Table Table(WordTableSpec spec)
    {
        return WordTableWriter.Create(_mainPart, _part, spec, Design, TextWidthTwips);
    }

    /// <summary>
    /// Builds the blocks of parsed Markdown or HTML. Consecutive list items become one list.
    /// </summary>
    /// <param name="blocks">The parsed blocks.</param>
    /// <returns>The elements, in order.</returns>
    public List<OpenXmlElement> FromRichText(IReadOnlyList<RichTextBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var elements = new List<OpenXmlElement>();
        var index = 0;

        while (index < blocks.Count)
        {
            var block = blocks[index];

            switch (block.Kind)
            {
                case RichTextBlockKind.Heading:
                    elements.Add(Paragraph(block.Spans, Style(WordStyleSheet.Heading(Math.Clamp(block.Level, 1, 6)))));
                    index++;

                    break;

                case RichTextBlockKind.BulletItem:
                case RichTextBlockKind.NumberedItem:
                    index = AppendList(blocks, index, elements);

                    break;

                case RichTextBlockKind.Quote:
                    elements.Add(Paragraph(block.Spans, Style(WordStyleSheet.Quote)));
                    index++;

                    break;

                case RichTextBlockKind.Code:
                    elements.AddRange(Code(block.Text));
                    index++;

                    break;

                case RichTextBlockKind.HorizontalRule:
                    elements.Add(Rule());
                    index++;

                    break;

                case RichTextBlockKind.Table:
                    elements.Add(Table(ToTableSpec(block.Table)));
                    index++;

                    break;

                default:
                    elements.Add(Paragraph(block.Spans));
                    index++;

                    break;
            }
        }

        return elements;
    }

    /// <summary>
    /// Converts a parsed Markdown table to a table description.
    /// </summary>
    /// <param name="source">The parsed table.</param>
    /// <returns>The table description.</returns>
    public static WordTableSpec ToTableSpec(RichTextTable source)
    {
        var spec = new WordTableSpec();

        if (source is null)
        {
            return spec;
        }

        var columnCount = Math.Max(1, source.ColumnCount);

        for (var index = 0; index < columnCount; index++)
        {
            spec.Columns.Add(new WordTableColumn
            {
                Header = index < source.Header.Cells.Count ? ToMarkdown(source.Header.Cells[index].Spans) : string.Empty,
            });
        }

        foreach (var row in source.Rows)
        {
            spec.Rows.Add([.. row.Cells.Select(cell => WordTableCell.FromText(ToMarkdown(cell.Spans)))]);
        }

        return spec;
    }

    /// <summary>
    /// Writes parsed spans back as inline Markdown, so a cell keeps its emphasis and links.
    /// </summary>
    /// <param name="spans">The spans.</param>
    /// <returns>The inline Markdown.</returns>
    public static string ToMarkdown(IEnumerable<RichTextSpan> spans)
    {
        if (spans is null)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();

        foreach (var span in spans)
        {
            var text = span.Text ?? string.Empty;

            if (span.Code)
            {
                text = "`" + text + "`";
            }

            if (span.Strikethrough)
            {
                text = "~~" + text + "~~";
            }

            if (span.Italic)
            {
                text = "*" + text + "*";
            }

            if (span.Bold)
            {
                text = "**" + text + "**";
            }

            if (!string.IsNullOrWhiteSpace(span.Link))
            {
                text = "[" + text + "](" + span.Link + ")";
            }

            builder.Append(text);
        }

        return builder.ToString();
    }

    private int AppendList(IReadOnlyList<RichTextBlock> blocks, int index, List<OpenXmlElement> elements)
    {
        var style = Style(WordStyleSheet.ListParagraph);

        // Each run of consecutive items is one list; a numbered run restarts its numbering.
        var numbered = blocks[index].Kind == RichTextBlockKind.NumberedItem;
        var numberId = WordNumbering.CreateList(_mainPart, numbered, Math.Max(1, blocks[index].Number));

        while (index < blocks.Count && blocks[index].Kind is RichTextBlockKind.BulletItem or RichTextBlockKind.NumberedItem)
        {
            var block = blocks[index];
            var isNumbered = block.Kind == RichTextBlockKind.NumberedItem;

            if (isNumbered != numbered && block.Level == 0)
            {
                numbered = isNumbered;
                numberId = WordNumbering.CreateList(_mainPart, numbered, Math.Max(1, block.Number));
            }

            var paragraph = ListItem(numberId, Math.Clamp(block.Level, 0, 8), style);

            WordInlineWriter.AppendSpans(paragraph, block.Spans, _part);
            elements.Add(paragraph);
            index++;
        }

        return index;
    }

    private static Paragraph ListItem(int numberId, int level, string styleId)
    {
        return new Paragraph(new ParagraphProperties
        {
            ParagraphStyleId = new ParagraphStyleId { Val = styleId },
            NumberingProperties = new NumberingProperties(
                new NumberingLevelReference { Val = level },
                new NumberingId { Val = numberId }),
        });
    }
}
