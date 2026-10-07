using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Inserts a table of contents built from the document's headings, or refreshes the one it has.
/// </summary>
internal sealed class AddWordTocTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.AddWordToc;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "title": { "type": "string", "description": "Heading over the table. Default 'Contents'; an empty string for none." },
            "from_level": { "type": "integer", "description": "Highest heading level listed. Default 1." },
            "to_level": { "type": "integer", "description": "Lowest heading level listed. Default 3." },
            "page_numbers": { "type": "boolean", "description": "Show page numbers. Default true." },
            "page_break_after": { "type": "boolean", "description": "Start the content after the table on a new page. Default true." },
            "after": { "type": "string", "description": "Only when the user asked for the table somewhere else: the id of the element it follows. Left out, it goes after the title and any cover page." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddWordTocTool"/> class.
    /// </summary>
    public AddWordTocTool()
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
    public override string Description => "Inserts a real Word table of contents built from the document's headings, with hyperlinked entries and page numbers, after the document's title and any cover page. If the document already has one, it is refreshed from the current headings instead; a table of figures does not count as one. The table is also refreshed automatically, with its page numbers, on every preview and export.";

    /// <summary>
    /// Inserts or refreshes the table of contents.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var (summary, document) = await context.EditAsync(arguments.Document(), "Added a table of contents", edit =>
        {
            var package = edit.Package;

            // A table of figures or of tables is not a table of contents, so the document still needs one.
            var tables = WordFieldScanner.Scan(package.Body).Where(field => field.Type == "TOC" && !WordTableOfContents.IsTableOfFigures(field.Instruction)).ToList();
            var existing = tables.Count > 0;

            if (!existing)
            {
                var from = Math.Clamp(arguments.GetInt("from_level") ?? 1, 1, 9);
                var to = Math.Clamp(arguments.GetInt("to_level") ?? 3, from, 9);
                var title = arguments.GetRawString("title") ?? "Contents";
                var after = arguments.GetString("after") ?? LeadingTitle(package);
                var at = after is null ? "start" : null;

                var writer = edit.CreateWriter();
                var toc = WordTableOfContents.Create(package, writer, title, from, to, arguments.GetBoolean("page_numbers") ?? true);
                var elements = new List<OpenXmlElement> { toc };

                if (arguments.GetBoolean("page_break_after") ?? true)
                {
                    elements.Add(WordBlockWriter.PageBreak());
                }

                WordBlockLocator.Insert(package, elements, after, null, at);
            }

            WordDocumentRefresher.Refresh(package, context.Services);

            if (existing && !tables.Any(field => WordTableOfContents.IsHeadingTable(field.Instruction)))
            {
                return Task.FromResult("The document's table of contents is built from custom styles or TC entries rather than heading levels, so it was left as it is; the exported file asks Word to update it when it opens.");
            }

            // An entry links to its heading, shows its page, or both.
            var entries = WordFieldScanner.Scan(package.Body)
                .Where(field => field.Type == "TOC" && WordTableOfContents.IsHeadingTable(field.Instruction))
                .SelectMany(field => WordTableOfContents.ParagraphsOf(field) ?? [])
                .Count(paragraph => paragraph.Descendants<Hyperlink>().Any(link => link.Anchor?.Value is not null) || WordFieldScanner.Scan(paragraph).Any(field => field.Type == "PAGEREF"));
            var headings = WordBlockReader.Read(package).Count(block => block.Kind == WordBlockKind.Heading);
            var tocBlock = WordBlockReader.Read(package).FirstOrDefault(block => block.Kind == WordBlockKind.TableOfContents);
            var text = existing ? "Refreshed the existing table of contents" : "Inserted a table of contents";

            text += tocBlock is null ? string.Empty : $" [{tocBlock.Id}]";
            text += $" with {entries} entr{(entries == 1 ? "y" : "ies")}.";

            if (headings == 0)
            {
                text += " The document has no headings yet, so the table is empty; give headings real heading styles (update_word_content with 'level') and it fills in.";
            }

            return Task.FromResult(text);
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {summary} preview_word shows it.";
    }

    // The title block: a title, a subtitle, and the page break that ends a cover page.
    private static string LeadingTitle(WordPackage package)
    {
        string last = null;

        foreach (var block in WordBlockReader.Read(package))
        {
            if (block.Kind is WordBlockKind.Title or WordBlockKind.Subtitle || (last is not null && block.Kind == WordBlockKind.PageBreak))
            {
                last = block.Id;

                continue;
            }

            if (block.Kind != WordBlockKind.Empty || block.EndsSection)
            {
                break;
            }
        }

        return last;
    }
}
