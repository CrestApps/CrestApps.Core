namespace CrestApps.Core.AI.Documents.Pdf;

/// <summary>
/// Well-known registered names for the tools of the system PDF agent. The tools are hidden from the
/// user-facing tool picker; they are only included when a profile (the system PDF agent) references them by
/// name.
/// </summary>
public static class PdfToolNames
{
    /// <summary>
    /// Lists the PDFs in the conversation, or describes one: pages, size, metadata, bookmarks, form fields,
    /// attachments, layers, signatures and security.
    /// </summary>
    public const string GetPdfInfo = "get_pdf_info";

    /// <summary>
    /// Starts a new composed PDF with its page setup, theme and first content.
    /// </summary>
    public const string CreatePdf = "create_pdf";

    /// <summary>
    /// Adds, inserts, replaces or removes content blocks in a composed PDF.
    /// </summary>
    public const string AddPdfContent = "add_pdf_content";

    /// <summary>
    /// Records how a composed PDF looks: theme, page layout, running heads, page numbers, cover, table of
    /// contents and watermark.
    /// </summary>
    public const string FormatPdf = "format_pdf";

    /// <summary>
    /// Shows PDF pages as pictures in the conversation.
    /// </summary>
    public const string PreviewPdf = "preview_pdf";

    /// <summary>
    /// Writes a PDF as a download.
    /// </summary>
    public const string ExportPdf = "export_pdf";

    /// <summary>
    /// Turns a tabular file, a Word document, a deck, text or images into a composed PDF.
    /// </summary>
    public const string ConvertToPdf = "convert_to_pdf";

    /// <summary>
    /// Merges, splits, extracts, reorders, rotates, deletes, inserts and crops pages, and stamps watermarks,
    /// page numbers and running text onto existing pages.
    /// </summary>
    public const string EditPdfPages = "edit_pdf_pages";

    /// <summary>
    /// Fills the form fields of a PDF.
    /// </summary>
    public const string FillPdfForm = "fill_pdf_form";

    /// <summary>
    /// Extracts text by page, optionally with positions and fonts.
    /// </summary>
    public const string ExtractPdfText = "extract_pdf_text";

    /// <summary>
    /// Detects and extracts tables, including tables that run across pages.
    /// </summary>
    public const string ExtractPdfTables = "extract_pdf_tables";

    /// <summary>
    /// Lists, shows and keeps the images embedded in a PDF.
    /// </summary>
    public const string ExtractPdfImages = "extract_pdf_images";

    /// <summary>
    /// Returns a PDF's logical structure: headings, paragraphs, lists, tables and figures in reading order.
    /// </summary>
    public const string ExtractPdfStructure = "extract_pdf_structure";

    /// <summary>
    /// Lists hyperlinks and internal links with their targets and pages.
    /// </summary>
    public const string ExtractPdfLinks = "extract_pdf_links";

    /// <summary>
    /// Finds text or patterns and returns matches with pages, snippets and positions.
    /// </summary>
    public const string SearchPdf = "search_pdf";

    /// <summary>
    /// Returns everything on one page: text blocks, fonts, images, links, annotations and form fields.
    /// </summary>
    public const string GetPdfPageContent = "get_pdf_page_content";

    /// <summary>
    /// Recognises text on scanned pages with a vision model, optionally adding a searchable text layer.
    /// </summary>
    public const string OcrPdf = "ocr_pdf";

    /// <summary>
    /// Describes a page's layout: columns, blocks, figures, captions and reading order.
    /// </summary>
    public const string AnalyzePdfLayout = "analyze_pdf_layout";

    /// <summary>
    /// Describes embedded images, charts and diagrams with a vision model.
    /// </summary>
    public const string AnalyzePdfImages = "analyze_pdf_images";

    /// <summary>
    /// Detects the languages a PDF is written in, overall and by page.
    /// </summary>
    public const string DetectPdfLanguage = "detect_pdf_language";

    /// <summary>
    /// Summarizes a PDF or selected pages, with page references.
    /// </summary>
    public const string SummarizePdf = "summarize_pdf";

    /// <summary>
    /// Finds the passages that answer a question, with page citations.
    /// </summary>
    public const string AskPdf = "ask_pdf";

    /// <summary>
    /// Compares two or more PDFs and reports additions, removals and changes.
    /// </summary>
    public const string ComparePdfs = "compare_pdfs";

    /// <summary>
    /// Extracts named entities such as people, organizations, dates, amounts and identifiers.
    /// </summary>
    public const string ExtractPdfEntities = "extract_pdf_entities";

    /// <summary>
    /// Extracts user-specified information into a structured JSON result.
    /// </summary>
    public const string ExtractPdfData = "extract_pdf_data";

    /// <summary>
    /// Classifies a PDF by type, topic or user-defined category.
    /// </summary>
    public const string ClassifyPdf = "classify_pdf";

    /// <summary>
    /// Builds a PDF's outline from its bookmarks or headings.
    /// </summary>
    public const string GeneratePdfOutline = "generate_pdf_outline";

    /// <summary>
    /// Finds related information and conflicting statements across PDFs.
    /// </summary>
    public const string CrossReferencePdfs = "cross_reference_pdfs";

    /// <summary>
    /// Replaces, adds or removes text and images on existing pages.
    /// </summary>
    public const string EditPdfContent = "edit_pdf_content";

    /// <summary>
    /// Lists, adds, updates and removes annotations: notes, highlights, shapes, stamps and free text.
    /// </summary>
    public const string ManagePdfAnnotations = "manage_pdf_annotations";

    /// <summary>
    /// Adds, replaces or generates bookmarks.
    /// </summary>
    public const string AddPdfBookmarks = "add_pdf_bookmarks";

    /// <summary>
    /// Updates title, author, subject, keywords, language and custom properties.
    /// </summary>
    public const string EditPdfMetadata = "edit_pdf_metadata";

    /// <summary>
    /// Lists, adds, extracts and removes embedded files.
    /// </summary>
    public const string ManagePdfAttachments = "manage_pdf_attachments";

    /// <summary>
    /// Lists optional content layers and shows, hides or removes them.
    /// </summary>
    public const string ManagePdfLayers = "manage_pdf_layers";

    /// <summary>
    /// Adds web links and links to pages.
    /// </summary>
    public const string AddPdfLinks = "add_pdf_links";

    /// <summary>
    /// Flattens form fields and annotations into page content.
    /// </summary>
    public const string FlattenPdf = "flatten_pdf";

    /// <summary>
    /// Lists form fields with their types, values, options and constraints.
    /// </summary>
    public const string GetPdfFormFields = "get_pdf_form_fields";

    /// <summary>
    /// Adds, changes, renames and removes form fields, including signature fields.
    /// </summary>
    public const string EditPdfForm = "edit_pdf_form";

    /// <summary>
    /// Checks form values against required fields, formats and options.
    /// </summary>
    public const string ValidatePdfForm = "validate_pdf_form";

    /// <summary>
    /// Applies a digital signature with the signing identity the host configured.
    /// </summary>
    public const string SignPdf = "sign_pdf";

    /// <summary>
    /// Verifies digital signatures and whether the document changed after signing.
    /// </summary>
    public const string VerifyPdfSignature = "verify_pdf_signature";

    /// <summary>
    /// Permanently removes text, images and areas from pages.
    /// </summary>
    public const string RedactPdf = "redact_pdf";

    /// <summary>
    /// Finds personal, financial and other sensitive data before redaction.
    /// </summary>
    public const string FindPdfSensitiveData = "find_pdf_sensitive_data";

    /// <summary>
    /// Encrypts a PDF with passwords and permissions.
    /// </summary>
    public const string ProtectPdf = "protect_pdf";

    /// <summary>
    /// Removes a PDF's password protection when its password is supplied.
    /// </summary>
    public const string RemovePdfSecurity = "remove_pdf_security";

    /// <summary>
    /// Removes metadata, scripts, attachments, hidden content and other artifacts.
    /// </summary>
    public const string SanitizePdf = "sanitize_pdf";

    /// <summary>
    /// Checks tagging, reading order, language, alternative text and other accessibility requirements.
    /// </summary>
    public const string CheckPdfAccessibility = "check_pdf_accessibility";

    /// <summary>
    /// Improves the accessibility settings and tags a PDF carries.
    /// </summary>
    public const string TagPdfAccessibility = "tag_pdf_accessibility";

    /// <summary>
    /// Checks a PDF against the requirements of PDF/A and PDF/UA.
    /// </summary>
    public const string ValidatePdfCompliance = "validate_pdf_compliance";

    /// <summary>
    /// Compresses and optimizes a PDF, reporting the size before and after.
    /// </summary>
    public const string OptimizePdf = "optimize_pdf";

    /// <summary>
    /// Converts a PDF to Word, Excel, Markdown, HTML, text, JSON, CSV or page images.
    /// </summary>
    public const string ConvertFromPdf = "convert_from_pdf";

    /// <summary>
    /// Checks a PDF's integrity: structure, objects, content streams and fonts.
    /// </summary>
    public const string ValidatePdf = "validate_pdf";

    /// <summary>
    /// Checks rendering, content overflow, fonts, links and layout.
    /// </summary>
    public const string CheckPdfQuality = "check_pdf_quality";
}
