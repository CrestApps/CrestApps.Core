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
    /// Sets page size, orientation, margins and columns.
    /// </summary>
    public const string SetWordPageLayout = "set_word_page_layout";

    /// <summary>
    /// Sets the page background color.
    /// </summary>
    public const string SetWordPageBackground = "set_word_page_background";

    /// <summary>
    /// Sets headers and footers with text, images and fields.
    /// </summary>
    public const string AddWordHeaderFooter = "add_word_header_footer";

    /// <summary>
    /// Adds page numbers, page counts and numbering formats.
    /// </summary>
    public const string AddWordPageNumbers = "add_word_page_numbers";

    /// <summary>
    /// Sets page borders.
    /// </summary>
    public const string AddWordPageBorders = "add_word_page_borders";

    /// <summary>
    /// Adds a link to a web address or to a place in the document.
    /// </summary>
    public const string AddWordHyperlink = "add_word_hyperlink";

    /// <summary>
    /// Creates, renames and removes bookmarks.
    /// </summary>
    public const string AddWordBookmark = "add_word_bookmark";

    /// <summary>
    /// Adds a numbered caption to a table, figure or other element.
    /// </summary>
    public const string AddWordCaption = "add_word_caption";

    /// <summary>
    /// Inserts a reference to a heading, figure, table or bookmark.
    /// </summary>
    public const string AddWordCrossReference = "add_word_cross_reference";

    /// <summary>
    /// Inserts a table.
    /// </summary>
    public const string AddWordTable = "add_word_table";

    /// <summary>
    /// Updates table rows, columns and cells.
    /// </summary>
    public const string UpdateWordTable = "update_word_table";

    /// <summary>
    /// Formats a table: style, borders, shading, alignment and conditional formatting.
    /// </summary>
    public const string FormatWordTable = "format_word_table";

    /// <summary>
    /// Merges table cells.
    /// </summary>
    public const string MergeWordTableCells = "merge_word_table_cells";

    /// <summary>
    /// Splits a table cell.
    /// </summary>
    public const string SplitWordTableCell = "split_word_table_cell";

    /// <summary>
    /// Inserts an image.
    /// </summary>
    public const string AddWordImage = "add_word_image";

    /// <summary>
    /// Replaces or changes an image.
    /// </summary>
    public const string UpdateWordImage = "update_word_image";

    /// <summary>
    /// Inserts a chart.
    /// </summary>
    public const string AddWordChart = "add_word_chart";

    /// <summary>
    /// Updates a chart's data, type, labels and formatting.
    /// </summary>
    public const string UpdateWordChart = "update_word_chart";

    /// <summary>
    /// Inserts a shape or a text box.
    /// </summary>
    public const string AddWordShape = "add_word_shape";

    /// <summary>
    /// Inserts a diagram, or updates the text of an existing SmartArt graphic.
    /// </summary>
    public const string AddWordSmartArt = "add_word_smartart";

    /// <summary>
    /// Extracts text, optionally with formatting.
    /// </summary>
    public const string ExtractWordText = "extract_word_text";

    /// <summary>
    /// Returns headings, paragraphs, lists, tables and images in reading order.
    /// </summary>
    public const string ExtractWordStructure = "extract_word_structure";

    /// <summary>
    /// Extracts tables as rows and columns.
    /// </summary>
    public const string ExtractWordTables = "extract_word_tables";

    /// <summary>
    /// Lists, shows and keeps embedded images.
    /// </summary>
    public const string ExtractWordImages = "extract_word_images";

    /// <summary>
    /// Lists hyperlinks, bookmarks and internal references.
    /// </summary>
    public const string ExtractWordLinks = "extract_word_links";

    /// <summary>
    /// Lists fields and their codes.
    /// </summary>
    public const string ExtractWordFields = "extract_word_fields";

    /// <summary>
    /// Lists comments with their authors, dates and anchored text.
    /// </summary>
    public const string ExtractWordComments = "extract_word_comments";

    /// <summary>
    /// Lists tracked insertions, deletions and formatting changes.
    /// </summary>
    public const string ExtractWordRevisions = "extract_word_revisions";

    /// <summary>
    /// Finds text and returns matches with their context.
    /// </summary>
    public const string SearchWordDocument = "search_word_document";

    /// <summary>
    /// Returns the content and formatting of one element or section.
    /// </summary>
    public const string GetWordContent = "get_word_content";

    /// <summary>
    /// Returns metadata, page setup, counts, styles and fonts.
    /// </summary>
    public const string GetWordDocumentInfo = "get_word_document_info";

    /// <summary>
    /// Detects the languages a document is written in.
    /// </summary>
    public const string DetectWordLanguage = "detect_word_language";

    /// <summary>
    /// Summarizes a document or selected sections.
    /// </summary>
    public const string SummarizeWordDocument = "summarize_word_document";

    /// <summary>
    /// Answers a question with references to the relevant paragraphs.
    /// </summary>
    public const string AskWordDocument = "ask_word_document";

    /// <summary>
    /// Compares documents and reports additions, removals and changes.
    /// </summary>
    public const string CompareWordDocuments = "compare_word_documents";

    /// <summary>
    /// Extracts named entities.
    /// </summary>
    public const string ExtractWordEntities = "extract_word_entities";

    /// <summary>
    /// Extracts requested information into structured JSON.
    /// </summary>
    public const string ExtractWordData = "extract_word_data";

    /// <summary>
    /// Classifies a document by type, topic or category.
    /// </summary>
    public const string ClassifyWordDocument = "classify_word_document";

    /// <summary>
    /// Generates a logical outline of a document.
    /// </summary>
    public const string GenerateWordOutline = "generate_word_outline";

    /// <summary>
    /// Rewrites selected content in place.
    /// </summary>
    public const string RewriteWordContent = "rewrite_word_content";

    /// <summary>
    /// Improves clarity, grammar, tone and readability in place.
    /// </summary>
    public const string ImproveWordContent = "improve_word_content";

    /// <summary>
    /// Translates content while keeping its structure and formatting.
    /// </summary>
    public const string TranslateWordContent = "translate_word_content";

    /// <summary>
    /// Finds related information and conflicting statements across documents.
    /// </summary>
    public const string CrossReferenceWordDocuments = "cross_reference_word_documents";

    /// <summary>
    /// Adds a comment anchored to text or an element.
    /// </summary>
    public const string AddWordComment = "add_word_comment";

    /// <summary>
    /// Changes a comment's text or resolves it.
    /// </summary>
    public const string UpdateWordComment = "update_word_comment";

    /// <summary>
    /// Removes comments.
    /// </summary>
    public const string DeleteWordComment = "delete_word_comment";

    /// <summary>
    /// Replies to a comment.
    /// </summary>
    public const string ReplyToWordComment = "reply_to_word_comment";

    /// <summary>
    /// Turns tracked changes on or off.
    /// </summary>
    public const string EnableWordTrackChanges = "enable_word_track_changes";

    /// <summary>
    /// Accepts tracked changes.
    /// </summary>
    public const string AcceptWordChanges = "accept_word_changes";

    /// <summary>
    /// Rejects tracked changes.
    /// </summary>
    public const string RejectWordChanges = "reject_word_changes";

    /// <summary>
    /// Lists tracked changes with the ids to accept or reject them by.
    /// </summary>
    public const string GetWordChanges = "get_word_changes";

    /// <summary>
    /// Inserts or deletes text as a tracked change.
    /// </summary>
    public const string AddWordTrackedChange = "add_word_tracked_change";

    /// <summary>
    /// Sets editing restrictions.
    /// </summary>
    public const string ManageWordProtection = "manage_word_protection";

    /// <summary>
    /// Converts Markdown, HTML, text, PDF, Excel, PowerPoint or CSV into a Word document.
    /// </summary>
    public const string ConvertToWord = "convert_to_word";

    /// <summary>
    /// Converts a Word document to PDF, HTML, Markdown, text, JSON or page images.
    /// </summary>
    public const string ConvertFromWord = "convert_from_word";

    /// <summary>
    /// Writes the document as a <c>.docx</c> download.
    /// </summary>
    public const string ExportWord = "export_word";

    /// <summary>
    /// Exports selected sections or elements as a separate Word document.
    /// </summary>
    public const string ExportWordContent = "export_word_content";

    /// <summary>
    /// Loads an uploaded Word document into the workspace.
    /// </summary>
    public const string ImportWord = "import_word";

    /// <summary>
    /// Makes a separate working copy of a document.
    /// </summary>
    public const string DuplicateWordDocument = "duplicate_word_document";

    /// <summary>
    /// Shows pages as pictures in the conversation.
    /// </summary>
    public const string PreviewWord = "preview_word";

    /// <summary>
    /// Draws selected pages at a chosen size and format.
    /// </summary>
    public const string RenderWordPages = "render_word_pages";

    /// <summary>
    /// Returns the page count and where each page starts.
    /// </summary>
    public const string GetWordPageCount = "get_word_page_count";

    /// <summary>
    /// Shows the page an element is on, with the element outlined.
    /// </summary>
    public const string PreviewWordContent = "preview_word_content";

    /// <summary>
    /// Returns page dimensions, fonts and layout details.
    /// </summary>
    public const string GetWordRenderingInfo = "get_word_rendering_info";

    /// <summary>
    /// Checks package integrity, the Open XML schema and relationships.
    /// </summary>
    public const string ValidateWordDocument = "validate_word_document";

    /// <summary>
    /// Checks the layout against what the document intends.
    /// </summary>
    public const string ValidateWordLayout = "validate_word_layout";

    /// <summary>
    /// Detects blank pages, clipped content and overlapping elements.
    /// </summary>
    public const string CheckWordRendering = "check_word_rendering";

    /// <summary>
    /// Lists fonts and whether they are standard, embedded or substituted.
    /// </summary>
    public const string CheckWordFonts = "check_word_fonts";

    /// <summary>
    /// Checks links, bookmarks and cross-reference targets.
    /// </summary>
    public const string CheckWordLinks = "check_word_links";

    /// <summary>
    /// Detects content that runs past the page or its container.
    /// </summary>
    public const string CheckWordContentOverflow = "check_word_content_overflow";

    /// <summary>
    /// Checks heading order, alternative text, table headers and reading order.
    /// </summary>
    public const string CheckWordAccessibility = "check_word_accessibility";

    /// <summary>
    /// Validates the accessibility requirements a document must meet and reports pass or fail.
    /// </summary>
    public const string ValidateWordAccessibility = "validate_word_accessibility";

    /// <summary>
    /// Checks fields, cross-references and dynamic content for broken references.
    /// </summary>
    public const string ValidateWordFields = "validate_word_fields";
}
