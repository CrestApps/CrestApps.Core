using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Adds content blocks — headings, paragraphs, lists, tables, pictures, charts, quotes, code and page breaks —
/// at a position in a document.
/// </summary>
internal sealed class AddWordContentTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.AddWordContent;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            {{WordToolSchemas.Blocks}},
            {{WordToolSchemas.Position}},
            {{WordToolSchemas.SaveAs}}
          },
          "required": ["content"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddWordContentTool"/> class.
    /// </summary>
    public AddWordContentTool()
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
    public override string Description => "Adds content to a Word document: headings, paragraphs (inline Markdown), Markdown, bulleted and numbered lists (nested), quotes, code, tables (with column formats, or filled from uploaded tabular data with source.sql), images, charts, captions, page breaks and rules. Add every block you have in ONE call, in order. Place it with 'after' or 'before' an element id, or at the 'start' or 'end' (default). Editing an uploaded file saves a working copy; the upload is never changed. Returns the new elements' ids.";

    /// <summary>
    /// Adds the content.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetElement("content", out var content))
        {
            throw new WordToolException("Pass 'content': an array of blocks such as [{ \"type\": \"heading\", \"text\": \"Overview\", \"level\": 1 }, { \"type\": \"paragraph\", \"text\": \"…\" }].");
        }

        if (arguments.TryGetObject("content", out var parsed))
        {
            content = parsed;
        }

        var after = arguments.GetString("after");
        var before = arguments.GetString("before");
        var at = arguments.GetString("at");

        var (result, document) = await context.EditAsync(
            arguments.Document(),
            "Added content",
            async edit =>
            {
                var anchor = after is not null ? WordBlockLocator.Require(edit.Package, after) : before is not null ? WordBlockLocator.Require(edit.Package, before) : null;
                var builder = new WordContentBuilder(edit, context, anchor);
                var elements = await builder.BuildAsync(content, cancellationToken);

                if (elements.Count == 0)
                {
                    throw new WordToolException("The content had no blocks to add.");
                }

                WordBlockLocator.Insert(edit.Package, elements, after, before, at);

                if (after is not null)
                {
                    ContinueList(edit.Package, anchor, elements);
                }

                if (builder.HasTableOfContents)
                {
                    // A table of contents lists the headings around it, so it is filled in once they are placed.
                    WordDocumentRefresher.Refresh(edit.Package, context.Services);
                }
                else if (elements.Any(element => WordBlockReader.HasField(element, "SEQ")))
                {
                    WordCaptions.Renumber(edit.Package);
                }

                var styles = new WordStyleIndex(edit.Package.MainPart);
                var lines = elements
                    .Select(element => WordBlockReader.ReadBlock(element, styles))
                    .Where(block => block is not null)
                    .Select(block => WordDescriber.Line(block, 80))
                    .ToList();

                return (lines, builder.Warnings);
            },
            arguments.SaveAs(),
            cancellationToken);

        var answer = new StringBuilder();

        answer.Append("Added ").Append(result.lines.Count).Append(" element(s) to \"").Append(document.Name).Append("\" (version ").Append(document.Version).AppendLine("):");

        foreach (var line in result.lines.Take(200))
        {
            answer.AppendLine(line);
        }

        foreach (var warning in result.Warnings)
        {
            answer.Append("Warning: ").AppendLine(warning);
        }

        return answer.ToString().TrimEnd();
    }

    // A list added right after an item of a list of the same kind — a fourth risk after the third — continues that
    // list, so a numbered list keeps counting instead of starting again at 1.
    private static void ContinueList(WordPackage package, DocumentFormat.OpenXml.OpenXmlElement anchor, List<DocumentFormat.OpenXml.OpenXmlElement> elements)
    {
        if (NumberingOf(anchor) is not { } previous ||
            elements.FirstOrDefault() is not DocumentFormat.OpenXml.Wordprocessing.Paragraph first ||
            NumberingOf(first) is not { } added ||
            added.Level != previous.Level ||
            IsBullet(package, previous.Id, previous.Level) != IsBullet(package, added.Id, added.Level))
        {
            return;
        }

        foreach (var paragraph in elements.OfType<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            if (paragraph.ParagraphProperties?.NumberingProperties?.NumberingId is not { } numbering)
            {
                break;
            }

            if (numbering.Val?.Value == added.Id)
            {
                numbering.Val = previous.Id;
            }
        }
    }

    private static (int Id, int Level)? NumberingOf(DocumentFormat.OpenXml.OpenXmlElement element)
    {
        var properties = (element as DocumentFormat.OpenXml.Wordprocessing.Paragraph)?.ParagraphProperties?.NumberingProperties;

        return properties?.NumberingId?.Val?.Value is { } id && id > 0
            ? (id, properties.NumberingLevelReference?.Val?.Value ?? 0)
            : null;
    }

    private static bool IsBullet(WordPackage package, int numberId, int level)
    {
        var numbering = package.MainPart.NumberingDefinitionsPart?.Numbering;
        var abstractId = numbering?.Elements<DocumentFormat.OpenXml.Wordprocessing.NumberingInstance>()
            .FirstOrDefault(instance => instance.NumberID?.Value == numberId)?.AbstractNumId?.Val?.Value;
        var definition = numbering?.Elements<DocumentFormat.OpenXml.Wordprocessing.AbstractNum>()
            .FirstOrDefault(item => item.AbstractNumberId?.Value == abstractId);
        var format = definition?.Elements<DocumentFormat.OpenXml.Wordprocessing.Level>()
            .FirstOrDefault(item => item.LevelIndex?.Value == level)?.NumberingFormat?.Val;

        return format is not null && format.Value == DocumentFormat.OpenXml.Wordprocessing.NumberFormatValues.Bullet;
    }
}
