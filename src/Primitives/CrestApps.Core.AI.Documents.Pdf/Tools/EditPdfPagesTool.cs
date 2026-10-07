using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Runs page operations on a PDF, in order, and saves the result as a working copy.
/// </summary>
/// <remarks>
/// Operations are a list so a request such as "merge these, drop the blank page, number the result and mark it
/// draft" is one call with one result, rather than a chain of intermediate copies. Page numbers in each
/// operation refer to the document as the operations before it left it.
/// </remarks>
internal sealed class EditPdfPagesTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.EditPdfPages;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "operations": {
              "type": "array",
              "description": "Operations run in order. Page numbers refer to the document as the previous operations left it.",
              "items": {
                "type": "object",
                "properties": {
                  "operation": {
                    "type": "string",
                    "enum": ["merge", "extract", "delete", "reorder", "reverse", "rotate", "duplicate", "insert_blank", "crop", "resize", "split", "watermark", "stamp", "page_numbers", "header_footer", "discard"],
                    "description": "merge (append other PDFs), extract (keep only 'pages'), delete, reorder ('order' such as '3,1,2'), reverse, rotate ('degrees' clockwise), duplicate ('count' copies after each page), insert_blank ('after', 'count', 'size'), crop ('margins' in mm), resize (fit onto 'size'), split ('every' N pages or 'ranges'; must be last; saves one working PDF per part), watermark, stamp (text such as APPROVED at a 'position'), page_numbers, header_footer, discard (delete this working PDF)."
                  },
                  "pages": { "type": "string", "description": "Pages this operation applies to, e.g. '1-3,5', 'odd', 'last'. Defaults to all." },
                  "sources": { "type": "array", "items": { "type": "string" }, "description": "merge: the PDFs to append (working PDF names or uploaded file names)." },
                  "position": { "type": "string", "description": "merge: 'end' (default), 'start' or a page number to insert after. stamp/page_numbers: top-left, top-center, top-right, center, bottom-left, bottom-center, bottom-right. watermark: center, top or bottom." },
                  "order": { "type": "string", "description": "reorder: the new order, e.g. '3,1,2,4-'. Pages not listed follow in their current order." },
                  "degrees": { "type": "integer", "enum": [90, 180, 270, -90] },
                  "count": { "type": "integer" },
                  "after": { "type": "integer", "description": "insert_blank: the page the blank pages go after (0 = first)." },
                  "size": { "type": "string", "description": "Paper size for insert_blank or resize, e.g. A4, Letter." },
                  "orientation": { "type": "string", "enum": ["portrait", "landscape"] },
                  "margins": {
                    "type": "object",
                    "properties": {
                      "margin_top_mm": { "type": "number" },
                      "margin_bottom_mm": { "type": "number" },
                      "margin_left_mm": { "type": "number" },
                      "margin_right_mm": { "type": "number" }
                    }
                  },
                  "every": { "type": "integer", "description": "split: pages per part." },
                  "ranges": { "type": "array", "items": { "type": "string" }, "description": "split: one page range per part, e.g. ['1-3', '4-10']." },
                  "text": { "type": "string", "description": "watermark or stamp text. Tokens: {page}, {pages}, {title}, {date}." },
                  "image": { "type": "string", "description": "watermark: an image source instead of text." },
                  "opacity": { "type": "number" },
                  "rotation": { "type": "number" },
                  "font_size": { "type": "number" },
                  "color": { "type": "string" },
                  "bold": { "type": "boolean" },
                  "box": { "type": "boolean", "description": "stamp: draw a box around the text." },
                  "behind": { "type": "boolean", "description": "watermark: draw behind the content." },
                  "template": { "type": "string", "description": "page_numbers: e.g. 'Page {page} of {pages}' (default) or '{page}'." },
                  "start_at": { "type": "integer" },
                  "format": { "type": "string", "enum": ["1", "i", "I", "a", "A"] },
                  "skip_first": { "type": "boolean", "description": "page_numbers: leave the first page unnumbered." },
                  "header": { "type": "object", "properties": { "left": { "type": "string" }, "center": { "type": "string" }, "right": { "type": "string" }, "font_size": { "type": "number" }, "color": { "type": "string" } } },
                  "footer": { "type": "object", "properties": { "left": { "type": "string" }, "center": { "type": "string" }, "right": { "type": "string" }, "font_size": { "type": "number" }, "color": { "type": "string" } } },
                  "margin": { "type": "number", "description": "Distance of stamped text from the page edge, in points." }
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
    /// Initializes a new instance of the <see cref="EditPdfPagesTool"/> class.
    /// </summary>
    public EditPdfPagesTool()
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
    public override string Description => "Edits the pages of an uploaded or working PDF with a list of operations run in order: merge other PDFs, extract, delete, reorder, reverse, rotate, duplicate, insert blank pages, crop, resize to a paper size, split into parts, and draw a watermark, a stamp (APPROVED, CONFIDENTIAL…), page numbers or header/footer text onto existing pages. The upload is never changed: the result is saved as a working PDF (named after the upload, or 'save_as'). For a document being composed, prefer format_pdf for watermarks, page numbers and headers so they stay editable.";

    /// <summary>
    /// Runs the operations.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var operations = arguments.Get<List<PdfPageOperation>>("operations");

        if (operations is not { Count: > 0 })
        {
            throw new PdfToolException("Pass 'operations', a list such as [{\"operation\": \"rotate\", \"pages\": \"2\", \"degrees\": 90}].");
        }

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());

            if (operations.Count == 1 && Normalize(operations[0].Operation) == "discard")
            {
                return await DiscardAsync(context, state, target);
            }

            var bytes = await context.ReadPdfAsync(target, cancellationToken);
            var title = Path.GetFileNameWithoutExtension(target.Name);
            var dateText = context.TimeProvider.GetLocalNow().ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
            var log = new StringBuilder();
            List<byte[]> parts = null;
            List<string> partRanges = null;

            using var editor = new PdfPageEditor(bytes, arguments.GetString("password"), title, dateText);

            for (var index = 0; index < operations.Count; index++)
            {
                var operation = operations[index] ?? new PdfPageOperation();
                var name = Normalize(operation.Operation);

                if (parts is not null)
                {
                    throw new PdfToolException("'split' must be the last operation.");
                }

                string message;

                switch (name)
                {
                    case "merge":
                        {
                            if (operation.Sources is not { Count: > 0 })
                            {
                                throw new PdfToolException("merge needs 'sources', the PDFs to append.");
                            }

                            var sources = new List<(string, byte[])>();

                            foreach (var sourceName in operation.Sources)
                            {
                                var source = context.FindPdf(state, sourceName);
                                sources.Add((source.Name, await context.ReadPdfAsync(source, cancellationToken)));
                            }

                            message = editor.Merge(sources, operation.Position);

                            break;
                        }

                    case "extract":
                        message = editor.Extract(Pages(operation.Pages, editor));

                        break;

                    case "delete":
                        message = editor.Delete(Pages(operation.Pages, editor, required: true));

                        break;

                    case "reorder":
                        message = editor.Reorder(PdfPageRange.Parse(operation.Order ?? operation.Pages ?? throw new PdfToolException("reorder needs 'order', such as '3,1,2'."), editor.PageCount, keepOrderAndDuplicates: true));

                        break;

                    case "reverse":
                        message = editor.Reverse();

                        break;

                    case "rotate":
                        message = editor.Rotate(Pages(operation.Pages, editor), operation.Degrees ?? 90);

                        break;

                    case "duplicate":
                        message = editor.Duplicate(Pages(operation.Pages, editor, required: true), operation.Count ?? 1);

                        break;

                    case "insert_blank":
                        message = editor.InsertBlank(operation.After ?? editor.PageCount, operation.Count ?? 1, operation.Size, operation.Orientation);

                        break;

                    case "crop":
                        message = editor.Crop(Pages(operation.Pages, editor), operation.Margins);

                        break;

                    case "resize":
                        message = editor.Resize(operation.Size, operation.Orientation);

                        break;

                    case "split":
                        {
                            var ranges = SplitRanges(operation, editor.PageCount);
                            partRanges = [.. ranges.Select(range => PdfPageRange.Describe(range))];
                            parts = editor.Split(ranges);
                            message = $"Split into {parts.Count} part(s): pages {string.Join(" | ", partRanges)}";

                            break;
                        }

                    case "watermark":
                        {
                            var image = string.IsNullOrWhiteSpace(operation.Image)
                                ? null
                                : await context.ResolveAsync(operation.Image, cancellationToken)
                                    ?? throw new PdfToolException($"The watermark image \"{operation.Image}\" could not be found, or is not a JPEG, PNG, GIF or BMP.");

                            if (image is null && string.IsNullOrWhiteSpace(operation.Text))
                            {
                                throw new PdfToolException("A watermark needs 'text' or 'image'.");
                            }

                            message = editor.Watermark(Pages(operation.Pages, editor), new PdfWatermarkDefinition
                            {
                                Text = operation.Text,
                                Image = operation.Image,
                                Opacity = operation.Opacity,
                                Rotation = operation.Rotation,
                                FontSize = operation.FontSize,
                                Color = operation.Color,
                                Position = operation.Position,
                                Behind = operation.Behind,
                            }, image);

                            break;
                        }

                    case "stamp":
                        message = editor.Stamp(Pages(operation.Pages, editor), operation.Text, operation.Position ?? "top-right", new PdfTextStampStyle
                        {
                            FontSize = operation.FontSize ?? 18,
                            Bold = operation.Bold ?? true,
                            Color = PdfColor.Parse(operation.Color, new PdfColor(0xC0, 0x00, 0x00)),
                            Opacity = Math.Clamp(operation.Opacity ?? 0.9, 0.05, 1),
                            Box = operation.Box ?? true,
                            Margin = operation.Margin ?? 36,
                        });

                        break;

                    case "page_numbers":
                        {
                            var pages = Pages(operation.Pages, editor);

                            if (operation.SkipFirst == true && pages.Count > 1)
                            {
                                pages.RemoveAt(0);
                            }

                            message = editor.PageNumbers(pages, operation.Template, operation.Position, operation.StartAt ?? pages[0], operation.Format, new PdfTextStampStyle
                            {
                                FontSize = operation.FontSize ?? 9,
                                Bold = operation.Bold == true,
                                Color = PdfColor.Parse(operation.Color, new PdfColor(0x40, 0x40, 0x40)),
                                Opacity = Math.Clamp(operation.Opacity ?? 1, 0.05, 1),
                                Margin = operation.Margin ?? 24,
                            });

                            break;
                        }

                    case "header_footer":
                        message = editor.HeaderFooter(Pages(operation.Pages, editor), operation.Header, operation.Footer, new PdfTextStampStyle
                        {
                            FontSize = operation.FontSize ?? 8.5,
                            Color = PdfColor.Parse(operation.Color, new PdfColor(0x50, 0x50, 0x50)),
                            Margin = operation.Margin ?? 24,
                        });

                        break;

                    case "discard":
                        throw new PdfToolException("'discard' deletes the working PDF and must be the only operation.");

                    default:
                        throw new PdfToolException($"'{operation.Operation}' is not a page operation.");
                }

                log.Append(index + 1).Append(". ").Append(message);

                if (parts is null)
                {
                    log.Append(" → ").Append(editor.PageCount).Append(" page(s)");
                }

                log.AppendLine(".");
            }

            var saveAs = arguments.GetString("save_as");
            var response = new StringBuilder();

            response.Append("Edited ").Append(target.Describe()).AppendLine(":").Append(log);

            if (parts is not null)
            {
                var baseName = PdfToolContext.SanitizeName(saveAs ?? Path.GetFileNameWithoutExtension(target.Name));

                response.AppendLine("Saved the parts as working PDFs:");

                for (var index = 0; index < parts.Count; index++)
                {
                    var partName = PdfToolContext.UniqueName(state, $"{baseName}-part-{index + 1}");
                    var working = await context.SaveWorkingFileAsync(state, target, partName, parts[index], $"Split part {index + 1} (pages {partRanges[index]})", cancellationToken);

                    response.Append("- \"").Append(working.Name).Append("\" (").Append(working.PageCount).AppendLine(" page(s))");
                }
            }
            else
            {
                var working = await context.SaveWorkingFileAsync(state, target, saveAs, editor.Save(), string.Join("; ", log.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim())), cancellationToken);

                response.Append("Saved as working PDF \"").Append(working.Name).Append("\" (").Append(working.PageCount).Append(" page(s)).");
            }

            if (target.IsUpload)
            {
                response.Append(" The uploaded file was not changed.");
            }

            response.Append(" Show it with preview_pdf or deliver it with export_pdf.");

            return response.ToString();
        }, cancellationToken);
    }

    private static string Normalize(string operation)
    {
        return operation?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
        {
            "combine" or "append" or "join" => "merge",
            "keep" or "select" => "extract",
            "remove" or "delete_pages" => "delete",
            "move" or "order" or "sort" => "reorder",
            "rotate_pages" or "turn" => "rotate",
            "copy" or "repeat" => "duplicate",
            "insert" or "blank" or "add_blank" or "insert_page" or "add_page" => "insert_blank",
            "trim" => "crop",
            "scale" or "fit" => "resize",
            "watermark_text" => "watermark",
            "page_number" or "number" or "numbering" => "page_numbers",
            "header" or "footer" or "headers_footers" => "header_footer",
            "delete_document" or "remove_document" => "discard",
            var value => value,
        };
    }

    private static List<int> Pages(string selection, PdfPageEditor editor, bool required = false)
    {
        if (required && string.IsNullOrWhiteSpace(selection))
        {
            throw new PdfToolException("This operation needs 'pages'.");
        }

        return PdfPageRange.Parse(selection, editor.PageCount);
    }

    private static List<List<int>> SplitRanges(PdfPageOperation operation, int pageCount)
    {
        if (operation.Ranges is { Count: > 0 })
        {
            return [.. operation.Ranges.Select(range => PdfPageRange.Parse(range, pageCount, keepOrderAndDuplicates: true))];
        }

        var every = operation.Every ?? 1;

        if (every < 1)
        {
            throw new PdfToolException("'every' must be at least 1.");
        }

        var ranges = new List<List<int>>();

        for (var start = 1; start <= pageCount; start += every)
        {
            ranges.Add([.. Enumerable.Range(start, Math.Min(every, pageCount - start + 1))]);
        }

        if (ranges.Count > 100)
        {
            throw new PdfToolException($"That would make {ranges.Count} parts; the most a split makes is 100.");
        }

        return ranges;
    }

    private static async Task<string> DiscardAsync(PdfToolContext context, PdfWorkspaceState state, PdfSource target)
    {
        if (target.IsUpload)
        {
            throw new PdfToolException("Uploaded files cannot be discarded here; only working PDFs can.");
        }

        state.Documents.Remove(target.Working);

        if (!string.IsNullOrEmpty(target.Working.BlobPath))
        {
            await context.DeleteBlobAsync(target.Working.BlobPath);
        }

        if (string.Equals(state.ActiveDocument, target.Working.Name, StringComparison.OrdinalIgnoreCase))
        {
            state.ActiveDocument = state.Documents.Count > 0 ? state.Documents[^1].Name : null;
        }

        return $"Discarded working PDF \"{target.Name}\".";
    }
}
