using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Reading;

/// <summary>
/// Reads a document's body as blocks — headings, paragraphs, list items, tables, pictures, a table of contents —
/// each with the id tools name it by and the section it is in.
/// </summary>
internal static class WordBlockReader
{
    /// <summary>
    /// Reads the body's blocks in reading order. A content control that wraps paragraphs or tables, as templates
    /// do, is read as the blocks inside it, so each can be named and edited by its own id; a table of contents
    /// or an index in a content control is read as one block.
    /// </summary>
    /// <param name="package">The document. Its paragraphs are given ids if they have none.</param>
    /// <returns>The blocks.</returns>
    public static List<WordBlock> Read(WordPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        _ = package.Ids;

        var styles = new WordStyleIndex(package.MainPart);
        var blocks = new List<WordBlock>();
        var section = 1;

        foreach (var element in package.Body.ChildElements)
        {
            foreach (var block in ReadBlocks(element, styles, control: null))
            {
                block.Section = section;
                block.Index = blocks.Count;
                blocks.Add(block);

                if (block.EndsSection)
                {
                    section++;
                }
            }
        }

        return blocks;
    }

    /// <summary>
    /// Returns whether a content control is read as the blocks inside it rather than as one block: one that
    /// wraps paragraphs or tables and is not a table of contents or an index.
    /// </summary>
    /// <param name="control">The content control.</param>
    /// <returns><see langword="true"/> when the blocks inside it are named by their own ids.</returns>
    public static bool IsTransparent(SdtBlock control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return KindOf(control) == WordBlockKind.ContentControl &&
            control.SdtContentBlock?.ChildElements.Any(child => child is Paragraph or Table or SdtBlock) == true;
    }

    private static IEnumerable<WordBlock> ReadBlocks(OpenXmlElement element, WordStyleIndex styles, string control)
    {
        if (element is SdtBlock content && IsTransparent(content))
        {
            var name = NameOf(content);

            foreach (var child in content.SdtContentBlock.ChildElements)
            {
                foreach (var inner in ReadBlocks(child, styles, control ?? name))
                {
                    yield return inner;
                }
            }

            yield break;
        }

        var block = ReadBlock(element, styles);

        if (block is not null)
        {
            block.ContentControl = control;

            yield return block;
        }
    }

    private static string NameOf(SdtBlock control)
    {
        // A content control is named by its title, then its tag; one with neither is still marked as one.
        var properties = control.SdtProperties;
        var name = properties?.GetFirstChild<SdtAlias>()?.Val?.Value ?? properties?.GetFirstChild<Tag>()?.Val?.Value;

        return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
    }

    private static WordBlockKind KindOf(SdtBlock control)
    {
        var gallery = control.SdtProperties?.Descendants<DocPartGallery>().FirstOrDefault()?.Val?.Value;

        if (string.Equals(gallery, "Table of Contents", StringComparison.OrdinalIgnoreCase) || HasField(control, "TOC"))
        {
            return WordBlockKind.TableOfContents;
        }

        return HasField(control, "INDEX") ? WordBlockKind.Index : WordBlockKind.ContentControl;
    }

    /// <summary>
    /// Reads one block.
    /// </summary>
    /// <param name="element">A child of the body, or a paragraph or table nested in one.</param>
    /// <param name="styles">The document's styles.</param>
    /// <returns>The block, or <see langword="null"/> for an element that is not content.</returns>
    public static WordBlock ReadBlock(OpenXmlElement element, WordStyleIndex styles)
    {
        ArgumentNullException.ThrowIfNull(styles);

        return element switch
        {
            Paragraph paragraph => ReadParagraph(paragraph, styles),
            Table table => ReadTable(table),
            SdtBlock control => ReadContentControl(control),
            AltChunk => new WordBlock { Kind = WordBlockKind.Other, Element = element, Text = "(embedded content from another format)" },
            CustomXmlBlock custom => new WordBlock
            {
                Id = WordParagraphIds.Of(custom),
                Kind = WordBlockKind.ContentControl,
                Element = custom,
                Text = WordText.Of(custom),
            },
            _ => null,
        };
    }

    /// <summary>
    /// Reads a paragraph as a block.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="styles">The document's styles.</param>
    /// <returns>The block.</returns>
    public static WordBlock ReadParagraph(Paragraph paragraph, WordStyleIndex styles)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(styles);

        var styleId = styles.StyleOf(paragraph);
        var text = WordText.Of(paragraph);
        var drawings = WordDrawingReader.ReadAll(paragraph);
        var block = new WordBlock
        {
            Id = WordParagraphIds.Of(paragraph),
            Element = paragraph,
            Text = text,
            StyleId = styleId,
            StyleName = styles.NameOf(styleId),
            Drawings = drawings,
            EndsSection = paragraph.ParagraphProperties?.SectionProperties is not null,
        };

        if (styles.OutlineLevelOf(paragraph) is { } outline)
        {
            block.Kind = WordBlockKind.Heading;
            block.Level = outline + 1;
        }
        else if (styles.HasStyle(paragraph, "Title"))
        {
            block.Kind = WordBlockKind.Title;
            block.Level = 0;
        }
        else if (styles.HasStyle(paragraph, "Subtitle"))
        {
            block.Kind = WordBlockKind.Subtitle;
        }
        else if (styles.TryGetList(paragraph, out var numberId, out var level))
        {
            block.Kind = styles.IsBullet(numberId, level) ? WordBlockKind.BulletItem : WordBlockKind.NumberedItem;
            block.Level = level;
        }
        else if (styles.HasStyle(paragraph, "Quote", "Intense Quote"))
        {
            block.Kind = WordBlockKind.Quote;
        }
        else if (styles.HasStyle(paragraph, "Code Block", "HTML Preformatted", "Source Code", "Code"))
        {
            block.Kind = WordBlockKind.Code;
        }
        else if (styles.HasStyle(paragraph, "caption"))
        {
            block.Kind = WordBlockKind.Caption;
        }
        else if (string.IsNullOrWhiteSpace(text) && drawings.Count > 0)
        {
            block.Kind = drawings[0].Kind switch
            {
                WordDrawingKind.Picture => WordBlockKind.Image,
                WordDrawingKind.Chart => WordBlockKind.Chart,
                WordDrawingKind.SmartArt => WordBlockKind.SmartArt,
                _ => WordBlockKind.Shape,
            };
        }
        else if (string.IsNullOrWhiteSpace(text) && paragraph.Descendants<Break>().Any(item => item.Type?.Value == BreakValues.Page))
        {
            block.Kind = WordBlockKind.PageBreak;
        }
        else if (HasField(paragraph, "TOC"))
        {
            block.Kind = WordBlockKind.TableOfContents;
        }
        else if (HasField(paragraph, "INDEX"))
        {
            block.Kind = WordBlockKind.Index;
        }
        else if (string.IsNullOrWhiteSpace(text) && paragraph.Descendants<Picture>().Any())
        {
            block.Kind = WordBlockKind.Shape;
        }
        else
        {
            block.Kind = string.IsNullOrWhiteSpace(text) ? WordBlockKind.Empty : WordBlockKind.Paragraph;
        }

        return block;
    }

    /// <summary>
    /// Returns whether an element holds a field of a kind, such as <c>TOC</c> or <c>INDEX</c>.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="fieldName">The field name.</param>
    /// <returns><see langword="true"/> when a field of the kind starts in the element.</returns>
    public static bool HasField(OpenXmlElement element, string fieldName)
    {
        return element.Descendants<FieldCode>().Any(code => StartsWithField(code.Text, fieldName)) ||
            element.Descendants<SimpleField>().Any(field => StartsWithField(field.Instruction?.Value, fieldName));
    }

    private static bool StartsWithField(string instruction, string fieldName)
    {
        var trimmed = (instruction ?? string.Empty).TrimStart();

        return trimmed.StartsWith(fieldName, StringComparison.OrdinalIgnoreCase) &&
            (trimmed.Length == fieldName.Length || char.IsWhiteSpace(trimmed[fieldName.Length]) || trimmed[fieldName.Length] == '\\');
    }

    private static WordBlock ReadTable(Table table)
    {
        var rows = table.Elements<TableRow>().ToList();
        var columns = table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().Count() ?? 0;

        if (columns == 0 && rows.Count > 0)
        {
            columns = rows.Max(row => row.Elements<TableCell>().Sum(cell => cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1));
        }

        var header = rows.Count == 0
            ? string.Empty
            : string.Join(" | ", rows[0].Elements<TableCell>().Select(cell => WordText.Clip(WordText.OfCell(cell), 30)));

        return new WordBlock
        {
            Id = WordParagraphIds.Of(table),
            Kind = WordBlockKind.Table,
            Element = table,
            Text = header,
            Rows = rows.Count,
            Columns = columns,
            StyleId = table.GetFirstChild<TableProperties>()?.TableStyle?.Val?.Value,
            Drawings = WordDrawingReader.ReadAll(table),
        };
    }

    private static WordBlock ReadContentControl(SdtBlock control)
    {
        var kind = KindOf(control);
        var isToc = kind == WordBlockKind.TableOfContents;
        var isIndex = kind == WordBlockKind.Index;
        var alias = control.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value;

        return new WordBlock
        {
            Id = WordParagraphIds.Of(control),
            Kind = isToc ? WordBlockKind.TableOfContents : isIndex ? WordBlockKind.Index : WordBlockKind.ContentControl,
            Element = control,
            Text = isToc || isIndex ? WordText.Clip(WordText.Of(control), 200) : WordText.Of(control),
            StyleName = alias,
            Drawings = WordDrawingReader.ReadAll(control),
            EndsSection = control.Descendants<SectionProperties>().Any(),
        };
    }
}
