namespace CrestApps.Core.AI.Documents.Word.Reading;

/// <summary>
/// What a block of a document is.
/// </summary>
internal enum WordBlockKind
{
    /// <summary>
    /// Body text.
    /// </summary>
    Paragraph,

    /// <summary>
    /// The document title.
    /// </summary>
    Title,

    /// <summary>
    /// A subtitle.
    /// </summary>
    Subtitle,

    /// <summary>
    /// A heading with an outline level.
    /// </summary>
    Heading,

    /// <summary>
    /// An item of a bulleted list.
    /// </summary>
    BulletItem,

    /// <summary>
    /// An item of a numbered list.
    /// </summary>
    NumberedItem,

    /// <summary>
    /// A quotation.
    /// </summary>
    Quote,

    /// <summary>
    /// A line of a code block.
    /// </summary>
    Code,

    /// <summary>
    /// A caption of a table or figure.
    /// </summary>
    Caption,

    /// <summary>
    /// A table.
    /// </summary>
    Table,

    /// <summary>
    /// A paragraph holding only a picture.
    /// </summary>
    Image,

    /// <summary>
    /// A paragraph holding only a chart.
    /// </summary>
    Chart,

    /// <summary>
    /// A paragraph holding only a shape, text box or drawing group.
    /// </summary>
    Shape,

    /// <summary>
    /// A paragraph holding only a SmartArt graphic.
    /// </summary>
    SmartArt,

    /// <summary>
    /// A table of contents.
    /// </summary>
    TableOfContents,

    /// <summary>
    /// An index.
    /// </summary>
    Index,

    /// <summary>
    /// A paragraph holding only a page break.
    /// </summary>
    PageBreak,

    /// <summary>
    /// An empty paragraph.
    /// </summary>
    Empty,

    /// <summary>
    /// A content control or other container.
    /// </summary>
    ContentControl,

    /// <summary>
    /// Anything else, such as embedded content from another format.
    /// </summary>
    Other,
}
