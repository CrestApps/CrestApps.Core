using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Structure;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Moves elements — paragraphs, tables, pictures, or a heading with its whole section — to another place.
/// </summary>
internal sealed class MoveWordContentTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.MoveWordContent;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            {{WordBlockSelection.Schema}},
            {{WordToolSchemas.Position}},
            {{WordToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="MoveWordContentTool"/> class.
    /// </summary>
    public MoveWordContentTool()
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
    public override string Description => "Moves elements of a Word document: elements by 'ids', a range ('from' to 'to'), or a heading with everything under it ('heading_with_content', to reorder sections), placed 'after' or 'before' another element or at the 'start' or 'end'. Elements keep their ids.";

    /// <summary>
    /// Moves the elements.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var after = arguments.GetString("after");
        var before = arguments.GetString("before");
        var at = arguments.GetString("at");

        if (after is null && before is null && at is null)
        {
            throw new WordToolException("Say where to move the content: 'after' or 'before' an element id, or 'at' start or end.");
        }

        var (count, document) = await context.EditAsync(arguments.Document(), "Moved content", edit =>
        {
            var blocks = WordBlockSelection.Read(edit.Package, arguments);

            if (blocks.Count == 0)
            {
                throw new WordToolException("Say what to move: 'ids', 'from' and 'to', or 'heading_with_content'.");
            }

            var anchorId = after ?? before;
            OpenXmlElement anchor = null;

            if (anchorId is not null)
            {
                anchor = WordSections.TopLevel(edit.Package.Body, WordBlockLocator.Require(edit.Package, anchorId));

                if (anchor is null || blocks.Contains(anchor))
                {
                    throw new WordToolException("The target position is inside the content being moved.");
                }
            }

            foreach (var block in blocks)
            {
                block.Remove();
            }

            if (anchor is not null)
            {
                var reference = anchor;

                foreach (var block in blocks)
                {
                    if (after is not null)
                    {
                        reference.InsertAfterSelf(block);
                        reference = block;
                    }
                    else
                    {
                        anchor.InsertBeforeSelf(block);
                    }
                }
            }
            else if (string.Equals(at, "start", StringComparison.OrdinalIgnoreCase))
            {
                for (var index = blocks.Count - 1; index >= 0; index--)
                {
                    edit.Package.Body.PrependChild(blocks[index]);
                }
            }
            else
            {
                var section = WordSections.EnsureBodySection(edit.Package.Body);

                foreach (var block in blocks)
                {
                    section.InsertBeforeSelf(block);
                }
            }

            return Task.FromResult(blocks.Count);
        }, arguments.SaveAs(), cancellationToken);

        return $"Moved {count} element(s) in \"{document.Name}\" (version {document.Version}). Call get_word_document_outline or preview_word to check the new order.";
    }
}
