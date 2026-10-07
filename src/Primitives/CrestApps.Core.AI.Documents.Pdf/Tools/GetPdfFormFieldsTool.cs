using System.Globalization;
using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Lists a PDF's form fields with their types, values, choices and constraints.
/// </summary>
internal sealed class GetPdfFormFieldsTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.GetPdfFormFields;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            "format": { "type": "string", "enum": ["text", "json"], "description": "'text' (default) or 'json'." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetPdfFormFieldsTool"/> class.
    /// </summary>
    public GetPdfFormFieldsTool()
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
    public override string Description => "Lists the interactive form fields of a PDF: full name, type (text, checkbox, radio, dropdown, listbox, signature, button), current and default value, choices, required, read-only, max length, tooltip, page and position. Call it before fill_pdf_form so the values use the exact field names and choices.";

    /// <summary>
    /// Lists the fields.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);

        using var document = PdfFiles.OpenForImport(bytes, arguments.GetString("password"));

        var fields = PdfFormFields.Read(document);

        if (fields.Count == 0)
        {
            return $"{source.Describe()} has no form fields. edit_pdf_form can add some.";
        }

        if (string.Equals(arguments.GetString("format"), "json", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(fields.Select(field => Describe(field, document)), _jsonOptions);
        }

        var builder = new StringBuilder();

        builder.Append(source.Describe()).Append(" has ").Append(fields.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" form field(s):");

        foreach (var field in fields)
        {
            var description = Describe(field, document);

            builder.Append("- \"").Append(description.Name).Append("\" ").Append(description.Type);

            if (description.Value is not null)
            {
                builder.Append(" = \"").Append(description.Value).Append('"');
            }

            if (description.Options is { Count: > 0 })
            {
                builder.Append("; choices: ").Append(string.Join(", ", description.Options));
            }

            var flags = new List<string>();

            if (description.Required == true)
            {
                flags.Add("required");
            }

            if (description.ReadOnly == true)
            {
                flags.Add("read-only");
            }

            if (description.MaxLength is > 0)
            {
                flags.Add($"max {description.MaxLength} chars");
            }

            if (flags.Count > 0)
            {
                builder.Append(" (").Append(string.Join(", ", flags)).Append(')');
            }

            if (!string.IsNullOrWhiteSpace(description.Tooltip))
            {
                builder.Append("; label \"").Append(description.Tooltip).Append('"');
            }

            if (description.Page is not null)
            {
                builder.Append("; page ").Append(description.Page).Append(" at ").Append(description.Position);
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static FieldDescription Describe(PdfFormFieldInfo field, PdfSharp.Pdf.PdfDocument document)
    {
        string position = null;

        if (field.Rectangle is not null && field.Page is { } page)
        {
            var visible = document.Pages[page - 1].EffectiveCropBoxReadOnly;

            position = string.Create(
                CultureInfo.InvariantCulture,
                $"x={Math.Round(field.Rectangle.X1 - visible.X1, 1)}, y={Math.Round(visible.Y2 - field.Rectangle.Y2, 1)}, w={Math.Round(field.Rectangle.Width, 1)}, h={Math.Round(field.Rectangle.Height, 1)}");
        }

        return new FieldDescription(
            field.Name,
            field.Type,
            string.IsNullOrEmpty(field.Value) ? null : field.Value,
            string.IsNullOrEmpty(field.DefaultValue) ? null : field.DefaultValue,
            field.Type is "checkbox" ? null : field.Options.Count > 0 ? field.Options : null,
            field.Required ? true : null,
            field.ReadOnly ? true : null,
            field.MaxLength,
            field.Tooltip,
            field.Page,
            position);
    }

    private sealed record FieldDescription(
        string Name,
        string Type,
        string Value,
        string DefaultValue,
        List<string> Options,
        bool? Required,
        bool? ReadOnly,
        int? MaxLength,
        string Tooltip,
        int? Page,
        string Position);
}
