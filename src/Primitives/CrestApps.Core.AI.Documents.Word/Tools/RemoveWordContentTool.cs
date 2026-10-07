using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Structure;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Removes elements, a range of elements, a heading with its content, a whole section, or a working document.
/// </summary>
internal sealed class RemoveWordContentTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.RemoveWordContent;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            {{WordBlockSelection.Schema}},
            "section": { "type": "integer", "description": "Remove a whole section (one-based) with its section break." },
            "scope": { "type": "string", "enum": ["elements", "document"], "description": "'document' deletes the working document itself from the workspace; uploads are never deleted." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveWordContentTool"/> class.
    /// </summary>
    public RemoveWordContentTool()
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
    public override string Description => "Removes content from a Word document: elements by 'ids', a range ('from' to 'to'), a heading with everything under it ('heading_with_content'), or a whole 'section'. Section layouts survive the removal. When tracked changes are on, removed paragraphs are marked as deletions instead. With scope 'document' it deletes a working document from the workspace (uploads are never deleted).";

    /// <summary>
    /// Removes the content.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        if (string.Equals(arguments.GetString("scope"), "document", StringComparison.OrdinalIgnoreCase))
        {
            return await RemoveDocumentAsync(arguments, context, cancellationToken);
        }

        var section = arguments.GetInt("section");

        var (summary, document) = await context.EditAsync(arguments.Document(), "Removed content", edit =>
        {
            // An id inside a table cell or a content control removes that paragraph or table alone.
            var blocks = section is { } number ? WordBlockSelection.Section(edit.Package, number) : WordBlockSelection.Read(edit.Package, arguments, nested: true);

            if (blocks.Count == 0)
            {
                throw new WordToolException("Say what to remove: 'ids', 'from' and 'to', 'heading_with_content' or 'section'.");
            }

            var tracked = WordRevisions.IsTracking(edit.Package);
            var revisions = tracked ? WordRevisions.For(edit.Package, edit.Author, edit.Now) : null;
            var removed = 0;
            var notes = new List<string>();

            foreach (var block in blocks)
            {
                var sectionProperties = (block as Paragraph)?.ParagraphProperties?.SectionProperties;

                if (revisions is not null && block is Paragraph paragraph)
                {
                    revisions.MarkDeleted(paragraph);
                    removed++;

                    continue;
                }

                if (sectionProperties is not null && section is null)
                {
                    // The paragraph ends a section: its layout moves to the paragraph before it, so the section keeps it.
                    WordSections.KeepSectionBreak(edit.Package, (Paragraph)block, blocks);
                }

                if (revisions is not null)
                {
                    notes.Add("Tables cannot be removed as a tracked change here; they were removed outright.");
                }

                var parent = block.Parent;

                block.Remove();
                removed++;

                // A cell or a content control keeps a paragraph at its end.
                if (parent is not null and not Body)
                {
                    WordBlockLocator.RepairCell(edit.Package, parent as TableCell ?? parent.Ancestors<TableCell>().FirstOrDefault());

                    if (parent is SdtContentBlock content && !content.Elements<Paragraph>().Any() && !content.Elements<Table>().Any())
                    {
                        var empty = new Paragraph();

                        edit.Package.Ids.Assign(empty);
                        content.Append(empty);
                    }
                }
            }

            if (section is not null && blocks.Count > 0 && blocks[^1] is not Paragraph { ParagraphProperties.SectionProperties: not null })
            {
                // The last section was removed: the section before it now ends the document, so its layout becomes the body's.
                var previous = edit.Package.Body.Elements<Paragraph>().LastOrDefault(item => item.ParagraphProperties?.SectionProperties is not null);

                if (previous is not null)
                {
                    var properties = previous.ParagraphProperties.SectionProperties;

                    properties.Remove();
                    WordSections.EnsureBodySection(edit.Package.Body).Remove();
                    edit.Package.Body.Append(properties);
                }
            }

            if (!edit.Package.Body.Elements<Paragraph>().Any() && !edit.Package.Body.Elements<Table>().Any())
            {
                WordSections.EnsureBodySection(edit.Package.Body).InsertBeforeSelf(new Paragraph());
            }

            var verb = tracked ? "marked as deleted" : "removed";

            return Task.FromResult($"{removed} element(s) {verb}." + (notes.Count > 0 ? " " + notes.Distinct().First() : string.Empty));
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {summary}";
    }

    private static async Task<string> RemoveDocumentAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var name = arguments.Document() ?? throw new WordToolException("Pass 'document' with the working document to delete.");

        return await context.MutateAsync(async state =>
        {
            var source = context.FindDocument(state, name);

            if (source.IsUpload)
            {
                throw new WordToolException($"\"{source.Name}\" is an upload; uploads are never deleted.");
            }

            await context.RemoveDocumentAsync(state, source.Working);

            return $"Deleted working document \"{source.Name}\" from the workspace.";
        }, cancellationToken);
    }
}
