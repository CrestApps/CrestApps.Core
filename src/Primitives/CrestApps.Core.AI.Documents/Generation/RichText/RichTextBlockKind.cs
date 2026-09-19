namespace CrestApps.Core.AI.Documents.Generation.RichText;

/// <summary>
/// The kind of block in a parsed document.
/// </summary>
public enum RichTextBlockKind
{
    /// <summary>
    /// A run of body text.
    /// </summary>
    Paragraph,

    /// <summary>
    /// A section heading. <see cref="RichTextBlock.Level"/> carries its depth, 1 through 6.
    /// </summary>
    Heading,

    /// <summary>
    /// An item in a bulleted list.
    /// </summary>
    BulletItem,

    /// <summary>
    /// An item in a numbered list. <see cref="RichTextBlock.Number"/> carries its position.
    /// </summary>
    NumberedItem,

    /// <summary>
    /// A quoted passage.
    /// </summary>
    Quote,

    /// <summary>
    /// A preformatted code block. The content is in <see cref="RichTextBlock.Text"/> rather than in
    /// spans, because code is never re-formatted.
    /// </summary>
    Code,

    /// <summary>
    /// A horizontal rule separating sections.
    /// </summary>
    HorizontalRule,

    /// <summary>
    /// A table. The content is in <see cref="RichTextBlock.Table"/>.
    /// </summary>
    Table,
}
