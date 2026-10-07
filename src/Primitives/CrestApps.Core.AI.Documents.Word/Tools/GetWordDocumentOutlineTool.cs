using System.Text;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Rendering;
using CrestApps.Core.AI.Documents.Word.Workspace;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Returns a document's outline: its title and headings as a tree, with their ids, sections and page numbers.
/// </summary>
internal sealed class GetWordDocumentOutlineTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.GetWordDocumentOutline;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "max_level": { "type": "integer", "description": "Deepest heading level listed. Default 6." },
            "page_numbers": { "type": "boolean", "description": "Show the page each heading starts on." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetWordDocumentOutlineTool"/> class.
    /// </summary>
    public GetWordDocumentOutlineTool()
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
    public override string Description => "Returns a Word document's outline: the title and the headings as an indented tree with their ids, where each section starts, and optionally the page each heading is on. Use it to understand or navigate a long document, and before moving sections with move_word_content.";

    /// <summary>
    /// Writes the outline.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var source = await context.FindDocumentAsync(arguments.Document(), cancellationToken);
        var maxLevel = Math.Clamp(arguments.GetInt("max_level") ?? 6, 1, 9);
        using var package = await context.OpenAsync(source, cancellationToken);

        var blocks = WordBlockReader.Read(package);
        var layout = arguments.GetBoolean("page_numbers") == true ? WordPreview.Layout(package, context.Services) : null;
        var answer = new StringBuilder();
        var previousLevel = 0;
        var skipped = new List<string>();
        var headings = 0;

        answer.Append("Outline of ").Append(source.Describe()).AppendLine(":");

        var section = 0;

        foreach (var block in blocks)
        {
            if (block.Section != section)
            {
                section = block.Section;

                if (section > 1 || blocks[^1].Section > 1)
                {
                    answer.Append("--- Section ").Append(section).AppendLine(" ---");
                }
            }

            if (!block.IsHeading || block.Level > maxLevel)
            {
                continue;
            }

            headings++;

            var indent = new string(' ', Math.Max(0, block.Level - 1) * 2);
            var label = block.Kind == WordBlockKind.Title ? "Title" : "H" + block.Level;

            answer.Append(indent).Append('[').Append(block.Id).Append("] ").Append(label).Append(": ").Append(WordText.Clip(block.Text, 120));

            if (layout?.DisplayNumberOf(block.Element) is { Length: > 0 } page)
            {
                answer.Append(" (page ").Append(page).Append(')');
            }

            answer.AppendLine();

            if (block.Kind == WordBlockKind.Heading && block.Level > previousLevel + 1 && previousLevel > 0)
            {
                skipped.Add($"[{block.Id}] jumps from heading {previousLevel} to heading {block.Level}");
            }

            if (block.Kind == WordBlockKind.Heading)
            {
                previousLevel = block.Level;
            }
        }

        if (headings == 0)
        {
            answer.AppendLine("The document has no headings; turn paragraphs into headings with update_word_content level.");
        }

        if (skipped.Count > 0)
        {
            answer.Append("Skipped heading levels: ").AppendJoin("; ", skipped).AppendLine(".");
        }

        return answer.ToString().TrimEnd();
    }
}
