namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// The pieces of JSON schema the presentation tools share, so an element, a slide or a style is described the
/// same way to the model by every tool that takes one.
/// </summary>
internal static class PresentationSchemas
{
    /// <summary>
    /// The argument that names the deck.
    /// </summary>
    public const string Presentation = """
        "presentation": { "type": "string", "description": "Name or id of the presentation. Omit to use the active one (the one created, uploaded or edited most recently)." }
        """;

    /// <summary>
    /// A length on the slide.
    /// </summary>
    public const string Length = """{ "type": ["number", "string"], "description": "Points (72 points = 1 inch; a 16:9 slide is 960 x 540), or text with a unit: '1.5in', '3cm', '25%' (of the slide)." }""";

    /// <summary>
    /// A colour.
    /// </summary>
    public const string Color = """{ "type": "string", "description": "#RRGGBB, a colour name, or a theme colour (accent1-accent6, text1, text2, background1, background2), optionally shaded ('accent1 lighter 40%'), or 'none'." }""";

    /// <summary>
    /// Paragraph text.
    /// </summary>
    public const string Text = """{ "description": "Text: a string with one paragraph per line (a line starting with '- ' is a bullet; indent two spaces per sub-level; '1. ' numbers it; **bold**, *italic* and [label](https://url or #3 for slide 3) work), or an array of strings, nested arrays for sub-points, or objects {text, level (1-9), bullet ('bullet'|'number'|'none'|a character), bold, italic, color, size, link}." }""";

    /// <summary>
    /// A text style.
    /// </summary>
    public const string TextStyle = """
        { "type": "object", "description": "How text looks. Every property is optional.", "properties": {
          "font": { "type": "string", "description": "Typeface, or 'heading'/'body' for the theme's fonts." },
          "size": { "type": "number", "description": "Points." },
          "bold": { "type": "boolean" }, "italic": { "type": "boolean" }, "underline": { "type": "boolean" }, "strikethrough": { "type": "boolean" },
          "color": COLOR, "highlight": COLOR,
          "align": { "type": "string", "enum": ["left", "center", "right", "justify"] },
          "vertical_align": { "type": "string", "enum": ["top", "middle", "bottom"] },
          "line_spacing": { "type": "number", "description": "Multiple of single spacing, such as 1.15." },
          "space_before": { "type": "number", "description": "Points above each paragraph." },
          "space_after": { "type": "number", "description": "Points below each paragraph." },
          "caps": { "type": "string", "enum": ["none", "all", "small"] },
          "autofit": { "type": "string", "enum": ["none", "shrink", "resize"], "description": "shrink text to fit its box, or grow the box to fit." }
        } }
        """;

    /// <summary>
    /// The properties of a shape style, spread into the object that uses them.
    /// </summary>
    public const string ShapeStyleProperties = """
        "fill": COLOR,
        "gradient": { "type": "array", "items": { "type": "string" }, "description": "Two or more colours for a gradient fill." },
        "gradient_angle": { "type": "number", "description": "Degrees clockwise from left-to-right; 90 runs top to bottom." },
        "transparency": { "type": "number", "description": "0 (opaque) to 100." },
        "outline_color": COLOR,
        "outline_width": { "type": "number", "description": "Points." },
        "outline_dash": { "type": "string", "enum": ["solid", "dash", "dot", "dash_dot", "long_dash"] },
        "shadow": { "type": "boolean" },
        "corner_radius": { "type": "number", "description": "Rounded rectangles: 0 (square) to 50 (fully round)." }
        """;

    /// <summary>
    /// A table style.
    /// </summary>
    public const string TableStyle = """
        { "type": "object", "description": "How a table looks.", "properties": {
          "header_fill": COLOR, "header_text_color": COLOR, "header_bold": { "type": "boolean" },
          "body_fill": COLOR, "band_fill": COLOR, "banded_rows": { "type": "boolean" },
          "text_color": COLOR, "border_color": COLOR, "border_width": { "type": "number" },
          "font": { "type": "string" }, "font_size": { "type": "number" },
          "first_column_bold": { "type": "boolean" }, "total_row": { "type": "boolean", "description": "Style the last row as a total." }
        } }
        """;

    /// <summary>
    /// A chart style.
    /// </summary>
    public const string ChartStyle = """
        { "type": "object", "description": "How a chart looks.", "properties": {
          "colors": { "type": "array", "items": { "type": "string" }, "description": "Series colours in order (slice colours for a pie)." },
          "legend": { "type": "string", "enum": ["bottom", "top", "left", "right", "none"] },
          "data_labels": { "type": "boolean" },
          "gridlines": { "type": "boolean" },
          "number_format": { "type": "string", "description": "Excel format such as '#,##0', '$#,##0.0,,\"M\"' or '0%'." },
          "font": { "type": "string" }, "font_size": { "type": "number" }, "text_color": COLOR
        } }
        """;

    /// <summary>
    /// A chart.
    /// </summary>
    public const string Chart = """
        { "type": "object", "description": "A chart's data and look.", "properties": {
          "kind": { "type": "string", "enum": ["column", "stacked_column", "percent_column", "bar", "stacked_bar", "percent_bar", "line", "line_markers", "area", "stacked_area", "pie", "doughnut", "scatter", "radar"] },
          "title": { "type": "string" },
          "categories": { "type": "array", "items": { "type": "string" } },
          "series": { "type": "array", "items": { "type": "object", "properties": { "name": { "type": "string" }, "values": { "type": "array", "items": { "type": ["number", "null"] } }, "color": { "type": "string" } } } },
          "category_axis_title": { "type": "string" }, "value_axis_title": { "type": "string" },
          "style": CHARTSTYLE
        } }
        """;

    /// <summary>
    /// An element to place on a slide.
    /// </summary>
    public const string Element = """
        { "type": "object", "description": "An element to place on a slide.", "properties": {
          "type": { "type": "string", "enum": ["text", "bullets", "shape", "line", "arrow", "table", "chart", "image", "icon", "diagram", "video", "audio", "group"] },
          "name": { "type": "string", "description": "Name to address the element by later." },
          "position": { "type": "string", "enum": ["content", "slide", "left_half", "right_half", "top_half", "bottom_half", "left_third", "center_third", "right_third", "left_two_thirds", "right_two_thirds", "top_left", "top_right", "bottom_left", "bottom_right", "center", "title", "footer"], "description": "A named area of the slide's content region. Tables, charts and pictures default to 'content', but next to body text they take the right half." },
          "x": LENGTH, "y": LENGTH, "width": LENGTH, "height": LENGTH,
          "rotation": { "type": "number", "description": "Degrees clockwise." },
          "text": TEXT,
          "style": TEXTSTYLE,
          SHAPESTYLE,
          "shape": { "type": "string", "description": "For shape: rectangle, rounded_rectangle, ellipse, triangle, diamond, pentagon, hexagon, octagon, parallelogram, trapezoid, chevron, home_plate, arrow, left_arrow, up_arrow, down_arrow, plus, star, heart, lightning, sun, moon, cloud, donut, callout, cylinder, cube, frame, gear, teardrop, funnel, process, decision, terminator, document." },
          "icon": { "type": "string", "description": "For icon: check, cross, info, question, warning, dollar, percent, phone, mail, home, flag, plane, music, pencil, time, number1-number5, star, heart, lightning, sun, moon, cloud, gear, smiley, plus, arrow, growth, decline, database, filter, target, idea." },
          "from": { "type": "string", "description": "For line/arrow: the element (id or name) it starts at." },
          "to": { "type": "string", "description": "For line/arrow: the element it ends at." },
          "start_x": LENGTH, "start_y": LENGTH, "end_x": LENGTH, "end_y": LENGTH,
          "start_arrow": { "type": "string", "enum": ["none", "triangle", "arrow", "stealth", "diamond", "oval"] },
          "end_arrow": { "type": "string", "enum": ["none", "triangle", "arrow", "stealth", "diamond", "oval"] },
          "rows": { "type": "array", "items": { "type": "array", "items": { "type": "string" } }, "description": "For table: rows of cell text, header row first. Keep to about 12 rows." },
          "header": { "type": "boolean", "description": "For table: whether the first row is a header. Defaults to true." },
          "column_widths": { "type": "array", "items": LENGTH },
          "column_alignments": { "type": "array", "items": { "type": "string", "enum": ["left", "center", "right"] } },
          "table_style": TABLESTYLE,
          "chart": CHART,
          "tabular_sql": { "type": "string", "description": "For table or chart: a read-only SQL query over the conversation's uploaded spreadsheet data (see list_tabular_data) that supplies the rows, so you do not have to copy numbers. A chart plots the first column as categories and the numeric columns as series." },
          "category_column": { "type": "string" }, "value_columns": { "type": "array", "items": { "type": "string" } },
          "max_rows": { "type": "integer", "description": "Most query rows used. Defaults to 25." },
          "link_data": { "type": "boolean", "description": "Keep the table or chart tied to its query so refresh_slide_data can update it." },
          "image_document_id": { "type": "string", "description": "For image: the id or file name of an uploaded picture." },
          "image_prompt": { "type": "string", "description": "For image: describe a picture to generate with the image model instead." },
          "fit": { "type": "string", "enum": ["contain", "cover", "stretch"], "description": "For image: keep the whole picture (contain, default) or fill the box and crop (cover)." },
          "media_url": { "type": "string", "description": "For video/audio: the address it plays from; shown as a labelled link." },
          "alt_text": { "type": "string", "description": "What a screen reader announces. Give it for every picture, chart and icon." },
          "decorative": { "type": "boolean" },
          "link": { "type": "string", "description": "Where clicking goes: an https address, 'slide 4', or next/previous/first/last." },
          "diagram_type": { "type": "string", "enum": ["process", "chevron", "cycle", "timeline", "hierarchy", "pyramid", "funnel", "matrix", "venn", "cards"], "description": "For diagram: its layout, drawn as editable shapes." },
          "items": { "type": "array", "items": { "type": ["string", "object"] }, "description": "For diagram: the steps, stages or nodes in order, as strings or {text, detail, parent (hierarchy: the text of the item above), color}." },
          "center_text": { "type": "string", "description": "For a cycle or venn diagram: text in the middle." },
          "colors": { "type": "array", "items": { "type": "string" }, "description": "For diagram: fill colours used in turn. Defaults to the theme's accents." },
          "children": { "type": "array", "items": { "type": "object" }, "description": "For group: its elements, each in this same format." }
        } }
        """;

    /// <summary>
    /// A slide to add.
    /// </summary>
    public const string Slide = """
        { "type": "object", "description": "A slide to add.", "properties": {
          "layout": { "type": "string", "description": "title, title_and_content (default), section_header, two_content, comparison, title_only, blank, content_with_caption, picture_with_caption, or the name of one of the deck's layouts." },
          "title": { "type": "string" },
          "subtitle": { "type": "string", "description": "For title and section header slides." },
          "body": TEXT,
          "right_body": TEXT,
          "left_heading": { "type": "string", "description": "For comparison: heading above the left column." },
          "right_heading": { "type": "string", "description": "For comparison: heading above the right column." },
          "notes": { "type": "string", "description": "Speaker notes." },
          "elements": { "type": "array", "items": ELEMENT, "description": "Further elements: charts, tables, pictures, shapes, icons." },
          "background": { "description": "A colour, a list of gradient colours, or {color, gradient, image_document_id, image_transparency}." },
          "position": { "type": "integer", "description": "Where to insert it, counting from 1. Defaults to the end." },
          "hidden": { "type": "boolean" }
        } }
        """;

    /// <summary>
    /// Expands the placeholders in a schema: <c>COLOR</c>, <c>LENGTH</c>, <c>TEXT</c>, <c>TEXTSTYLE</c>,
    /// <c>SHAPESTYLE</c>, <c>TABLESTYLE</c>, <c>CHARTSTYLE</c>, <c>CHART</c>, <c>ELEMENT</c>, <c>SLIDE</c> and
    /// <c>PRESENTATION</c>.
    /// </summary>
    /// <param name="schema">The schema with placeholders.</param>
    /// <returns>The schema.</returns>
    public static string Expand(string schema)
    {
        // Composite pieces first, so the pieces they contain are expanded after them.
        return schema
            .Replace("SLIDE", Slide, StringComparison.Ordinal)
            .Replace("ELEMENT", Element, StringComparison.Ordinal)
            .Replace("CHARTSTYLE", ChartStyle, StringComparison.Ordinal)
            .Replace("CHART", Chart, StringComparison.Ordinal)
            .Replace("CHARTSTYLE", ChartStyle, StringComparison.Ordinal)
            .Replace("TABLESTYLE", TableStyle, StringComparison.Ordinal)
            .Replace("TEXTSTYLE", TextStyle, StringComparison.Ordinal)
            .Replace("SHAPESTYLE", ShapeStyleProperties, StringComparison.Ordinal)
            .Replace("TEXT", Text, StringComparison.Ordinal)
            .Replace("LENGTH", Length, StringComparison.Ordinal)
            .Replace("COLOR", Color, StringComparison.Ordinal)
            .Replace("PRESENTATION", Presentation, StringComparison.Ordinal);
    }
}
