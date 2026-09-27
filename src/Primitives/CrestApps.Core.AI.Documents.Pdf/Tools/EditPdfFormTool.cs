using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Adds, changes and removes form fields — creating a fillable form from a static one, or tidying an existing
/// one.
/// </summary>
internal sealed class EditPdfFormTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.EditPdfForm;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "changes": {
              "type": "array",
              "description": "Changes run in order.",
              "items": {
                "type": "object",
                "properties": {
                  "action": { "type": "string", "enum": ["add", "update", "remove"] },
                  "type": { "type": "string", "enum": ["text", "multiline", "password", "checkbox", "radio", "dropdown", "listbox", "signature"], "description": "add: the kind of field." },
                  "name": { "type": "string", "description": "The field's name (for update and remove, as get_pdf_form_fields lists it)." },
                  "new_name": { "type": "string", "description": "update: rename the field." },
                  "page": { "type": "integer", "description": "add: one-based page." },
                  "x": { "type": "number", "description": "add: distance from the left edge, in points (72 per inch)." },
                  "y": { "type": "number", "description": "add: distance from the top edge, in points." },
                  "width": { "type": "number" },
                  "height": { "type": "number", "description": "For a radio group, the size of each button; buttons are stacked down the page with their labels." },
                  "options": { "type": "array", "items": { "type": "string" }, "description": "Choices of a radio group, dropdown or list box." },
                  "value": { "type": "string", "description": "add: initial value." },
                  "default_value": { "type": "string" },
                  "required": { "type": "boolean" },
                  "read_only": { "type": "boolean" },
                  "tooltip": { "type": "string", "description": "The field's label for assistive technology and hover text." },
                  "max_length": { "type": "integer" },
                  "font_size": { "type": "number", "description": "0 sizes the text to the field." },
                  "editable": { "type": "boolean", "description": "dropdown: also accept typed text." }
                },
                "required": ["action", "name"]
              }
            }
          },
          "required": ["changes"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="EditPdfFormTool"/> class.
    /// </summary>
    public EditPdfFormTool()
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
    public override string Description => "Creates and edits interactive form fields: add text, multi-line, password, checkbox, radio-group, dropdown, list-box and signature fields at a position on a page; update a field's name, tooltip, required/read-only flags, choices, max length or default; remove fields. Positions are points from the top-left of the page (use search_pdf or get_pdf_page_content to find where labels are). Saves a working PDF.";

    /// <summary>
    /// Applies the changes.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var changes = arguments.Get<List<PdfFormFieldSpec>>("changes");

        if (changes is not { Count: > 0 })
        {
            throw new PdfToolException("Pass 'changes', for example [{\"action\": \"add\", \"type\": \"text\", \"name\": \"full_name\", \"page\": 1, \"x\": 150, \"y\": 200}].");
        }

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);

            using var document = PdfFiles.OpenForEditing(bytes, arguments.GetString("password"));

            var log = new List<string>();

            foreach (var change in changes)
            {
                if (change is null)
                {
                    continue;
                }

                switch (change.Action?.Trim().ToLowerInvariant())
                {
                    case "add" or "create":
                        {
                            var name = PdfFormFields.Add(document, change);
                            log.Add($"added {change.Type ?? "text"} field \"{name}\" on page {change.Page ?? 1}");

                            break;
                        }

                    case "update" or "edit" or "rename":
                        {
                            var field = PdfFormFields.Find(PdfFormFields.Read(document), change.Name)
                                ?? throw new PdfToolException($"There is no field named \"{change.Name}\".");
                            var updates = PdfFormFields.Update(document, field, change);

                            log.Add(updates.Count == 0
                                ? $"nothing to change on \"{field.Name}\""
                                : $"updated \"{field.Name}\": {string.Join(", ", updates)}");

                            break;
                        }

                    case "remove" or "delete":
                        {
                            var field = PdfFormFields.Find(PdfFormFields.Read(document), change.Name)
                                ?? throw new PdfToolException($"There is no field named \"{change.Name}\".");

                            PdfFormFields.Remove(document, field);
                            log.Add($"removed \"{field.Name}\"");

                            break;
                        }

                    default:
                        throw new PdfToolException($"'{change.Action}' is not an action; use add, update or remove.");
                }
            }

            var total = PdfFormFields.Read(document).Count;
            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), PdfFiles.Save(document), "Form: " + string.Join("; ", log), cancellationToken);
            var response = new StringBuilder();

            response.Append("Saved working PDF \"").Append(working.Name).Append("\" with ").Append(total).AppendLine(" form field(s):");

            foreach (var line in log)
            {
                response.Append("- ").AppendLine(line);
            }

            response.Append(target.IsUpload ? "The uploaded file was not changed. " : string.Empty).Append("Preview with preview_pdf to check the positions.");

            return response.ToString();
        }, cancellationToken);
    }
}
