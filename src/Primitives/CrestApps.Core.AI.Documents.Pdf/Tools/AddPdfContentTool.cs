using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Adds, inserts, replaces or removes content blocks of a composed PDF.
/// </summary>
/// <remarks>
/// A document is built and revised through here one request at a time. Blocks carry identifiers, so "change
/// the second chart" or "drop the appendix" is one targeted call rather than a rebuild of the whole document
/// from a restated description.
/// </remarks>
internal sealed class AddPdfContentTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.AddPdfContent;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            "operation": {
              "type": "string",
              "enum": ["append", "insert_before", "insert_after", "replace", "remove", "clear"],
              "description": "append (default) adds the blocks at the end (of 'section' when given); insert_before/insert_after place them around the 'target' block; replace swaps the 'target' block for the blocks; remove deletes the 'target' blocks; clear removes all content."
            },
            "target": { "type": "string", "description": "Block id such as 'b7' for insert, replace and remove. For remove, several ids may be separated by commas." },
            "section": { "type": "string", "description": "Section id to append to. Defaults to the last section." },
            "new_section": {
              "type": "object",
              "description": "Start a new section (on a new page) for these blocks, optionally with its own page setup, for example a landscape page for a wide table.",
              "properties": {
                {{PdfCompositionSchemas.PageSetup}}
              }
            },
            {{PdfCompositionSchemas.Blocks}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddPdfContentTool"/> class.
    /// </summary>
    public AddPdfContentTool()
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
    public override string Description => "Adds content to a PDF being composed, or revises it: headings, paragraphs with inline Markdown, Markdown, lists, tables (with number formats, totals, highlighted cells, or rows read from uploaded spreadsheets), images, charts (from values, uploaded tabular data, or a [chart:…] marker), callouts, key-value facts, quotes, code, rules, spacers, page breaks and signature lines. Every block gets an id (b1, b2, …) so a follow-up can insert before/after, replace or remove it. Put all blocks of a request in one call.";

    /// <summary>
    /// Changes the document's content.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var operation = (arguments.GetString("operation") ?? "append").Trim().ToLowerInvariant();
        var target = arguments.GetString("target");

        return await context.MutateAsync(async state =>
        {
            var document = PdfCompositionDescriber.RequireComposed(context.FindPdf(state, arguments.Pdf()));
            var definition = document.Definition ??= new PdfDocumentDefinition();

            if (definition.Sections.Count == 0)
            {
                definition.Sections.Add(new PdfSectionDefinition { Id = "s" + definition.NextSectionNumber++.ToString(CultureInfo.InvariantCulture) });
            }

            var blocks = arguments.Get<List<PdfBlockDefinition>>("blocks") ?? [];

            blocks.RemoveAll(block => block is null);

            if (operation is "append" or "insert_before" or "insert_after" or "replace" && blocks.Count == 0)
            {
                throw new PdfToolException($"The '{operation}' operation needs 'blocks'.");
            }

            string summary;
            List<string> warnings = [];

            switch (operation)
            {
                case "append":
                    {
                        var section = ResolveSection(definition, arguments);
                        warnings = await PdfBlockPreparer.PrepareAsync(definition, blocks, arguments.Services, cancellationToken);
                        section.Blocks.AddRange(blocks);
                        summary = $"Added {Describe(blocks)} to section {section.Id}.";

                        break;
                    }

                case "insert_before":
                case "insert_after":
                    {
                        var (section, index) = FindBlock(definition, target);
                        warnings = await PdfBlockPreparer.PrepareAsync(definition, blocks, arguments.Services, cancellationToken);
                        section.Blocks.InsertRange(operation == "insert_before" ? index : index + 1, blocks);
                        summary = $"Inserted {Describe(blocks)} {(operation == "insert_before" ? "before" : "after")} {target}.";

                        break;
                    }

                case "replace":
                    {
                        var (section, index) = FindBlock(definition, target);
                        var replacedId = section.Blocks[index].Id;
                        warnings = await PdfBlockPreparer.PrepareAsync(definition, blocks, arguments.Services, cancellationToken);

                        // A one-for-one replacement keeps the id, so the block can still be referred to by the
                        // name the model already knows.
                        if (blocks.Count == 1)
                        {
                            blocks[0].Id = replacedId;
                        }

                        section.Blocks.RemoveAt(index);
                        section.Blocks.InsertRange(index, blocks);
                        summary = $"Replaced {replacedId} with {Describe(blocks)}.";

                        break;
                    }

                case "remove":
                    {
                        var ids = (target ?? string.Empty).Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        if (ids.Length == 0)
                        {
                            throw new PdfToolException("The 'remove' operation needs 'target' with one or more block ids.");
                        }

                        foreach (var id in ids)
                        {
                            var (section, index) = FindBlock(definition, id);
                            section.Blocks.RemoveAt(index);
                        }

                        // A section emptied by the removal is dropped, and a document always keeps one.
                        definition.Sections.RemoveAll(section => section.Blocks.Count == 0);

                        if (definition.Sections.Count == 0)
                        {
                            definition.Sections.Add(new PdfSectionDefinition { Id = "s" + definition.NextSectionNumber++.ToString(CultureInfo.InvariantCulture) });
                        }

                        summary = $"Removed {string.Join(", ", ids)}.";

                        break;
                    }

                case "clear":
                    definition.Sections = [new PdfSectionDefinition { Id = "s" + definition.NextSectionNumber++.ToString(CultureInfo.InvariantCulture) }];
                    summary = "Removed all content; the formatting is kept.";

                    break;

                default:
                    throw new PdfToolException($"'{operation}' is not an operation. Use append, insert_before, insert_after, replace, remove or clear.");
            }

            document.Version++;
            document.UpdatedUtc = context.TimeProvider.GetUtcNow().UtcDateTime;
            document.History.Add(summary);
            state.ActiveDocument = document.Name;

            var render = await context.RenderAsync(document, cancellationToken);
            document.PageCount = render.PageCount;

            var response = new StringBuilder();

            response.Append("Updated working PDF \"").Append(document.Name).Append("\": ").Append(summary).Append(' ').AppendLine(PdfCompositionDescriber.DescribeRender(render));

            foreach (var warning in warnings)
            {
                response.Append("- ").AppendLine(warning);
            }

            response.AppendLine().AppendLine(PdfCompositionDescriber.DescribeBlocks(definition));
            response.AppendLine().Append("Preview with preview_pdf before exporting.");

            return response.ToString();
        }, cancellationToken);
    }

    private static PdfSectionDefinition ResolveSection(PdfDocumentDefinition definition, PdfToolArguments arguments)
    {
        if (arguments.TryGetElement("new_section", out var element) && element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var section = new PdfSectionDefinition
            {
                Id = "s" + definition.NextSectionNumber++.ToString(CultureInfo.InvariantCulture),
                PageSetup = element.TryGetProperty("page_setup", out var setup)
                    ? PdfDefinitionJson.FromElement<PdfPageSetupDefinition>(setup)
                    : null,
            };

            definition.Sections.Add(section);

            return section;
        }

        var id = arguments.GetString("section");

        if (string.IsNullOrWhiteSpace(id))
        {
            return definition.Sections[^1];
        }

        return definition.Sections.FirstOrDefault(section => string.Equals(section.Id, id.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new PdfToolException($"There is no section \"{id}\". Sections: {string.Join(", ", definition.Sections.Select(section => section.Id))}.");
    }

    private static (PdfSectionDefinition Section, int Index) FindBlock(PdfDocumentDefinition definition, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new PdfToolException("This operation needs 'target', the id of a block such as 'b3'.");
        }

        var trimmed = id.Trim();

        foreach (var section in definition.Sections)
        {
            var index = section.Blocks.FindIndex(block => string.Equals(block.Id, trimmed, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                return (section, index);
            }
        }

        throw new PdfToolException($"There is no block \"{trimmed}\". " + PdfCompositionDescriber.DescribeBlocks(definition, 40));
    }

    private static string Describe(List<PdfBlockDefinition> blocks)
    {
        return blocks.Count == 1
            ? blocks[0].Id
            : $"{blocks.Count} blocks ({blocks[0].Id}–{blocks[^1].Id})";
    }
}
