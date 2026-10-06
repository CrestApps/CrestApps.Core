using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Structure;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Inserts a page break, a column break or a section break.
/// </summary>
internal sealed class AddWordPageBreakTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.AddWordPageBreak;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "type": { "type": "string", "enum": ["page", "column", "section"], "description": "page (default), column (in a multi-column section) or section (a new section on a new page; use add_word_section to give it its own layout)." },
            {{WordToolSchemas.Position}},
            {{WordToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddWordPageBreakTool"/> class.
    /// </summary>
    public AddWordPageBreakTool()
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
    public override string Description => "Inserts a page break, a column break or a section break into a Word document 'after' or 'before' an element, or at the 'start' or 'end'. To make a heading always start a new page, prefer format_word_content with paragraph_format.page_break_before.";

    /// <summary>
    /// Inserts the break.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var type = (arguments.GetString("type") ?? "page").Trim().ToLowerInvariant();

        var (description, document) = await context.EditAsync(arguments.Document(), "Added a " + type + " break", edit =>
        {
            if (type == "section")
            {
                var after = arguments.GetString("after");
                var before = arguments.GetString("before");

                if (after is null && before is null && string.Equals(arguments.GetString("at"), "start", StringComparison.OrdinalIgnoreCase))
                {
                    throw new WordToolException("A section break cannot go at the very start of the document.");
                }

                var (_, number) = WordSectionBreaks.Insert(edit.Package, after, before, "next_page");

                return Task.FromResult($"a section break; section {number} starts on a new page");
            }

            var breakElement = new Paragraph(new Run(new Break { Type = type == "column" ? BreakValues.Column : BreakValues.Page }));

            WordBlockLocator.Insert(edit.Package, [breakElement], arguments.GetString("after"), arguments.GetString("before"), arguments.GetString("at"));

            return Task.FromResult($"a {type} break [{WordParagraphIds.Of(breakElement)}]");
        }, arguments.SaveAs(), cancellationToken);

        return $"Inserted {description} in \"{document.Name}\" (version {document.Version}).";
    }
}
