using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Extracts a document's content as Markdown or plain text, its tables, its links or its pictures, optionally as a
/// download.
/// </summary>
internal sealed class ExtractWordContentTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.ExtractWordContent;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "what": { "type": "string", "enum": ["markdown", "text", "tables", "links", "images"], "description": "markdown (default): the whole document with headings, lists and tables; text: plain text; tables: every table as Markdown, or as CSV when saved; links: every hyperlink; images: every picture and chart with its alt text and size." },
            "ids": { "type": "array", "items": { "type": "string" }, "description": "Only these elements (a table id for one table)." },
            "section": { "type": "integer", "description": "Only this section." },
            "save_as_file": { "type": "boolean", "description": "Also save the result as a download (.md, .txt or .csv)." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractWordContentTool"/> class.
    /// </summary>
    public ExtractWordContentTool()
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
    public override string Description => "Extracts the content of a Word document (or chosen elements or a section) for reading, summarizing, answering questions or reuse: 'markdown' (headings, lists, tables, links), plain 'text', its 'tables', its 'links', or its 'images' with alt text. 'save_as_file' also gives a download, such as the document as Markdown or its tables as CSV.";

    /// <summary>
    /// Extracts the content.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var what = (arguments.GetString("what") ?? "markdown").Trim().ToLowerInvariant();
        var source = await context.FindDocumentAsync(arguments.Document(), cancellationToken);

        using var package = await context.OpenAsync(source, cancellationToken);

        var styles = new WordStyleIndex(package.MainPart);
        var blocks = Select(package, styles, arguments);
        var save = arguments.GetBoolean("save_as_file") == true;
        var (content, extension) = what switch
        {
            "text" => (string.Join("\n\n", blocks.Select(block => block.Kind == WordBlockKind.Table ? TableText(block, "\t") : block.Text).Where(text => !string.IsNullOrWhiteSpace(text))), ".txt"),
            "tables" => Tables(blocks, save ? "csv" : "markdown"),
            "links" => (Links(package, blocks), ".txt"),
            "images" => (Images(blocks), ".txt"),
            "markdown" => (Markdown(package, blocks), ".md"),
            _ => throw new WordToolException("'what' must be markdown, text, tables, links or images."),
        };

        if (string.IsNullOrWhiteSpace(content))
        {
            return $"{source.Describe()} has no {what} in the selected content.";
        }

        var answer = new StringBuilder();

        if (save)
        {
            var fileName = Path.GetFileNameWithoutExtension(source.Name) + (what == "tables" ? "-tables" : string.Empty) + extension;

            // Markdown is served as a plain download so a browser never renders it as a page.
            var contentType = extension switch
            {
                ".csv" => "text/csv",
                ".txt" => "text/plain",
                _ => "application/octet-stream",
            };
            var marker = await context.ExportAsync(fileName, Encoding.UTF8.GetBytes(content), contentType, cancellationToken);

            answer.Append("Saved \"").Append(fileName).Append("\". WRITE THIS MARKER IN YOUR ANSWER EXACTLY AS SHOWN, ON A LINE OF ITS OWN: ").AppendLine(marker).AppendLine();
        }

        answer.Append(what).Append(" of ").Append(source.Describe()).AppendLine(":");
        answer.Append(content);

        return answer.ToString();
    }

    private static List<WordBlock> Select(WordPackage package, WordStyleIndex styles, WordToolArguments arguments)
    {
        var ids = arguments.GetIds("ids", "id");

        if (ids.Count > 0)
        {
            return [.. ids.Select(id => WordBlockReader.ReadBlock(WordBlockLocator.Require(package, id), styles))];
        }

        var blocks = WordBlockReader.Read(package);

        return arguments.GetInt("section") is { } section ? [.. blocks.Where(block => block.Section == section)] : blocks;
    }

    private static string Markdown(WordPackage package, List<WordBlock> blocks)
    {
        var answer = new StringBuilder();
        var inList = false;

        foreach (var block in blocks)
        {
            var paragraph = block.Element as Paragraph;
            var isListItem = block.Kind is WordBlockKind.BulletItem or WordBlockKind.NumberedItem;

            // List items sit on consecutive lines; a blank line ends the list.
            if (inList && !isListItem)
            {
                answer.AppendLine();
            }

            inList = isListItem;
            var text = paragraph is null ? block.Text : WordTextEditor.ToMarkdown(paragraph, package.MainPart);

            switch (block.Kind)
            {
                case WordBlockKind.Title:
                    answer.Append("# ").AppendLine(text);

                    break;

                case WordBlockKind.Heading:
                    answer.Append('#', Math.Clamp(block.Level + 1, 2, 6)).Append(' ').AppendLine(text);

                    break;

                case WordBlockKind.Subtitle:
                    answer.Append('_').Append(text).AppendLine("_");

                    break;

                case WordBlockKind.BulletItem:
                    answer.Append(' ', block.Level * 2).Append("- ").AppendLine(text);

                    continue;

                case WordBlockKind.NumberedItem:
                    answer.Append(' ', block.Level * 3).Append("1. ").AppendLine(text);

                    continue;

                case WordBlockKind.Quote:
                    answer.Append("> ").AppendLine(text);

                    break;

                case WordBlockKind.Code:
                    answer.AppendLine("```").AppendLine(block.Text).AppendLine("```");

                    break;

                case WordBlockKind.Table:
                    answer.Append(TableMarkdown(block));

                    break;

                case WordBlockKind.Image or WordBlockKind.Chart or WordBlockKind.Shape or WordBlockKind.SmartArt:
                    var drawing = block.Drawings.FirstOrDefault();

                    answer.Append('[').Append(block.Kind.ToString().ToLowerInvariant()).Append(": ").Append(drawing?.AltText ?? drawing?.Title ?? drawing?.Name ?? "no description").AppendLine("]");

                    break;

                case WordBlockKind.TableOfContents or WordBlockKind.Index or WordBlockKind.PageBreak or WordBlockKind.Empty:
                    continue;

                case WordBlockKind.Caption:
                    answer.Append('_').Append(text).AppendLine("_");

                    break;

                default:
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    answer.AppendLine(text);

                    break;
            }

            answer.AppendLine();
        }

        return answer.ToString().Trim();
    }

    private static (string Content, string Extension) Tables(List<WordBlock> blocks, string format)
    {
        var tables = blocks.Where(block => block.Kind == WordBlockKind.Table).ToList();
        var answer = new StringBuilder();

        for (var index = 0; index < tables.Count; index++)
        {
            if (format == "csv")
            {
                if (index > 0)
                {
                    answer.AppendLine();
                }

                answer.Append(TableText(tables[index], ",", csv: true));
            }
            else
            {
                answer.Append(CultureInfo.InvariantCulture, $"Table {index + 1} [{tables[index].Id}], {tables[index].Rows} × {tables[index].Columns}:").AppendLine();
                answer.Append(TableMarkdown(tables[index])).AppendLine();
            }
        }

        return (answer.ToString().Trim(), format == "csv" ? ".csv" : ".md");
    }

    private static string TableMarkdown(WordBlock block)
    {
        var rows = Rows(block);
        var answer = new StringBuilder();

        for (var index = 0; index < rows.Count; index++)
        {
            answer.Append("| ").AppendJoin(" | ", rows[index].Select(cell => cell.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" "))).AppendLine(" |");

            if (index == 0)
            {
                answer.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", rows[0].Count))).AppendLine();
            }
        }

        return answer.ToString();
    }

    private static string TableText(WordBlock block, string separator, bool csv = false)
    {
        var answer = new StringBuilder();

        foreach (var row in Rows(block))
        {
            answer.AppendJoin(separator, row.Select(cell => csv ? Csv(cell) : cell.ReplaceLineEndings(" "))).AppendLine();
        }

        return answer.ToString().TrimEnd();
    }

    private static List<List<string>> Rows(WordBlock block)
    {
        return [.. block.Element.Elements<TableRow>().Select(row => row.Elements<TableCell>().Select(WordText.OfCell).ToList())];
    }

    private static string Csv(string value)
    {
        // A cell starting with a formula character is quoted with a leading apostrophe so a spreadsheet does not
        // run it.
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' ? "'" + value : value;

        return safe.IndexOfAny([',', '"', '\n', '\r']) >= 0 || safe != value ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : safe;
    }

    private static string Links(WordPackage package, List<WordBlock> blocks)
    {
        var relationships = package.MainPart.HyperlinkRelationships.ToDictionary(item => item.Id, item => item.Uri.OriginalString, StringComparer.Ordinal);
        var answer = new StringBuilder();

        foreach (var block in blocks)
        {
            foreach (var link in block.Element.Descendants<Hyperlink>())
            {
                var target = link.Id?.Value is { } id && relationships.TryGetValue(id, out var uri) ? uri : link.Anchor?.Value is { } anchor ? "#" + anchor : "(no target)";

                answer.Append("- \"").Append(WordText.Of(link)).Append("\" → ").Append(target).Append(" in [").Append(block.Id).AppendLine("]");
            }
        }

        return answer.ToString().TrimEnd();
    }

    private static string Images(List<WordBlock> blocks)
    {
        var answer = new StringBuilder();

        foreach (var block in blocks)
        {
            foreach (var drawing in block.Drawings.Concat(block.Kind == WordBlockKind.Table ? block.Element.Descendants<Paragraph>().SelectMany(WordDrawingReader.ReadAll) : []))
            {
                answer.Append(CultureInfo.InvariantCulture, $"- {drawing.Kind} \"{drawing.Name}\" {drawing.Width:0}×{drawing.Height:0}pt{(drawing.IsFloating ? ", floating" : string.Empty)} in [{block.Id}]: ")
                    .AppendLine(string.IsNullOrWhiteSpace(drawing.AltText) ? "no alt text" : "alt text \"" + drawing.AltText + "\"");
            }
        }

        return answer.ToString().TrimEnd();
    }
}
