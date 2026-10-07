using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Starts a new composed PDF in the conversation's workspace: its name, page setup, look and first content.
/// </summary>
internal sealed class CreatePdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.CreatePdf;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "Name of the new working PDF, used by later calls and as the default file name. Defaults to the title." },
            "replace": { "type": "boolean", "description": "Start over when a working PDF with this name already exists. Otherwise the call fails, so an earlier document is never lost by accident." },
            {{PdfCompositionSchemas.Metadata}},
            {{PdfCompositionSchemas.PageSetup}},
            {{PdfCompositionSchemas.Theme}},
            {{PdfCompositionSchemas.RunningHeads}},
            {{PdfCompositionSchemas.FrontMatter}},
            {{PdfCompositionSchemas.Blocks}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreatePdfTool"/> class.
    /// </summary>
    public CreatePdfTool()
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
    public override string Description => "Starts a new PDF document to build step by step: title and metadata, page setup (size, orientation, margins), theme (brand colours, fonts, logo, table style), header, footer, page numbers, cover page, table of contents, watermark, and optionally its first content blocks. The document stays in the workspace for follow-ups: add content with add_pdf_content, change its look with format_pdf, show it with preview_pdf, deliver it with export_pdf.";

    /// <summary>
    /// Creates the document.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var title = arguments.GetString("title");
        var name = PdfToolContext.SanitizeName(arguments.GetString("name") ?? title ?? "document");
        var replace = arguments.GetBoolean("replace") == true;

        return await context.MutateAsync(async state =>
        {
            var existing = state.Find(name);

            if (existing is not null && !replace)
            {
                throw new PdfToolException($"A working PDF named \"{name}\" already exists. Change it with add_pdf_content or format_pdf, choose another name, or pass replace: true to start it over.");
            }

            if (existing is null && state.Documents.Count >= context.Options.MaxWorkingDocuments)
            {
                throw new PdfToolException($"This conversation already holds {state.Documents.Count} working PDFs, the most it keeps. Reuse a name with replace: true.");
            }

            var definition = new PdfDocumentDefinition
            {
                Sections = [new PdfSectionDefinition { Id = "s1" }],
                NextSectionNumber = 2,
            };

            PdfFormattingArguments.Apply(definition, arguments, replace: true);

            var blocks = arguments.Get<List<PdfBlockDefinition>>("blocks") ?? [];
            var warnings = await PdfBlockPreparer.PrepareAsync(definition, blocks, arguments.Services, cancellationToken);

            definition.Sections[0].Blocks.AddRange(blocks.Where(block => block is not null));

            var document = existing ?? new PdfWorkingDocument { Name = name };

            if (!string.IsNullOrEmpty(document.BlobPath))
            {
                await context.DeleteBlobAsync(document.BlobPath);
                document.BlobPath = null;
            }

            document.Kind = PdfWorkingDocument.ComposedKind;
            document.Definition = definition;
            document.ByteLength = 0;
            document.Version++;
            document.UpdatedUtc = context.TimeProvider.GetUtcNow().UtcDateTime;
            document.History = ["Created with create_pdf"];

            if (existing is null)
            {
                state.Documents.Add(document);
            }

            state.ActiveDocument = document.Name;

            var render = await context.RenderAsync(document, cancellationToken);

            document.PageCount = render.PageCount;

            var response = new StringBuilder();

            response.Append("Created working PDF \"").Append(document.Name).Append("\". ").AppendLine(PdfCompositionDescriber.DescribeRender(render));

            foreach (var warning in warnings)
            {
                response.Append("- ").AppendLine(warning);
            }

            response.AppendLine().AppendLine(PdfCompositionDescriber.DescribeFormatting(definition));
            response.AppendLine().AppendLine(PdfCompositionDescriber.DescribeBlocks(definition));
            response.AppendLine().Append("Next: add content with add_pdf_content, then preview_pdf to show it and export_pdf to deliver it.");

            return response.ToString();
        }, cancellationToken);
    }
}
