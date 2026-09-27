namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// The roles a block of text plays on a page.
/// </summary>
internal static class PdfLayoutRoles
{
    /// <summary>
    /// A heading: text set noticeably larger, or bold on its own line, and short.
    /// </summary>
    public const string Heading = "heading";

    /// <summary>
    /// Running text.
    /// </summary>
    public const string Paragraph = "paragraph";

    /// <summary>
    /// An item of a bulleted or numbered list.
    /// </summary>
    public const string ListItem = "list_item";

    /// <summary>
    /// The caption of a figure or a table next to it.
    /// </summary>
    public const string Caption = "caption";

    /// <summary>
    /// A running head repeated at the top of pages.
    /// </summary>
    public const string Header = "header";

    /// <summary>
    /// A running foot or page number repeated at the bottom of pages.
    /// </summary>
    public const string Footer = "footer";

    /// <summary>
    /// Text inside a table.
    /// </summary>
    public const string Table = "table";

    /// <summary>
    /// Text inside a figure, such as a chart's labels.
    /// </summary>
    public const string FigureText = "figure_text";
}
