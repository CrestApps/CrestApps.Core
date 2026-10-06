namespace CrestApps.Core.AI.Documents.Word;

/// <summary>
/// Well-known registered names for the tools of the system Word agent. The tools are hidden from the
/// user-facing tool picker; they are only included when a profile (the system Word agent) references them by
/// name.
/// </summary>
public static class WordToolNames
{
    /// <summary>
    /// Starts a new Word document with page setup, properties, theme and default styles.
    /// </summary>
    public const string CreateWordDocument = "create_word_document";

    /// <summary>
    /// Adds headings, paragraphs, lists, tables, images, charts, quotes, code blocks and page breaks.
    /// </summary>
    public const string AddWordContent = "add_word_content";

    /// <summary>
    /// Updates existing elements by id without rebuilding the document.
    /// </summary>
    public const string UpdateWordContent = "update_word_content";

    /// <summary>
    /// Removes elements or sections.
    /// </summary>
    public const string RemoveWordContent = "remove_word_content";

    /// <summary>
    /// Reorders sections, paragraphs, tables, images and other elements.
    /// </summary>
    public const string MoveWordContent = "move_word_content";

    /// <summary>
    /// Lists the conversation's Word documents, or returns one document's content and metadata.
    /// </summary>
    public const string GetWordDocument = "get_word_document";

    /// <summary>
    /// Returns the hierarchical outline of headings and sections.
    /// </summary>
    public const string GetWordDocumentOutline = "get_word_document_outline";

    /// <summary>
    /// Finds text and returns matches with their context.
    /// </summary>
    public const string SearchWordDocument = "search_word_document";

    /// <summary>
    /// Extracts the content as Markdown or text, its tables, links or pictures, optionally as a download.
    /// </summary>
    public const string ExtractWordContent = "extract_word_content";

    /// <summary>
    /// Compares documents and reports additions, removals and changes.
    /// </summary>
    public const string CompareWordDocuments = "compare_word_documents";

    /// <summary>
    /// Checks accessibility, references, links and layout, and lists the problems to fix.
    /// </summary>
    public const string CheckWordDocument = "check_word_document";

    /// <summary>
    /// Adds a section with its own page layout, headers, footers and numbering.
    /// </summary>
    public const string AddWordSection = "add_word_section";

    /// <summary>
    /// Inserts a page break or a section break.
    /// </summary>
    public const string AddWordPageBreak = "add_word_page_break";

    /// <summary>
    /// Generates a table of contents from the document's headings.
    /// </summary>
    public const string AddWordToc = "add_word_toc";

    /// <summary>
    /// Marks index entries and inserts an index.
    /// </summary>
    public const string AddWordIndex = "add_word_index";

    /// <summary>
    /// Adds a numbered caption to a table, figure or other element.
    /// </summary>
    public const string AddWordCaption = "add_word_caption";

    /// <summary>
    /// Inserts a reference to a heading, figure, table or bookmark.
    /// </summary>
    public const string AddWordCrossReference = "add_word_cross_reference";

    /// <summary>
    /// Creates, renames and removes bookmarks.
    /// </summary>
    public const string AddWordBookmark = "add_word_bookmark";

    /// <summary>
    /// Adds a link to a web address or to a place in the document.
    /// </summary>
    public const string AddWordHyperlink = "add_word_hyperlink";

    /// <summary>
    /// Updates table rows, columns and cells.
    /// </summary>
    public const string UpdateWordTable = "update_word_table";

    /// <summary>
    /// Applies document-wide formatting: theme, fonts, colors and default styles.
    /// </summary>
    public const string FormatWordDocument = "format_word_document";

    /// <summary>
    /// Formats selected paragraphs, runs, headings and other elements.
    /// </summary>
    public const string FormatWordContent = "format_word_content";

    /// <summary>
    /// Lists, creates, updates and removes styles.
    /// </summary>
    public const string ManageWordStyles = "manage_word_styles";

    /// <summary>
    /// Sets page size, orientation, margins, columns, page borders and page numbering.
    /// </summary>
    public const string SetWordPageLayout = "set_word_page_layout";

    /// <summary>
    /// Sets the page background color.
    /// </summary>
    public const string SetWordPageBackground = "set_word_page_background";

    /// <summary>
    /// Sets headers and footers with text, page numbers and other fields, and a logo.
    /// </summary>
    public const string AddWordHeaderFooter = "add_word_header_footer";

    /// <summary>
    /// Lists, adds, answers, resolves and deletes review comments.
    /// </summary>
    public const string ManageWordComments = "manage_word_comments";

    /// <summary>
    /// Turns change tracking on or off, lists tracked changes, and accepts or rejects them.
    /// </summary>
    public const string ManageWordRevisions = "manage_word_revisions";

    /// <summary>
    /// Shows pages as pictures in the conversation.
    /// </summary>
    public const string PreviewWord = "preview_word";

    /// <summary>
    /// Writes the document as a <c>.docx</c> download.
    /// </summary>
    public const string ExportWord = "export_word";

    /// <summary>
    /// Loads an uploaded Word document into the workspace.
    /// </summary>
    public const string ImportWord = "import_word";
}
