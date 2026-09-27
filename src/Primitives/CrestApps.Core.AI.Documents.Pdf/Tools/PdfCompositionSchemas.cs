namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// The JSON schema properties of a composed document, shared by the tools that create, fill and format one.
/// </summary>
internal static class PdfCompositionSchemas
{
    /// <summary>
    /// The <c>blocks</c> property.
    /// </summary>
    public const string Blocks = """
        "blocks": {
          "type": "array",
          "description": "Content blocks in reading order. Put every block of a request in ONE call.",
          "items": {
            "type": "object",
            "properties": {
              "type": {
                "type": "string",
                "enum": ["heading", "paragraph", "markdown", "list", "table", "image", "chart", "page_break", "spacer", "rule", "quote", "code", "callout", "key_value", "signature_lines"],
                "description": "heading (becomes a bookmark and a contents entry), paragraph (inline **bold**, *italic*, `code`, [link](https://…)), markdown (headings, lists, quotes, code and tables in Markdown), list, table, image, chart, page_break, spacer, rule, quote, code, callout (shaded note box), key_value (label/value pairs), signature_lines."
              },
              "text": { "type": "string", "description": "Text of a heading, paragraph, markdown, quote, code or callout block." },
              "level": { "type": "integer", "description": "Heading level, 1 (largest) to 4." },
              "items": { "type": "array", "items": { "type": "string" }, "description": "List entries (indent with two spaces per level to nest), key_value entries as 'Label: value', or signature_lines labels." },
              "pairs": { "type": "array", "items": { "type": "array", "items": { "type": "string" } }, "description": "key_value pairs as [label, value]." },
              "ordered": { "type": "boolean", "description": "Number a list." },
              "title": { "type": "string", "description": "Callout title." },
              "variant": { "type": "string", "enum": ["info", "success", "warning", "danger", "note"], "description": "Callout colour." },
              "table": {
                "type": "object",
                "description": "Give raw values in rows and let the columns format them.",
                "properties": {
                  "columns": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "properties": {
                        "header": { "type": "string" },
                        "width": { "type": "number", "description": "Percent of the table width." },
                        "align": { "type": "string", "enum": ["left", "center", "right"] },
                        "format": { "type": "string", "enum": ["general", "text", "number", "integer", "currency", "accounting", "percent", "date", "datetime", "time", "scientific"], "description": "Percent expects fractions (0.15 → 15%)." },
                        "decimals": { "type": "integer" },
                        "currency_symbol": { "type": "string" },
                        "format_code": { "type": "string", "description": "Spreadsheet format code such as '#,##0.00'." },
                        "negatives_in_red": { "type": "boolean" },
                        "bold": { "type": "boolean" },
                        "color": { "type": "string" },
                        "background_color": { "type": "string" }
                      },
                      "required": ["header"]
                    }
                  },
                  "rows": { "type": "array", "items": { "type": "array", "items": { "type": ["string", "number", "boolean", "null"] } } },
                  "source": {
                    "type": "object",
                    "description": "Read the rows from the uploaded spreadsheets instead of listing them: a read-only SQLite 'sql' query (names from list_tabular_data) or a 'table_name'. Columns default to the result's columns and formats.",
                    "properties": {
                      "sql": { "type": "string" },
                      "table_name": { "type": "string" },
                      "max_rows": { "type": "integer" }
                    }
                  },
                  "total_row": {
                    "type": "object",
                    "properties": {
                      "label": { "type": "string" },
                      "functions": { "type": "object", "description": "Column header → sum, average, count, min or max.", "additionalProperties": { "type": "string" } }
                    }
                  },
                  "highlight_rules": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "properties": {
                        "column": { "type": "string" },
                        "operator": { "type": "string", "enum": ["gt", "gte", "lt", "lte", "eq", "ne", "between", "contains", "negative", "positive", "duplicate", "scale"] },
                        "value": { "type": "string" },
                        "value2": { "type": "string" },
                        "background_color": { "type": "string", "description": "Fill of a matching cell; for scale, the colour of the highest value." },
                        "minimum_color": { "type": "string", "description": "For scale, the colour of the lowest value." },
                        "color": { "type": "string" },
                        "bold": { "type": "boolean" }
                      },
                      "required": ["column", "operator"]
                    }
                  },
                  "banded": { "type": "boolean" },
                  "header_background": { "type": "string" },
                  "header_text_color": { "type": "string" },
                  "band_color": { "type": "string" },
                  "border_color": { "type": "string" },
                  "borders": { "type": "string", "enum": ["all", "horizontal", "none"] },
                  "font_size": { "type": "number" },
                  "repeat_header": { "type": "boolean" },
                  "caption": { "type": "string" }
                }
              },
              "image": {
                "type": "object",
                "properties": {
                  "source": { "type": "string", "description": "An uploaded image's file name or id, an 'asset:imgN' id from extract_pdf_images, a [fig:N] marker of an extracted image, 'figure:{documentId}/{figureId}', or a data:image/png;base64 URI." },
                  "width_percent": { "type": "number", "description": "Width as a percent of the text width." },
                  "width": { "type": "number", "description": "Width in points." },
                  "height": { "type": "number", "description": "Height in points." },
                  "caption": { "type": "string" },
                  "alt_text": { "type": "string" }
                },
                "required": ["source"]
              },
              "chart": {
                "type": "object",
                "description": "A vector chart. Give labels and series with the real values, or source over uploaded tabular data, or chart_js with a [chart:…] marker returned earlier.",
                "properties": {
                  "chart_type": { "type": "string", "enum": ["column", "bar", "stacked_column", "stacked_bar", "line", "area", "pie"] },
                  "title": { "type": "string" },
                  "labels": { "type": "array", "items": { "type": "string" } },
                  "series": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "properties": {
                        "name": { "type": "string" },
                        "values": { "type": "array", "items": { "type": ["number", "null"] } },
                        "color": { "type": "string" }
                      },
                      "required": ["values"]
                    }
                  },
                  "source": {
                    "type": "object",
                    "properties": {
                      "sql": { "type": "string" },
                      "table_name": { "type": "string" },
                      "label_column": { "type": "string" },
                      "value_columns": { "type": "array", "items": { "type": "string" } },
                      "max_rows": { "type": "integer" }
                    }
                  },
                  "chart_js": { "type": "string" },
                  "x_axis_title": { "type": "string" },
                  "y_axis_title": { "type": "string" },
                  "legend": { "type": "boolean" },
                  "data_labels": { "type": "boolean" },
                  "number_format": { "type": "string", "description": "For example '#,##0' or '0%'." },
                  "width_percent": { "type": "number" },
                  "height": { "type": "number", "description": "Height in points." },
                  "caption": { "type": "string" }
                }
              },
              "height": { "type": "number", "description": "Spacer height in points." },
              "align": { "type": "string", "enum": ["left", "center", "right", "justify"] },
              "font_size": { "type": "number" },
              "font_family": { "type": "string" },
              "bold": { "type": "boolean" },
              "italic": { "type": "boolean" },
              "color": { "type": "string", "description": "Hex colour such as #1F4E79." },
              "background_color": { "type": "string" },
              "space_before": { "type": "number" },
              "space_after": { "type": "number" },
              "keep_with_next": { "type": "boolean" }
            },
            "required": ["type"]
          }
        }
        """;

    /// <summary>
    /// The <c>page_setup</c> property.
    /// </summary>
    public const string PageSetup = """
        "page_setup": {
          "type": "object",
          "description": "Paper and margins.",
          "properties": {
            "size": { "type": "string", "enum": ["A3", "A4", "A5", "A6", "B5", "Letter", "Legal", "Tabloid", "Executive"] },
            "orientation": { "type": "string", "enum": ["portrait", "landscape"] },
            "width_mm": { "type": "number", "description": "Custom width, with height_mm." },
            "height_mm": { "type": "number" },
            "margin_top_mm": { "type": "number" },
            "margin_bottom_mm": { "type": "number" },
            "margin_left_mm": { "type": "number" },
            "margin_right_mm": { "type": "number" }
          }
        }
        """;

    /// <summary>
    /// The <c>theme</c> property.
    /// </summary>
    public const string Theme = """
        "theme": {
          "type": "object",
          "description": "Colours (hex), fonts and table style for the whole document.",
          "properties": {
            "primary_color": { "type": "string", "description": "Brand colour for headings, table headers and the first chart series." },
            "accent_color": { "type": "string" },
            "text_color": { "type": "string" },
            "heading_color": { "type": "string" },
            "muted_color": { "type": "string" },
            "font_family": { "type": "string", "description": "Arial, Times New Roman, Georgia, Verdana, Tahoma, Trebuchet MS, Calibri, Cambria, Segoe UI, Courier New…" },
            "heading_font_family": { "type": "string" },
            "base_font_size": { "type": "number" },
            "line_spacing": { "type": "number", "description": "Multiple, e.g. 1.15." },
            "logo": { "type": "string", "description": "Image source for the logo (same forms as an image block's source)." },
            "logo_position": { "type": "string", "enum": ["header-left", "header-right", "footer-left", "footer-right", "none"] },
            "logo_height": { "type": "number" },
            "chart_colors": { "type": "array", "items": { "type": "string" } },
            "tables": {
              "type": "object",
              "properties": {
                "header_background": { "type": "string" },
                "header_text_color": { "type": "string" },
                "border_color": { "type": "string" },
                "banded": { "type": "boolean" },
                "band_color": { "type": "string" },
                "font_size": { "type": "number" },
                "borders": { "type": "string", "enum": ["all", "horizontal", "none"] }
              }
            }
          }
        }
        """;

    /// <summary>
    /// The <c>header</c> and <c>footer</c> properties.
    /// </summary>
    public const string RunningHeads = """
        "header": {
          "type": "object",
          "description": "Running head on body pages. Slots accept the tokens {page}, {pages}, {title}, {author}, {date}.",
          "properties": {
            "left": { "type": "string" },
            "center": { "type": "string" },
            "right": { "type": "string" },
            "separator": { "type": "boolean" },
            "show_on_first_page": { "type": "boolean" },
            "font_size": { "type": "number" },
            "color": { "type": "string" }
          }
        },
        "footer": {
          "type": "object",
          "description": "Running foot on body pages, same shape as header.",
          "properties": {
            "left": { "type": "string" },
            "center": { "type": "string" },
            "right": { "type": "string" },
            "separator": { "type": "boolean" },
            "show_on_first_page": { "type": "boolean" },
            "font_size": { "type": "number" },
            "color": { "type": "string" }
          }
        },
        "page_numbers": {
          "type": "object",
          "properties": {
            "enabled": { "type": "boolean" },
            "position": { "type": "string", "enum": ["footer-left", "footer-center", "footer-right", "header-left", "header-center", "header-right"] },
            "format": { "type": "string", "enum": ["1", "i", "I", "a", "A"] },
            "template": { "type": "string", "description": "Defaults to 'Page {page} of {pages}'." },
            "start_at": { "type": "integer" },
            "skip_first_page": { "type": "boolean" }
          }
        }
        """;

    /// <summary>
    /// The <c>cover_page</c>, <c>table_of_contents</c> and <c>watermark</c> properties.
    /// </summary>
    public const string FrontMatter = """
        "cover_page": {
          "type": "object",
          "properties": {
            "enabled": { "type": "boolean" },
            "title": { "type": "string" },
            "subtitle": { "type": "string" },
            "author": { "type": "string" },
            "date": { "type": "string" },
            "note": { "type": "string" },
            "logo": { "type": "string" },
            "background_color": { "type": "string" },
            "text_color": { "type": "string" },
            "align": { "type": "string", "enum": ["left", "center"] }
          }
        },
        "table_of_contents": {
          "type": "object",
          "properties": {
            "enabled": { "type": "boolean" },
            "title": { "type": "string" },
            "depth": { "type": "integer", "description": "Deepest heading level listed, default 2." }
          }
        },
        "watermark": {
          "type": "object",
          "properties": {
            "text": { "type": "string" },
            "image": { "type": "string" },
            "opacity": { "type": "number", "description": "0 to 1, default 0.18." },
            "rotation": { "type": "number", "description": "Degrees; defaults to the page diagonal." },
            "font_size": { "type": "number" },
            "color": { "type": "string" },
            "position": { "type": "string", "enum": ["center", "top", "bottom"] },
            "behind": { "type": "boolean" }
          }
        }
        """;

    /// <summary>
    /// The document metadata properties.
    /// </summary>
    public const string Metadata = """
        "title": { "type": "string", "description": "Document title (metadata and {title} token)." },
        "author": { "type": "string" },
        "subject": { "type": "string" },
        "keywords": { "type": "string" },
        "language": { "type": "string", "description": "BCP 47 language tag, e.g. en-US." },
        "pdf_a": { "type": "boolean", "description": "Write the file as PDF/A for archiving." }
        """;
}
