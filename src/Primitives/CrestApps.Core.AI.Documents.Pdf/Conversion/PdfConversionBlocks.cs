using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.AI.Documents.Pdf.Conversion;

/// <summary>
/// Creates the blocks a converter emits, and gathers consecutive list items into one list.
/// </summary>
internal sealed class PdfConversionBlocks
{
    private readonly List<PdfBlockDefinition> _blocks;
    private readonly List<string> _items = [];
    private bool _ordered;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfConversionBlocks"/> class.
    /// </summary>
    /// <param name="blocks">The list the blocks are added to.</param>
    public PdfConversionBlocks(List<PdfBlockDefinition> blocks)
    {
        _blocks = blocks;
    }

    /// <summary>
    /// Gets the number of blocks added so far, lists being built included.
    /// </summary>
    public int Count => _blocks.Count + (_items.Count > 0 ? 1 : 0);

    /// <summary>
    /// Adds a heading.
    /// </summary>
    /// <param name="text">The heading text, inline Markdown.</param>
    /// <param name="level">The level, 1 to 4.</param>
    public void Heading(string text, int level)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Add(new PdfBlockDefinition { Type = PdfBlockTypes.Heading, Text = text.Trim(), Level = Math.Clamp(level, 1, 4) });
    }

    /// <summary>
    /// Adds a paragraph.
    /// </summary>
    /// <param name="text">The text, inline Markdown.</param>
    /// <param name="align">The alignment, or <see langword="null"/>.</param>
    public void Paragraph(string text, string align = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Add(new PdfBlockDefinition { Type = PdfBlockTypes.Paragraph, Text = text.Trim(), Align = align });
    }

    /// <summary>
    /// Adds a list item, joining it to the list before it when that list is of the same kind.
    /// </summary>
    /// <param name="text">The item text, inline Markdown.</param>
    /// <param name="level">The nesting level, from 0.</param>
    /// <param name="ordered">Whether the list is numbered.</param>
    public void ListItem(string text, int level, bool ordered)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (_items.Count > 0 && _ordered != ordered && level == 0)
        {
            FlushList();
        }

        if (_items.Count == 0)
        {
            _ordered = ordered;
        }

        // A list block reads nesting from the leading spaces of an item.
        _items.Add(new string(' ', Math.Clamp(level, 0, 4) * 2) + text.Trim());
    }

    /// <summary>
    /// Adds a table.
    /// </summary>
    /// <param name="rows">The rows, the first being the header.</param>
    /// <param name="caption">The caption, or <see langword="null"/>.</param>
    public void Table(List<List<string>> rows, string caption = null)
    {
        var cleaned = rows?.Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell))).ToList() ?? [];

        if (cleaned.Count == 0)
        {
            return;
        }

        var columns = cleaned.Max(row => row.Count);

        // A one-column table in a word processor is usually a layout box around text, not data.
        if (columns == 1)
        {
            foreach (var row in cleaned)
            {
                Paragraph(PdfInlineMarkdown.Escape(row[0]));
            }

            return;
        }

        var table = new PdfTableDefinition { Caption = caption };
        var header = cleaned[0];

        for (var index = 0; index < columns; index++)
        {
            table.Columns.Add(new PdfTableColumnDefinition { Header = index < header.Count ? header[index]?.Trim() ?? string.Empty : string.Empty });
        }

        foreach (var row in cleaned.Skip(1))
        {
            table.Rows.Add([.. Enumerable.Range(0, columns).Select(index => index < row.Count ? row[index]?.Trim() ?? string.Empty : string.Empty)]);
        }

        Add(new PdfBlockDefinition { Type = PdfBlockTypes.Table, Table = table });
    }

    /// <summary>
    /// Adds a picture.
    /// </summary>
    /// <param name="source">The image source, such as <c>asset:img1</c>.</param>
    /// <param name="widthPoints">The width it had in the file, in points, or <see langword="null"/>.</param>
    /// <param name="altText">Its description, or <see langword="null"/>.</param>
    public void Image(string source, double? widthPoints, string altText)
    {
        Add(new PdfBlockDefinition
        {
            Type = PdfBlockTypes.Image,
            Image = new PdfImageDefinition
            {
                Source = source,
                Width = widthPoints is > 0 ? Math.Round(widthPoints.Value, 1) : null,
                AltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim(),
            },
        });
    }

    /// <summary>
    /// Adds a block as it is.
    /// </summary>
    /// <param name="block">The block.</param>
    public void Add(PdfBlockDefinition block)
    {
        FlushList();
        _blocks.Add(block);
    }

    /// <summary>
    /// Starts a new page, unless nothing has been written since the last one.
    /// </summary>
    public void PageBreak()
    {
        FlushList();

        if (_blocks.Count > 0 && _blocks[^1].Type != PdfBlockTypes.PageBreak)
        {
            _blocks.Add(new PdfBlockDefinition { Type = PdfBlockTypes.PageBreak });
        }
    }

    /// <summary>
    /// Ends the list being built.
    /// </summary>
    public void FlushList()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _blocks.Add(new PdfBlockDefinition { Type = PdfBlockTypes.List, Items = [.. _items], Ordered = _ordered });
        _items.Clear();
    }
}
