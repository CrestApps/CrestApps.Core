using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Lists, adds, changes and removes the comments and review marks on a PDF.
/// </summary>
internal sealed class ManagePdfAnnotationsTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ManagePdfAnnotations;

    private const int MaxListed = 200;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            "action": { "type": "string", "enum": ["list", "add", "update", "remove"], "description": "list (default) reads the comments and marks; add, update and remove change them." },
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "types": { "type": "array", "items": { "type": "string" }, "description": "list and remove: only these kinds (note, reply, highlight, underline, strikeout, squiggly, rectangle, ellipse, line, arrow, free_text, stamp, ink, link …). A remove without ids or types removes every comment and mark on the pages, but not links." },
            "ids": { "type": "array", "items": { "type": "string" }, "description": "remove: the ids list returned." },
            "annotations": {
              "type": "array",
              "description": "add: the annotations to add. update: the changes, each with its 'id'.",
              "items": {
                "type": "object",
                "properties": {
                  "id": { "type": "string", "description": "update: the annotation to change." },
                  "type": { "type": "string", "enum": ["note", "highlight", "underline", "strikeout", "squiggly", "rectangle", "ellipse", "line", "arrow", "free_text", "stamp"] },
                  "text": { "type": "string", "description": "Text on the page to mark. Highlight, underline, strikeout and squiggly cover it; a note is placed after it; a rectangle or ellipse circles it; a stamp or text box is put beside it." },
                  "occurrence": { "type": "integer", "description": "Only this occurrence of 'text' (1 = first). Omit for every one." },
                  "match_case": { "type": "boolean" },
                  "page": { "type": "integer" },
                  "pages": { "type": "string", "description": "Pages to look for 'text' on." },
                  "x": { "type": "number", "description": "Points from the left edge." },
                  "y": { "type": "number", "description": "Points from the top edge." },
                  "width": { "type": "number" },
                  "height": { "type": "number" },
                  "x2": { "type": "number", "description": "line, arrow: where it ends." },
                  "y2": { "type": "number" },
                  "contents": { "type": "string", "description": "A note's comment, a text box's text or a stamp's label (APPROVED, DRAFT, CONFIDENTIAL, PAID …). On a mark it is the comment attached to it." },
                  "author": { "type": "string" },
                  "subject": { "type": "string" },
                  "color": { "type": "string" },
                  "fill_color": { "type": "string", "description": "rectangle, ellipse, free_text: the fill, or 'none'." },
                  "opacity": { "type": "number", "minimum": 0, "maximum": 1 },
                  "border_width": { "type": "number" },
                  "font_size": { "type": "number", "description": "free_text." },
                  "icon": { "type": "string", "enum": ["Comment", "Note", "Help", "Key", "Insert", "Paragraph", "NewParagraph"] }
                }
              }
            }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagePdfAnnotationsTool"/> class.
    /// </summary>
    public ManagePdfAnnotationsTool()
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
    public override string Description => "Reads and edits a PDF's comments and review marks. list returns every sticky note, reply, highlight (with the text it covers), text box, shape, stamp and link with its id, page, position, author and comment. add puts notes, highlights, underlines, strikeouts, squiggles, rectangles, ellipses, lines, arrows, text boxes and stamps on pages — by the text they mark or by position. update changes a comment, colour, author or position by id; remove deletes by id or kind. Changes save a working PDF; the upload is never changed.";

    /// <summary>
    /// Runs the action.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var action = arguments.GetString("action")?.Trim().ToLowerInvariant() ?? "list";
        var password = arguments.GetString("password");
        var types = new HashSet<string>(arguments.GetStrings("types", splitCommas: true).Select(PdfAnnotations.NormalizeType), StringComparer.OrdinalIgnoreCase);

        if (action is "list" or "read" or "get")
        {
            var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
            var bytes = await context.ReadPdfAsync(source, cancellationToken);
            var pages = PdfPageRange.Parse(arguments.GetPages(), PdfFiles.CountPages(bytes));
            var found = PdfAnnotations.List(bytes, password, pages, types, out var widgets);

            return DescribeList(source.Describe(), found, widgets);
        }

        if (action is not ("add" or "update" or "remove" or "delete"))
        {
            throw new PdfToolException($"'{action}' is not an action; use list, add, update or remove.");
        }

        var specs = arguments.Get<List<PdfAnnotationSpec>>("annotations") ?? [];
        var ids = arguments.GetStrings("ids", splitCommas: true);

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);
            var now = context.TimeProvider.GetUtcNow();
            var log = new List<string>();
            var warnings = new List<string>();
            byte[] result;

            switch (action)
            {
                case "add":
                    if (specs.Count == 0)
                    {
                        throw new PdfToolException("Pass 'annotations', for example [{\"type\": \"highlight\", \"text\": \"net revenue\", \"contents\": \"Check this figure\"}].");
                    }

                    result = PdfAnnotations.Add(bytes, password, specs, now, log, warnings);

                    break;

                case "update":
                    if (specs.Count == 0)
                    {
                        throw new PdfToolException("Pass 'annotations' with the 'id' of each annotation to change and the new values.");
                    }

                    result = PdfAnnotations.Update(bytes, password, specs, now, log);

                    break;

                default:
                    {
                        var pages = PdfPageRange.Parse(arguments.GetPages(), PdfFiles.CountPages(bytes));

                        result = PdfAnnotations.Remove(bytes, password, ids, types, pages, out var removed);
                        log.Add(removed == 0
                            ? "no annotation matched"
                            : string.Create(CultureInfo.InvariantCulture, $"removed {removed} annotation(s), with their replies"));

                        break;
                    }
            }

            if (ReferenceEquals(result, bytes))
            {
                return "Nothing was changed: " + string.Join("; ", log.Concat(warnings)) + ".";
            }

            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), result, "Annotations: " + string.Join("; ", log), cancellationToken);
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

            response.Append(target.IsUpload ? "The uploaded file was not changed. " : string.Empty)
                .Append("Annotations can still be moved or deleted in a viewer; flatten_pdf makes them part of the page.");

            return response.ToString();
        }, cancellationToken);
    }

    private static string DescribeList(string source, List<PdfAnnotationInfo> found, int widgets)
    {
        var builder = new StringBuilder();

        if (found.Count == 0)
        {
            builder.Append("There are no comments or marks in ").Append(source).Append('.');
        }
        else
        {
            builder.Append(found.Count.ToString(CultureInfo.InvariantCulture)).Append(" annotation(s) in ").Append(source).Append(": ")
                .AppendLine(string.Join(", ", found.GroupBy(item => item.Type).Select(group => $"{group.Count()} {group.Key}")));

            foreach (var item in found.Take(MaxListed))
            {
                builder.Append("- [").Append(item.Id).Append("] p. ").Append(item.Page).Append(' ').Append(item.Type);
                builder.Append(string.Create(CultureInfo.InvariantCulture, $" at x={item.Position.X}, y={item.Position.Y}, {item.Position.Width}×{item.Position.Height}"));

                if (!string.IsNullOrEmpty(item.Author))
                {
                    builder.Append(" by ").Append(item.Author);
                }

                if (!string.IsNullOrEmpty(item.MarkedText))
                {
                    builder.Append(" over \"").Append(item.MarkedText).Append('"');
                }

                if (!string.IsNullOrEmpty(item.Contents))
                {
                    builder.Append(": \"").Append(item.Contents.Length > 300 ? item.Contents[..299] + "…" : item.Contents).Append('"');
                }

                if (!string.IsNullOrEmpty(item.Target))
                {
                    builder.Append(" (").Append(item.Target).Append(')');
                }

                if (item.Replies > 0)
                {
                    builder.Append(" — ").Append(item.Replies).Append(" repl").Append(item.Replies == 1 ? "y" : "ies");
                }

                if (!string.IsNullOrEmpty(item.Color))
                {
                    builder.Append(", ").Append(item.Color);
                }

                builder.AppendLine();
            }

            if (found.Count > MaxListed)
            {
                builder.Append("… and ").Append(found.Count - MaxListed).AppendLine(" more; narrow with 'pages' or 'types'.");
            }
        }

        if (widgets > 0)
        {
            builder.AppendLine().Append(widgets).Append(" form-field widget(s) are not listed; get_pdf_form_fields reads them.");
        }

        return builder.ToString().TrimEnd();
    }
}
