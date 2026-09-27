using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Fills a PDF's form fields and saves the filled form as a working copy.
/// </summary>
internal sealed class FillPdfFormTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.FillPdfForm;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "values": {
              "type": "object",
              "description": "Field name → value. Checkboxes take true/false; radio groups, dropdowns and list boxes take one of their choices. Use the names get_pdf_form_fields returns.",
              "additionalProperties": { "type": ["string", "number", "boolean"] }
            },
            "flatten": { "type": "boolean", "description": "Flatten the form after filling, so the values can no longer be changed." },
            "override_read_only": { "type": "boolean", "description": "Also fill fields marked read-only. Only when the user explicitly asks." }
          },
          "required": ["values"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="FillPdfFormTool"/> class.
    /// </summary>
    public FillPdfFormTool()
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
    public override string Description => "Fills the form fields of an uploaded or working PDF with the values given (field name → value) and saves the filled form as a working PDF; the upload is never changed. Values only come from the user or from documents they provided — never invent them. Optionally flattens the result. Reports every field filled, every name not found, and every value a field would not take.";

    /// <summary>
    /// Fills the form.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetElement("values", out var values) || values.ValueKind != JsonValueKind.Object)
        {
            throw new PdfToolException("Pass 'values', an object of field name → value.");
        }

        var overrideReadOnly = arguments.GetBoolean("override_read_only") == true;

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);

            using var document = PdfFiles.OpenForEditing(bytes, arguments.GetString("password"));

            var fields = PdfFormFields.Read(document);

            if (fields.Count == 0)
            {
                throw new PdfToolException($"{target.Describe()} has no form fields to fill. edit_pdf_form can add fields, or edit_pdf_content can write text onto the page.");
            }

            var filled = new List<string>();
            var problems = new List<string>();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in values.EnumerateObject())
            {
                var field = PdfFormFields.Find(fields, property.Name);

                if (field is null)
                {
                    problems.Add($"no field named \"{property.Name}\"{Suggest(fields, property.Name)}");

                    continue;
                }

                // A loosely matched name can land on a field another entry already filled; the first value
                // given for a field is the one kept, rather than whichever happened to come last.
                if (!set.Add(field.Name))
                {
                    problems.Add($"\"{property.Name}\" names \"{field.Name}\", which was already filled in this call; the first value was kept");

                    continue;
                }

                if (field.ReadOnly && !overrideReadOnly)
                {
                    problems.Add($"\"{field.Name}\" is read-only and was left as it is");

                    continue;
                }

                try
                {
                    var assigned = PdfFormFields.SetValue(document, field, ToText(property.Value));
                    filled.Add($"\"{field.Name}\" = {assigned}");
                }
                catch (PdfToolException ex)
                {
                    problems.Add(ex.Message);
                }
            }

            if (filled.Count == 0)
            {
                throw new PdfToolException("No field was filled: " + string.Join("; ", problems) + ".");
            }

            // Read before the document is saved: PDFsharp does not allow a saved document to be read again.
            var empty = PdfFormFields.Read(document)
                .Where(field => field.Required && string.IsNullOrEmpty(field.Value) && field.Type is not "signature")
                .Select(field => field.Name)
                .ToList();

            var flattened = false;

            if (arguments.GetBoolean("flatten") == true)
            {
                PdfFlattener.Flatten(document, forms: true, annotations: false);
                flattened = true;
            }

            var working = await context.SaveWorkingFileAsync(
                state,
                target,
                arguments.GetString("save_as"),
                PdfFiles.Save(document),
                $"Filled {filled.Count} form field(s){(flattened ? " and flattened the form" : string.Empty)}",
                cancellationToken);

            var response = new StringBuilder();

            response.Append("Filled ").Append(filled.Count).Append(" field(s) and saved working PDF \"").Append(working.Name).AppendLine("\":");

            foreach (var line in filled)
            {
                response.Append("- ").AppendLine(line);
            }

            if (problems.Count > 0)
            {
                response.AppendLine("Not filled:");

                foreach (var problem in problems)
                {
                    response.Append("- ").AppendLine(problem);
                }
            }

            if (!flattened && empty.Count > 0)
            {
                response.Append("Required fields still empty: ").AppendLine(string.Join(", ", empty));
            }

            if (flattened)
            {
                response.AppendLine("The form was flattened; its values are now part of the pages.");
            }

            response.Append(target.IsUpload ? "The uploaded file was not changed. " : string.Empty).Append("Preview with preview_pdf or deliver with export_pdf.");

            return response.ToString();
        }, cancellationToken);
    }

    private static string ToText(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => value.GetRawText(),
        };
    }

    private static string Suggest(List<PdfFormFieldInfo> fields, string name)
    {
        var suggestions = fields
            .Select(field => (field.Name, Distance: Levenshtein(field.Name.ToLowerInvariant(), name.ToLowerInvariant())))
            .Where(candidate => candidate.Distance <= Math.Max(3, name.Length / 3))
            .OrderBy(candidate => candidate.Distance)
            .Take(3)
            .Select(candidate => $"\"{candidate.Name}\"")
            .ToList();

        return suggestions.Count == 0
            ? string.Empty
            : " (did you mean " + string.Join(" or ", suggestions) + "?)";
    }

    private static int Levenshtein(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
