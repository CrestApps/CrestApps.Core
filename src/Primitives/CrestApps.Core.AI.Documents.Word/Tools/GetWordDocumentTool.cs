using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Lists the conversation's Word documents, or returns one document's content — every block with the id tools
/// name it by — and its metadata.
/// </summary>
internal sealed class GetWordDocumentTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.GetWordDocument;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "list": { "type": "boolean", "description": "List every working and uploaded Word document instead of reading one." },
            "from": { "type": "integer", "description": "First block to return, from 1. Use it to page through a long document." },
            "limit": { "type": "integer", "description": "Most blocks to return. Default 200." },
            "text_length": { "type": "integer", "description": "Most characters of each block's text. Default 200." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetWordDocumentTool"/> class.
    /// </summary>
    public GetWordDocumentTool()
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
    public override string Description => "Lists the Word documents in this conversation (working documents and uploads), or returns one document's content: properties, sections and page setup, and every element — headings, paragraphs, list items, tables, pictures, charts — with the [id] that update_word_content, remove_word_content, move_word_content, format_word_content and the other editing tools take. Call it before editing an existing document, and again after big changes. Uploaded files are read directly; they are copied into the workspace only when edited.";

    /// <summary>
    /// Describes the documents or one document.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var state = await context.GetStateAsync(cancellationToken);

        if (arguments.GetBoolean("list") == true || (arguments.Document() is null && state.Documents.Count + context.WordUploads.Count() != 1 && state.Find(state.ActiveDocument) is null))
        {
            return DescribeAll(context, state);
        }

        var source = context.FindDocument(state, arguments.Document());

        using var package = await context.OpenAsync(source, cancellationToken);

        var blocks = WordBlockReader.Read(package);
        var sections = WordSections.All(package);
        var from = Math.Max(1, arguments.GetInt("from") ?? 1);
        var limit = Math.Clamp(arguments.GetInt("limit") ?? 200, 1, 2000);
        var textLength = Math.Clamp(arguments.GetInt("text_length") ?? 200, 20, 4000);
        var answer = new StringBuilder();

        answer.Append("Document: ").Append(source.Describe()).AppendLine(".");

        if (source.Working is { } working)
        {
            if (!string.IsNullOrEmpty(working.SourceFileName))
            {
                answer.Append("Copied from upload \"").Append(working.SourceFileName).AppendLine("\" (the upload is unchanged).");
            }

            if (working.History.Count > 0)
            {
                answer.Append("Recent changes: ").AppendJoin("; ", working.History.TakeLast(5)).AppendLine(".");
            }
        }

        var properties = WordProperties.Read(package.Document);

        if (properties.Count > 0)
        {
            answer.Append("Properties: ").AppendJoin(", ", properties.Select(pair => pair.Key + " = \"" + WordText.Clip(pair.Value, 80) + "\"")).AppendLine(".");
        }

        for (var index = 0; index < sections.Count; index++)
        {
            answer.Append("Section ").Append(index + 1).Append(": ").Append(WordDescriber.DescribeSection(sections[index])).AppendLine(".");
        }

        var words = blocks.Sum(block => block.Kind == WordBlockKind.Table ? WordText.CountWords(WordText.Of(block.Element)) : WordText.CountWords(block.Text));

        answer.Append(CultureInfo.InvariantCulture, $"Contents: {blocks.Count} elements, {blocks.Count(block => block.IsHeading)} headings, {blocks.Count(block => block.Kind == WordBlockKind.Table)} tables, {blocks.Sum(block => block.Drawings.Count)} drawings, about {words:N0} words.");
        answer.AppendLine();

        if (package.MainPart.WordprocessingCommentsPart?.Comments?.HasChildren == true)
        {
            answer.Append("It has comments; see manage_word_comments. ");
        }

        if (WordRevisions.Changes(package).Count is > 0 and var revisions)
        {
            answer.Append(CultureInfo.InvariantCulture, $"It has {revisions} tracked change(s); see manage_word_revisions.");
        }

        answer.AppendLine().AppendLine();

        var shown = blocks.Skip(from - 1).Take(limit).ToList();

        if (shown.Count == 0)
        {
            answer.AppendLine(blocks.Count == 0 ? "The document is empty." : $"There are only {blocks.Count} elements.");

            return answer.ToString();
        }

        answer.Append(CultureInfo.InvariantCulture, $"Elements {from}-{from + shown.Count - 1} of {blocks.Count} ([id] Kind: text):").AppendLine();

        foreach (var block in shown)
        {
            answer.AppendLine(WordDescriber.Line(block, textLength));
        }

        if (from + shown.Count - 1 < blocks.Count)
        {
            answer.Append(CultureInfo.InvariantCulture, $"More: call again with from = {from + shown.Count}.").AppendLine();
        }

        return answer.ToString();
    }

    private static string DescribeAll(WordToolContext context, WordWorkspaceState state)
    {
        var answer = new StringBuilder();

        if (state.Documents.Count == 0 && !context.WordUploads.Any())
        {
            return "There are no Word documents in this conversation. Start one with create_word_document.";
        }

        if (state.Documents.Count > 0)
        {
            answer.AppendLine("Working documents:");

            foreach (var document in state.Documents)
            {
                answer.Append("- \"").Append(document.Name).Append('"')
                    .Append(CultureInfo.InvariantCulture, $" version {document.Version}, {document.ByteLength:N0} bytes");

                if (!string.IsNullOrEmpty(document.SourceFileName))
                {
                    answer.Append(", copied from \"").Append(document.SourceFileName).Append('"');
                }

                if (string.Equals(document.Name, state.ActiveDocument, StringComparison.OrdinalIgnoreCase))
                {
                    answer.Append(" (active)");
                }

                answer.AppendLine();
            }
        }

        var uploads = context.WordUploads.ToList();

        if (uploads.Count > 0)
        {
            answer.AppendLine("Uploaded Word documents (never changed; edits save a working copy):");

            foreach (var upload in uploads)
            {
                answer.Append("- \"").Append(upload.FileName).Append('"').Append(CultureInfo.InvariantCulture, $" {upload.FileSize:N0} bytes").AppendLine();
            }
        }

        answer.Append("Pass 'document' to read one.");

        return answer.ToString();
    }
}
