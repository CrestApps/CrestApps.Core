using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Flattens form fields and annotations into page content.
/// </summary>
internal sealed class FlattenPdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.FlattenPdf;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "scope": { "type": "string", "enum": ["all", "forms", "annotations"], "description": "'all' (default) flattens form fields and annotations; 'forms' only form fields; 'annotations' only comments, highlights, stamps and shapes." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlattenPdfTool"/> class.
    /// </summary>
    public FlattenPdfTool()
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
    public override string Description => "Flattens a PDF: paints form field values and/or annotations (comments, highlights, stamps, shapes) into the page content and removes the interactive objects, so the result looks the same everywhere and can no longer be edited. Saves a working PDF.";

    /// <summary>
    /// Flattens the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var scope = (arguments.GetString("scope") ?? "all").Trim().ToLowerInvariant();
        var forms = scope is "all" or "forms" or "form";
        var annotations = scope is "all" or "annotations" or "annotation";

        if (!forms && !annotations)
        {
            throw new PdfToolException("'scope' must be all, forms or annotations.");
        }

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);

            using var document = PdfFiles.OpenForEditing(bytes, arguments.GetString("password"));

            var pages = arguments.GetPages() is null ? null : PdfPageRange.Parse(arguments.GetPages(), document.PageCount);
            var (fields, marks) = PdfFlattener.Flatten(document, forms, annotations, pages);

            if (fields + marks == 0)
            {
                return $"{target.Describe()} has no {(forms && annotations ? "form fields or annotations" : forms ? "form fields" : "annotations")} to flatten{(pages is null ? string.Empty : " on those pages")}; nothing was changed.";
            }

            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), PdfFiles.Save(document), $"Flattened {fields} field widget(s) and {marks} annotation(s)", cancellationToken);

            return $"Flattened {fields} form field widget(s) and {marks} annotation(s) into the pages, and saved working PDF \"{working.Name}\" ({working.PageCount} page(s)).{(target.IsUpload ? " The uploaded file was not changed." : string.Empty)} Preview with preview_pdf or deliver with export_pdf.";
        }, cancellationToken);
    }
}
