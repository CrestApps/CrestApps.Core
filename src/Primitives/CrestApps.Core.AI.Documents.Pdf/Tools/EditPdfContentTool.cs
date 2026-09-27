using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Replaces, adds or removes text and images on the pages of an existing PDF.
/// </summary>
internal sealed class EditPdfContentTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.EditPdfContent;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "operations": {
              "type": "array",
              "description": "Edits run in order.",
              "items": {
                "type": "object",
                "properties": {
                  "operation": { "type": "string", "enum": ["replace_text", "add_text", "add_image", "remove_area", "cover"], "description": "replace_text swaps 'find' for 'replace' where it stands; add_text writes 'text' at a position; add_image places an image; remove_area deletes the text and images inside an area; cover paints over an area without removing what is under it." },
                  "find": { "type": "string" },
                  "replace": { "type": "string", "description": "Replacement text; empty removes the text." },
                  "pages": { "type": "string", "description": "replace_text: pages to look on." },
                  "match_case": { "type": "boolean" },
                  "whole_word": { "type": "boolean" },
                  "occurrence": { "type": "integer", "description": "replace_text: only this occurrence (1 = first). Omit for all." },
                  "page": { "type": "integer", "description": "Page for add_text, add_image, remove_area and cover." },
                  "x": { "type": "number", "description": "Points from the left edge." },
                  "y": { "type": "number", "description": "Points from the top edge." },
                  "width": { "type": "number", "description": "add_text: wrap width. add_image, remove_area, cover: width." },
                  "height": { "type": "number" },
                  "text": { "type": "string" },
                  "source": { "type": "string", "description": "add_image: an uploaded image's file name or id, an asset:imgN id, a [fig:N] marker or a data URI." },
                  "font_size": { "type": "number" },
                  "font_family": { "type": "string" },
                  "color": { "type": "string" },
                  "bold": { "type": "boolean" },
                  "italic": { "type": "boolean" },
                  "align": { "type": "string", "enum": ["left", "center", "right", "justify"] },
                  "fill": { "type": "string", "description": "remove_area: 'none' (default), 'white' or a colour. cover: the colour (default white)." }
                },
                "required": ["operation"]
              }
            }
          },
          "required": ["operations"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="EditPdfContentTool"/> class.
    /// </summary>
    public EditPdfContentTool()
        : base(Schema)
    {
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Edits what the pages of an existing PDF show: replace_text swaps words or phrases where they stand (the old text is removed from the page, the new one set on the same baseline, size and colour), add_text writes text at a position, add_image places an image, remove_area deletes the text and images in an area, cover paints over an area. Positions are points from the page's top-left (search_pdf returns them). Saves a working PDF; the upload is never changed. For confidential content use redact_pdf.";

    /// <summary>
    /// Applies the edits.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var operations = arguments.Get<List<PdfContentEditOperation>>("operations");

        if (operations is not { Count: > 0 })
        {
            throw new PdfToolException("Pass 'operations', for example [{\"operation\": \"replace_text\", \"find\": \"2025\", \"replace\": \"2026\"}].");
        }

        var password = arguments.GetString("password");

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);
            var log = new List<string>();
            var warnings = new List<string>();
            var changed = false;

            foreach (var operation in operations)
            {
                if (operation is null)
                {
                    continue;
                }

                switch (operation.Operation?.Trim().ToLowerInvariant().Replace('-', '_'))
                {
                    case "replace_text" or "replace":
                        {
                            var (result, count) = PdfContentEditor.ReplaceText(bytes, password, operation, warnings);

                            if (count == 0)
                            {
                                log.Add($"\"{operation.Find}\" was not found; nothing replaced");
                            }
                            else
                            {
                                bytes = result;
                                changed = true;
                                log.Add(string.IsNullOrEmpty(operation.Replace)
                                    ? $"removed {count} occurrence(s) of \"{operation.Find}\""
                                    : $"replaced {count} occurrence(s) of \"{operation.Find}\" with \"{operation.Replace}\"");
                            }

                            break;
                        }

                    case "add_text" or "text":
                        bytes = PdfContentEditor.AddText(bytes, password, operation);
                        changed = true;
                        log.Add($"wrote \"{Clip(operation.Text)}\" on page {operation.Page ?? 1}");

                        break;

                    case "add_image" or "image":
                        {
                            var image = await context.ResolveAsync(operation.Source, cancellationToken)
                                ?? throw new PdfToolException($"The image \"{operation.Source}\" could not be found, or is not a JPEG, PNG, GIF or BMP.");

                            bytes = PdfContentEditor.AddImage(bytes, password, operation, image);
                            changed = true;
                            log.Add($"placed {operation.Source} on page {operation.Page ?? 1}");

                            break;
                        }

                    case "remove_area" or "remove" or "delete_area":
                        {
                            var (result, glyphs, images) = PdfContentEditor.RemoveArea(bytes, password, operation, removeContent: true);

                            bytes = result;
                            changed = true;
                            log.Add($"removed {glyphs} character(s) and {images} image(s) from an area of page {operation.Page ?? 1}");

                            break;
                        }

                    case "cover" or "whiteout":
                        {
                            var (result, _, _) = PdfContentEditor.RemoveArea(bytes, password, operation, removeContent: false);

                            bytes = result;
                            changed = true;
                            log.Add($"covered an area of page {operation.Page ?? 1} (the content under it is still in the file; use remove_area or redact_pdf to delete it)");

                            break;
                        }

                    default:
                        throw new PdfToolException($"'{operation.Operation}' is not an operation; use replace_text, add_text, add_image, remove_area or cover.");
                }
            }

            if (!changed)
            {
                return "Nothing was changed: " + string.Join("; ", log) + ".";
            }

            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), bytes, "Content: " + string.Join("; ", log), cancellationToken);
            var response = new StringBuilder();

            response.Append("Saved working PDF \"").Append(working.Name).AppendLine("\":");

            foreach (var line in log)
            {
                response.Append("- ").AppendLine(line);
            }

            foreach (var warning in warnings)
            {
                response.Append("- warning: ").AppendLine(warning);
            }

            response.Append(target.IsUpload ? "The uploaded file was not changed. " : string.Empty).Append("Preview the changed pages with preview_pdf.");

            return response.ToString();
        }, cancellationToken);
    }

    private static string Clip(string text)
    {
        return string.IsNullOrEmpty(text) || text.Length <= 40
            ? text
            : text[..39] + "…";
    }
}
