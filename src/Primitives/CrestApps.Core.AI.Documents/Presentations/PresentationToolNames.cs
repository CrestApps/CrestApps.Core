namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// Well-known names of the tools the system presentation agent uses. They are hidden from the user-facing
/// tool picker and only included for a profile that names them, which is the presentation agent.
/// </summary>
public static class PresentationToolNames
{
    /// <summary>
    /// Lists the decks and each slide with its layout, title, elements and notes.
    /// </summary>
    public const string GetPresentationOutline = "get_presentation_outline";

    /// <summary>
    /// Describes one slide completely: every element with its position, text, formatting and data.
    /// </summary>
    public const string GetSlideContent = "get_slide_content";

    /// <summary>
    /// Describes the theme, slide masters and layouts.
    /// </summary>
    public const string GetPresentationTheme = "get_presentation_theme";

    /// <summary>
    /// Finds text across slides, notes, tables and charts.
    /// </summary>
    public const string SearchPresentation = "search_presentation";

    /// <summary>
    /// Extracts text, notes, tables, charts, images or links from the deck as structured data.
    /// </summary>
    public const string ExtractPresentationContent = "extract_presentation_content";

    /// <summary>
    /// Starts a new deck, from a theme preset or an uploaded template.
    /// </summary>
    public const string CreatePresentation = "create_presentation";

    /// <summary>
    /// Adds slides.
    /// </summary>
    public const string AddSlide = "add_slide";

    /// <summary>
    /// Copies a slide.
    /// </summary>
    public const string DuplicateSlide = "duplicate_slide";

    /// <summary>
    /// Removes slides.
    /// </summary>
    public const string DeleteSlide = "delete_slide";

    /// <summary>
    /// Moves a slide.
    /// </summary>
    public const string MoveSlide = "move_slide";

    /// <summary>
    /// Changes slides' titles, body text, notes, visibility and transitions.
    /// </summary>
    public const string UpdateSlide = "update_slide";

    /// <summary>
    /// Places text boxes, shapes, lines, tables, charts, pictures, icons and media on a slide.
    /// </summary>
    public const string InsertSlideElement = "insert_slide_element";

    /// <summary>
    /// Changes an element's text, position, size, rotation, style, picture, alternative text or link.
    /// </summary>
    public const string UpdateSlideElement = "update_slide_element";

    /// <summary>
    /// Rewrites text across the deck while keeping its formatting, or finds and replaces.
    /// </summary>
    public const string UpdateSlideText = "update_slide_text";

    /// <summary>
    /// Copies or moves elements between slides.
    /// </summary>
    public const string CopySlideElements = "copy_slide_elements";

    /// <summary>
    /// Removes elements from a slide.
    /// </summary>
    public const string DeleteSlideElement = "delete_slide_element";

    /// <summary>
    /// Groups elements, or breaks a group apart.
    /// </summary>
    public const string GroupSlideElements = "group_slide_elements";

    /// <summary>
    /// Aligns, distributes, restacks, snaps and lays out elements.
    /// </summary>
    public const string ArrangeSlideElements = "arrange_slide_elements";

    /// <summary>
    /// Changes a table's cells, rows, columns and look.
    /// </summary>
    public const string UpdateSlideTable = "update_slide_table";

    /// <summary>
    /// Changes a chart's data, kind, titles and look.
    /// </summary>
    public const string UpdateSlideChart = "update_slide_chart";

    /// <summary>
    /// Ties a table or chart to a query over the conversation's tabular data.
    /// </summary>
    public const string LinkSlideData = "link_slide_data";

    /// <summary>
    /// Re-runs the queries behind linked tables and charts.
    /// </summary>
    public const string RefreshSlideData = "refresh_slide_data";

    /// <summary>
    /// Styles titles, text, shapes, tables, charts and backgrounds across the deck, some slides or one
    /// element, and remembers the style for new content.
    /// </summary>
    public const string FormatPresentation = "format_presentation";

    /// <summary>
    /// Rebuilds slides on another layout.
    /// </summary>
    public const string ApplySlideLayout = "apply_slide_layout";

    /// <summary>
    /// Restyles the deck after an uploaded template or another deck.
    /// </summary>
    public const string ApplyPresentationTemplate = "apply_presentation_template";

    /// <summary>
    /// Sets slide backgrounds.
    /// </summary>
    public const string SetSlideBackground = "set_slide_background";

    /// <summary>
    /// Changes the theme colours and fonts.
    /// </summary>
    public const string UpdatePresentationTheme = "update_presentation_theme";

    /// <summary>
    /// Changes a slide master or layout.
    /// </summary>
    public const string UpdateSlideMaster = "update_slide_master";

    /// <summary>
    /// Applies company branding: logo, colours, fonts and footer.
    /// </summary>
    public const string ApplyPresentationBranding = "apply_presentation_branding";

    /// <summary>
    /// Sets the footer text, slide numbers and date.
    /// </summary>
    public const string SetPresentationFooter = "set_presentation_footer";

    /// <summary>
    /// Organises slides into named sections.
    /// </summary>
    public const string UpdatePresentationSections = "update_presentation_sections";

    /// <summary>
    /// Adds an agenda slide linking to the deck's sections or slides.
    /// </summary>
    public const string AddPresentationToc = "add_presentation_toc";

    /// <summary>
    /// Generates a picture with the image model and places it on a slide.
    /// </summary>
    public const string GenerateSlideImage = "generate_slide_image";

    /// <summary>
    /// Draws an editable diagram — a process, cycle, timeline, hierarchy, pyramid, matrix or funnel — from a
    /// list of steps.
    /// </summary>
    public const string GenerateSlideDiagram = "generate_slide_diagram";

    /// <summary>
    /// Reports the deck's structure, word counts, timing and the issues most worth fixing.
    /// </summary>
    public const string AnalyzePresentation = "analyze_presentation";

    /// <summary>
    /// Checks the deck for layout, consistency, accessibility, readability, link, media and file problems.
    /// </summary>
    public const string CheckPresentation = "check_presentation";

    /// <summary>
    /// Compares two decks, or a deck with an earlier version of itself.
    /// </summary>
    public const string ComparePresentations = "compare_presentations";

    /// <summary>
    /// Shows slides as pictures in the chat.
    /// </summary>
    public const string PreviewPresentation = "preview_presentation";

    /// <summary>
    /// Writes the deck as a downloadable <c>.pptx</c> or PDF.
    /// </summary>
    public const string ExportPresentation = "export_presentation";

    /// <summary>
    /// Writes the deck's outline, notes or speaker script as a downloadable document, or its slides as images.
    /// </summary>
    public const string ExportPresentationContent = "export_presentation_content";

    /// <summary>
    /// Undoes the most recent changes to a deck.
    /// </summary>
    public const string UndoPresentationChange = "undo_presentation_change";
}
