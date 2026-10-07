namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// The JSON schema fragments the Word tools share, so an argument means the same thing in every tool.
/// </summary>
internal static class WordToolSchemas
{
    /// <summary>
    /// The document a call works on.
    /// </summary>
    public const string Document = """
        "document": { "type": "string", "description": "The working document's name, or an uploaded Word file's name or id. Omit to use the active document (the one last created or edited)." }
        """;

    /// <summary>
    /// The name an edit is saved under.
    /// </summary>
    public const string SaveAs = """
        "save_as": { "type": "string", "description": "Save the result as a new working document with this name instead of changing the document in place." }
        """;

    /// <summary>
    /// Where new content goes.
    /// </summary>
    public const string Position = """
        "after": { "type": "string", "description": "Insert after the element with this id (ids come from get_word_document)." },
        "before": { "type": "string", "description": "Insert before the element with this id." },
        "at": { "type": "string", "enum": ["start", "end"], "description": "Insert at the start or end of the document when neither 'after' nor 'before' is given. Defaults to the end." }
        """;

    /// <summary>
    /// Character formatting.
    /// </summary>
    public const string RunFormat = """
        {
          "type": "object",
          "description": "Character formatting. Only what is given changes.",
          "properties": {
            "font": { "type": "string" },
            "size": { "type": "number", "description": "Points." },
            "bold": { "type": "boolean" },
            "italic": { "type": "boolean" },
            "underline": { "type": "string", "enum": ["single", "double", "dotted", "dash", "wave", "thick", "none"] },
            "strikethrough": { "type": "boolean" },
            "color": { "type": "string", "description": "Hex such as #1F4E79, or a color name." },
            "highlight": { "type": "string", "description": "yellow, green, cyan, magenta, blue, red, dark_blue, dark_cyan, dark_green, dark_magenta, dark_red, dark_yellow, dark_gray, light_gray, black, white, or none." },
            "shading": { "type": "string", "description": "Background color behind the text." },
            "superscript": { "type": "boolean" },
            "subscript": { "type": "boolean" },
            "all_caps": { "type": "boolean" },
            "small_caps": { "type": "boolean" },
            "character_spacing": { "type": "number", "description": "Extra points between characters." },
            "language": { "type": "string", "description": "Language tag such as en-US or fr-FR." }
          }
        }
        """;

    /// <summary>
    /// Paragraph formatting.
    /// </summary>
    public const string ParagraphFormat = """
        {
          "type": "object",
          "description": "Paragraph formatting. Only what is given changes. Lengths are points, or text with a unit such as 0.5in or 1cm.",
          "properties": {
            "alignment": { "type": "string", "enum": ["left", "center", "right", "justify"] },
            "space_before": { "type": "number", "description": "Points." },
            "space_after": { "type": "number", "description": "Points." },
            "line_spacing": { "type": "number", "description": "Multiple of single spacing: 1, 1.15, 1.5, 2." },
            "line_height": { "type": "number", "description": "Exact line height in points." },
            "indent_left": { "type": ["number", "string"] },
            "indent_right": { "type": ["number", "string"] },
            "first_line_indent": { "type": ["number", "string"] },
            "hanging_indent": { "type": ["number", "string"] },
            "keep_with_next": { "type": "boolean" },
            "keep_lines_together": { "type": "boolean" },
            "page_break_before": { "type": "boolean" },
            "shading": { "type": "string", "description": "Paragraph background color." },
            "border": { "type": "string", "enum": ["top", "bottom", "left", "right", "box", "none"] },
            "border_color": { "type": "string" },
            "outline_level": { "type": "integer", "description": "1-9 puts the paragraph in the outline and table of contents at that level." }
          }
        }
        """;

    /// <summary>
    /// The properties of a table, shared by table blocks and the table tools.
    /// </summary>
    public const string TableProperties = """
        "columns": { "type": "array", "description": "Column headers as strings, or objects { header, width (\"1.5in\", \"30%\" or a relative weight), alignment, format: currency|accounting|percent|number|integer|date|datetime|text or a format code like \"#,##0.0\", decimals, currency_symbol }. Numeric formats right-align and present raw values (percent takes fractions: 0.12 = 12%).", "items": {} },
        "rows": { "type": "array", "description": "Body rows: each an array of cells. A cell is a string (inline Markdown), a number (formatted by its column), or { text | value, bold, italic, color, fill, alignment, vertical_alignment, colspan, rowspan }.", "items": { "type": "array", "items": {} } },
        "source": { "type": ["object", "string"], "description": "For a table or chart: fill it from uploaded tabular data instead of rows — { sql: \"SELECT …\" (SQLite over list_tabular_data tables) | table_name, max_rows, label_column, value_columns }. For an image: the picture." },
        "header_row": { "type": "boolean", "description": "Write the column headers as a header row. Default true." },
        "repeat_header": { "type": "boolean", "description": "Repeat the header row on every page the table runs onto. Default true." },
        "table_style": { "type": "string", "description": "data (shaded header, banded rows; default), grid, light (horizontal rules), plain (no borders), or a document table style name." },
        "banded": { "type": "boolean" },
        "header_fill": { "type": "string" },
        "header_text_color": { "type": "string" },
        "band_fill": { "type": "string" },
        "border_color": { "type": "string", "description": "A table's border color, or a picture's outline color." },
        "font_size": { "type": "number" },
        "bold_first_column": { "type": "boolean" }
        """;

    /// <summary>
    /// The properties of a picture, shared by image blocks and the image tools.
    /// </summary>
    public const string ImageProperties = """
        "width": { "type": ["number", "string"], "description": "Points, or with a unit (3in, 8cm) or a percentage of the text width (50%). Give width or height to keep proportions; defaults to the natural size within the margins." },
        "height": { "type": ["number", "string"] },
        "alt_text": { "type": "string", "description": "What the picture shows, for screen readers. Always give it for pictures and charts." },
        "wrap": { "type": "string", "enum": ["inline", "square", "tight", "top_and_bottom", "behind_text", "in_front_of_text"], "description": "inline (default) sits in the text flow; the others float." },
        "offset_x": { "type": ["number", "string"], "description": "Floating only: distance from the left margin." },
        "offset_y": { "type": ["number", "string"], "description": "Floating only: distance from the top of the paragraph." },
        "crop": { "type": "object", "description": "Percent to crop from each edge: { left, top, right, bottom }." },
        "border_width": { "type": "number", "description": "Outline width in points; give border_color too." }
        """;

    /// <summary>
    /// The properties of a chart, shared by chart blocks and the chart tools.
    /// </summary>
    public const string ChartProperties = """
        "chart_type": { "type": "string", "enum": ["column", "bar", "line", "pie", "doughnut", "area", "scatter", "stacked_column", "stacked_bar", "stacked_area", "percent_column", "percent_bar"] },
        "title": { "type": "string" },
        "labels": { "type": "array", "items": { "type": "string" }, "description": "Category labels." },
        "series": { "type": "array", "description": "[{ name, values: [numbers], color, x_values (scatter) }]. Use the real numbers.", "items": { "type": "object" } },
        "legend": { "type": "string", "enum": ["right", "left", "top", "bottom", "none"] },
        "data_labels": { "type": "boolean" },
        "x_axis_title": { "type": "string" },
        "y_axis_title": { "type": "string" },
        "number_format": { "type": "string", "description": "Value format code such as #,##0 or 0%." },
        "colors": { "type": "array", "items": { "type": "string" } },
        "smooth": { "type": "boolean" }
        """;

    /// <summary>
    /// One content block.
    /// </summary>
    public const string Block = $$"""
        {
          "type": "object",
          "properties": {
            "type": { "type": "string", "enum": ["heading", "title", "subtitle", "paragraph", "markdown", "bullet_list", "numbered_list", "quote", "code", "table", "image", "chart", "caption", "toc", "page_break", "rule"], "description": "'title' only for a document that has no title yet; a document created with 'title' already has it at the top. 'toc' is a table of contents, filled in from the headings." },
            "text": { "type": "string", "description": "The text, with inline Markdown (**bold**, *italic*, `code`, [link](https://…)). For 'markdown', full Markdown that becomes headings, lists and tables. For 'code', the code. For 'toc', its title (default 'Contents')." },
            "level": { "type": "integer", "description": "Heading level 1-6." },
            "style": { "type": "string", "description": "Paragraph style name for a paragraph." },
            "alignment": { "type": "string", "enum": ["left", "center", "right", "justify"] },
            "format": {{RunFormat}},
            "paragraph_format": {{ParagraphFormat}},
            "items": { "type": "array", "description": "List items: strings, or { text, level, items, type, items_type } for nesting. 'type' (bullet or numbered) makes an item the other kind than its list, and 'items_type' does so for the items nested under it, such as bullets under a numbered goal.", "items": {} },
            "to_level": { "type": "integer", "description": "For a toc block: the lowest heading level listed. Default 3." },
            "start": { "type": "integer", "description": "First number of a numbered list." },
            "caption": { "type": "string", "description": "Numbered caption for a table (above), image or chart (below), e.g. 'Revenue by region'." },
            "caption_position": { "type": "string", "enum": ["above", "below"] },
            "label": { "type": "string", "description": "For a caption block: Figure, Table, Equation." },
            {{TableProperties}},
            {{ImageProperties}},
            {{ChartProperties}}
          },
          "required": ["type"]
        }
        """;

    /// <summary>
    /// A list of content blocks.
    /// </summary>
    public const string Blocks = $$"""
        "content": { "type": "array", "description": "Content blocks, in order. Add many blocks in one call.", "items": {{Block}} }
        """;

    /// <summary>
    /// The document-wide design.
    /// </summary>
    public const string Theme = """
        "theme": {
          "type": "object",
          "description": "The document's look. Start from a preset and override what you want.",
          "properties": {
            "preset": { "type": "string", "enum": ["default", "professional", "modern", "classic", "minimal", "vibrant", "elegant"] },
            "body_font": { "type": "string" },
            "heading_font": { "type": "string" },
            "body_size": { "type": "number", "description": "Points." },
            "text_color": { "type": "string" },
            "heading_color": { "type": "string" },
            "accent_color": { "type": "string", "description": "Rules, quote bars and table headers." },
            "link_color": { "type": "string" },
            "line_spacing": { "type": "number" },
            "paragraph_spacing": { "type": "number", "description": "Points after each body paragraph." },
            "table_header_fill": { "type": "string" },
            "table_header_text_color": { "type": "string" },
            "table_band_fill": { "type": "string" },
            "table_border_color": { "type": "string" }
          }
        }
        """;

    /// <summary>
    /// The page setup.
    /// </summary>
    public const string PageSetup = """
        "page_setup": {
          "type": "object",
          "properties": {
            "size": { "type": "string", "description": "letter, legal, tabloid, executive, A3, A4, A5, B5." },
            "orientation": { "type": "string", "enum": ["portrait", "landscape"] },
            "margins": { "type": ["number", "string", "object"], "description": "One length for every side, or { top, right, bottom, left, header, footer }. Points or with units (1in, 2.5cm)." },
            "columns": { "type": "integer" },
            "column_spacing": { "type": ["number", "string"] }
          }
        }
        """;

    /// <summary>
    /// Document properties.
    /// </summary>
    public const string Properties = """
        "properties": {
          "type": "object",
          "description": "Document properties: { title, subject, author, keywords, description, category, company }.",
          "properties": {
            "title": { "type": "string" },
            "subject": { "type": "string" },
            "author": { "type": "string" },
            "keywords": { "type": "string" },
            "description": { "type": "string" },
            "category": { "type": "string" },
            "company": { "type": "string" }
          }
        }
        """;
}
