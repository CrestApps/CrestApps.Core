namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// The block types a composed document understands.
/// </summary>
internal static class PdfBlockTypes
{
    /// <summary>
    /// A heading, which also becomes a bookmark and a table of contents entry.
    /// </summary>
    public const string Heading = "heading";

    /// <summary>
    /// A paragraph of body text with inline Markdown.
    /// </summary>
    public const string Paragraph = "paragraph";

    /// <summary>
    /// A run of Markdown or HTML parsed into headings, lists, quotes, code, rules and tables.
    /// </summary>
    public const string Markdown = "markdown";

    /// <summary>
    /// A bulleted or numbered list.
    /// </summary>
    public const string List = "list";

    /// <summary>
    /// A table.
    /// </summary>
    public const string Table = "table";

    /// <summary>
    /// An image.
    /// </summary>
    public const string Image = "image";

    /// <summary>
    /// A chart.
    /// </summary>
    public const string Chart = "chart";

    /// <summary>
    /// A forced page break.
    /// </summary>
    public const string PageBreak = "page_break";

    /// <summary>
    /// Vertical white space.
    /// </summary>
    public const string Spacer = "spacer";

    /// <summary>
    /// A horizontal rule.
    /// </summary>
    public const string Rule = "rule";

    /// <summary>
    /// A block quotation.
    /// </summary>
    public const string Quote = "quote";

    /// <summary>
    /// Preformatted code.
    /// </summary>
    public const string Code = "code";

    /// <summary>
    /// A shaded box with an optional title, for notes and warnings.
    /// </summary>
    public const string Callout = "callout";

    /// <summary>
    /// Label/value pairs printed as a borderless two-column table, for invoice headers and fact sheets.
    /// </summary>
    public const string KeyValue = "key_value";

    /// <summary>
    /// Lines to sign on, each with a label under it.
    /// </summary>
    public const string SignatureLines = "signature_lines";

    /// <summary>
    /// Every block type, in the order the tool schema lists them.
    /// </summary>
    public static readonly string[] All =
    [
        Heading,
        Paragraph,
        Markdown,
        List,
        Table,
        Image,
        Chart,
        PageBreak,
        Spacer,
        Rule,
        Quote,
        Code,
        Callout,
        KeyValue,
        SignatureLines,
    ];
}
