using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Changes existing elements in place: their text, a phrase within them, their style, heading level or
/// alignment, or the whole element — without rebuilding the document.
/// </summary>
internal sealed class UpdateWordContentTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.UpdateWordContent;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "id": { "type": "string", "description": "The element to change." },
            "ids": { "type": "array", "items": { "type": "string" }, "description": "Several elements to change the same way." },
            "text": { "type": "string", "description": "New text for the whole element, with inline Markdown. The element keeps its style." },
            "find": { "type": "string", "description": "Text to find inside the elements, in the section of the heading 'under' names, or in the whole document with 'everywhere'." },
            "under": { "type": "string", "description": "A heading id: find and replace only in that heading's section, up to the next heading of the same or a higher level — 'in the Executive Summary'." },
            "everywhere": { "type": "boolean", "description": "Find and replace in the whole document, the title and every section included." },
            "replace": { "type": "string", "description": "What 'find' becomes; an empty string deletes it." },
            "match_case": { "type": "boolean" },
            "whole_word": { "type": "boolean" },
            "count": { "type": "integer", "description": "Most occurrences to replace. Default all." },
            "block": {{WordToolSchemas.Block}},
            "style": { "type": "string", "description": "Paragraph style name, such as 'Heading 2', 'Quote' or 'Normal'." },
            "level": { "type": "integer", "description": "Make the paragraph a heading of this level (1-6)." },
            "alignment": { "type": "string", "enum": ["left", "center", "right", "justify"] },
            {{WordToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateWordContentTool"/> class.
    /// </summary>
    public UpdateWordContentTool()
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
    public override string Description => "Changes existing elements of a Word document in place, by id: replace an element's whole 'text' (inline Markdown, style kept), 'find' and 'replace' a phrase (across formatting, in the elements or the whole document), replace the element with a new 'block', or change its 'style', heading 'level' or 'alignment'. When tracked changes are on, text edits are recorded as revisions. Use it for every follow-up edit instead of rebuilding the document; tables are changed with update_word_table.";

    /// <summary>
    /// Updates the elements.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var ids = arguments.GetIds("ids", "id");
        var text = arguments.GetRawString("text");
        var find = arguments.GetRawString("find");
        var hasBlock = arguments.TryGetObject("block", out var block);
        var style = arguments.GetString("style");
        var level = arguments.GetInt("level");
        var alignment = arguments.GetString("alignment");

        if (text is null && find is null && !hasBlock && style is null && level is null && alignment is null)
        {
            throw new WordToolException("Say what to change: 'text', 'find' and 'replace', 'block', 'style', 'level' or 'alignment'.");
        }

        var under = arguments.GetString("under");

        if (ids.Count == 0 && find is null)
        {
            throw new WordToolException("Pass 'id' (or 'ids') of the element to change. Only 'find' and 'replace' work on a section or the whole document.");
        }

        // A phrase replaced with no scope would change the title and every section, which a request about one part
        // of the document rarely means, so the whole document has to be asked for.
        if (find is not null && ids.Count == 0 && under is null && arguments.GetBoolean("everywhere") != true)
        {
            throw new WordToolException("Say where to replace: 'ids' of the elements, 'under' with a heading id for that heading's section, or 'everywhere': true for the whole document, title included.");
        }

        var (lines, document) = await context.EditAsync(arguments.Document(), "Updated content", async edit =>
        {
            var revisions = WordRevisions.IsTracking(edit.Package) ? WordRevisions.For(edit.Package, edit.Author, edit.Now) : null;
            var report = new List<string>();

            if (find is not null && ids.Count == 0)
            {
                List<DocumentFormat.OpenXml.OpenXmlElement> scopes = under is null ? [edit.Package.Body] : SectionOf(edit.Package, under);
                var remaining = arguments.GetInt("count") ?? int.MaxValue;
                var replaced = 0;

                foreach (var scope in scopes)
                {
                    var count = WordTextEditor.Replace(scope, find, arguments.GetRawString("replace") ?? string.Empty, arguments.GetBoolean("match_case") == true, arguments.GetBoolean("whole_word") == true, revisions, remaining - replaced);

                    replaced += count;

                    if (replaced >= remaining)
                    {
                        break;
                    }
                }

                if (replaced == 0)
                {
                    throw new WordToolException(under is null ? $"\"{find}\" was not found in the document." : $"\"{find}\" was not found under [{under}].");
                }

                report.Add($"Replaced {replaced} occurrence(s) of \"{find}\".");

                return report;
            }

            foreach (var id in ids)
            {
                var element = WordBlockLocator.Require(edit.Package, id);

                if (hasBlock)
                {
                    var built = await new WordContentBuilder(edit, context, element).BuildBlockAsync(block, cancellationToken);
                    var container = element.Parent;

                    if (container is SdtContentBlock && built.All(item => item is Paragraph or Table))
                    {
                        // Inside a content control, the new content takes the old element's place in the control.
                        foreach (var item in built)
                        {
                            edit.Package.Ids.Assign(item);
                            element.InsertBeforeSelf(item);
                        }
                    }
                    else
                    {
                        WordBlockLocator.Insert(edit.Package, built, after: null, before: id, at: null);
                    }

                    // A paragraph that ends a section hands the break to the last new paragraph, or to an empty one
                    // after a new table, so the section keeps its layout.
                    if (element is Paragraph replaced)
                    {
                        WordSections.KeepSectionBreak(edit.Package, replaced);
                    }

                    element.Remove();
                    WordBlockLocator.RepairContainer(edit.Package, container);
                    report.Add($"[{id}] replaced by {built.Count} new element(s): " + string.Join(", ", built.Select(item => "[" + WordParagraphIds.Of(item) + "]")));

                    continue;
                }

                if (find is not null)
                {
                    var replaced = WordTextEditor.Replace(element, find, arguments.GetRawString("replace") ?? string.Empty, arguments.GetBoolean("match_case") == true, arguments.GetBoolean("whole_word") == true, revisions, arguments.GetInt("count") ?? int.MaxValue);

                    report.Add($"[{id}] {replaced} occurrence(s) of \"{find}\" replaced.");
                }

                if (element is not Paragraph paragraph)
                {
                    if (text is not null || style is not null || level is not null || alignment is not null)
                    {
                        throw new WordToolException($"[{id}] is a {(element is Table ? "table — change it with update_word_table or format_word_content" : "container — change the paragraphs inside it")}.");
                    }

                    continue;
                }

                if (text is not null && WordTextEditor.ReplaceParagraph(paragraph, text, edit.Package.MainPart, revisions) is { } dropped)
                {
                    report.Add($"[{id}] {dropped} Use 'find' and 'replace' to change only part of the text.");
                }

                var properties = paragraph.ParagraphProperties ??= new ParagraphProperties();

                if (level is not null)
                {
                    properties.ParagraphStyleId = new ParagraphStyleId { Val = WordStyleSheet.Ensure(edit.Package.MainPart, WordStyleSheet.Heading(Math.Clamp(level.Value, 1, 6)), edit.Design) };
                }
                else if (style is not null)
                {
                    properties.ParagraphStyleId = new ParagraphStyleId { Val = ResolveStyle(edit, style) };
                }

                if (WordTableWriter.ReadAlignment(alignment) is { } justification)
                {
                    properties.Justification = new Justification { Val = justification };
                }

                if (!properties.HasChildren)
                {
                    properties.Remove();
                }

                var styles = new WordStyleIndex(edit.Package.MainPart);

                report.Add(WordDescriber.Line(WordBlockReader.ReadParagraph(paragraph, styles), 100));
            }

            return report;
        }, arguments.SaveAs(), cancellationToken);

        var answer = new StringBuilder();

        answer.Append("Updated \"").Append(document.Name).Append("\" (version ").Append(document.Version).AppendLine("):");

        foreach (var line in lines)
        {
            answer.AppendLine(line);
        }

        return answer.ToString().TrimEnd();
    }

    private static string ResolveStyle(WordEditContext edit, string style)
    {
        var found = WordStyleSheet.Find(edit.Package.MainPart, style, StyleValues.Paragraph);

        if (found is not null)
        {
            return found;
        }

        var standard = style.Replace(" ", string.Empty, StringComparison.Ordinal);

        if (standard.Equals("Normal", StringComparison.OrdinalIgnoreCase))
        {
            return WordStyleSheet.Normal;
        }

        if (standard.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) || standard is "Quote" or "Caption" or "Title" or "Subtitle" or "NoSpacing" or "CodeBlock" or "ListParagraph")
        {
            return WordStyleSheet.Ensure(edit.Package.MainPart, char.ToUpperInvariant(standard[0]) + standard[1..], edit.Design);
        }

        throw new WordToolException($"The document has no paragraph style \"{style}\". manage_word_styles lists and creates styles.");
    }

    // The elements of a heading's section: everything after it up to the next heading of its level or higher.
    private static List<DocumentFormat.OpenXml.OpenXmlElement> SectionOf(WordPackage package, string headingId)
    {
        var blocks = WordBlockReader.Read(package);
        var index = blocks.FindIndex(block => string.Equals(block.Id, headingId, StringComparison.OrdinalIgnoreCase));

        if (index < 0 || blocks[index].Kind != WordBlockKind.Heading)
        {
            throw new WordToolException($"[{headingId}] is not a heading; 'under' takes the id of the heading whose section to change.");
        }

        var level = blocks[index].Level;

        return [.. blocks
            .Skip(index + 1)
            .TakeWhile(block => block.Kind != WordBlockKind.Heading || block.Level > level)
            .Select(block => block.Element)];
    }
}
