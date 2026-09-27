using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Records how a composed PDF looks: theme, page setup, running heads, page numbers, cover page, table of
/// contents, watermark and metadata.
/// </summary>
/// <remarks>
/// The formatting is remembered with the document, and a call merges into what is already there, so a
/// follow-up such as "make the headings green" states only that and keeps everything the user asked for
/// before.
/// </remarks>
internal sealed class FormatPdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.FormatPdf;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfCompositionSchemas.Metadata}},
            {{PdfCompositionSchemas.PageSetup}},
            {{PdfCompositionSchemas.Theme}},
            {{PdfCompositionSchemas.RunningHeads}},
            {{PdfCompositionSchemas.FrontMatter}},
            "section": { "type": "string", "description": "Apply 'page_setup' to this section only (for example 's2'), instead of the whole document." },
            "replace": { "type": "boolean", "description": "Replace each area given (theme, header, …) instead of merging into what is recorded." },
            "clear": {
              "type": "array",
              "items": { "type": "string", "enum": ["theme", "page_setup", "header", "footer", "page_numbers", "cover_page", "table_of_contents", "watermark"] },
              "description": "Areas to remove."
            }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="FormatPdfTool"/> class.
    /// </summary>
    public FormatPdfTool()
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
    public override string Description => "Changes how a PDF being composed looks, for the whole document: theme (brand colours, fonts, logo, table style), page setup (size, orientation, margins — or one section's), header and footer text with {page}/{pages}/{title}/{date} tokens, page numbers (position, style, start), cover page, table of contents, watermark, title/author/subject/keywords/language metadata and PDF/A. Remembered for follow-ups: pass only what changes; use 'clear' to remove an area.";

    /// <summary>
    /// Applies the formatting.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        return await context.MutateAsync(async state =>
        {
            var document = PdfCompositionDescriber.RequireComposed(context.FindPdf(state, arguments.Pdf()));
            var definition = document.Definition ??= new PdfDocumentDefinition();
            var replace = arguments.GetBoolean("replace") == true;
            var sectionId = arguments.GetString("section");
            var changed = new List<string>();

            if (!string.IsNullOrWhiteSpace(sectionId) && arguments.TryGetElement("page_setup", out var setup))
            {
                var section = definition.Sections.FirstOrDefault(candidate => string.Equals(candidate.Id, sectionId.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? throw new PdfToolException($"There is no section \"{sectionId}\". Sections: {string.Join(", ", definition.Sections.Select(candidate => candidate.Id))}.");

                section.PageSetup = replace
                    ? PdfDefinitionJson.FromElement<PdfPageSetupDefinition>(setup)
                    : PdfDefinitionJson.Merge(section.PageSetup, setup);

                changed.Add($"page setup of section {section.Id}");

                // The section's page setup is handled; the document's is left alone.
                changed.AddRange(PdfFormattingArguments.Apply(definition, arguments, replace, includePageSetup: false));
            }
            else
            {
                changed.AddRange(PdfFormattingArguments.Apply(definition, arguments, replace));
            }

            if (changed.Count == 0)
            {
                throw new PdfToolException("Nothing to change was given. Pass theme, page_setup, header, footer, page_numbers, cover_page, table_of_contents, watermark, metadata, or clear.");
            }

            document.Version++;
            document.UpdatedUtc = context.TimeProvider.GetUtcNow().UtcDateTime;
            document.History.Add("Formatted: " + string.Join(", ", changed));
            state.ActiveDocument = document.Name;

            var render = await context.RenderAsync(document, cancellationToken);
            document.PageCount = render.PageCount;

            var response = new StringBuilder();

            response.Append("Updated the formatting of working PDF \"").Append(document.Name).Append("\" (").Append(string.Join(", ", changed)).Append("). ")
                .AppendLine(PdfCompositionDescriber.DescribeRender(render));
            response.AppendLine().AppendLine("Formatting now recorded:").AppendLine(PdfCompositionDescriber.DescribeFormatting(definition));
            response.AppendLine().Append("Preview with preview_pdf before exporting.");

            return response.ToString();
        }, cancellationToken);
    }
}
