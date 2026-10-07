using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Adds a numbered caption to a table, a picture, a chart or any other element.
/// </summary>
internal sealed class AddWordCaptionTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.AddWordCaption;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "target": { "type": "string", "description": "The id of the table, picture, chart or paragraph the caption belongs to." },
            "label": { "type": "string", "description": "Figure, Table, Equation, or another label. Defaults to Table for a table and Figure otherwise." },
            "text": { "type": "string", "description": "The caption text after the number, such as 'Revenue by region'." },
            "position": { "type": "string", "enum": ["above", "below"], "description": "Defaults to above for tables and below for everything else." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": ["target"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddWordCaptionTool"/> class.
    /// </summary>
    public AddWordCaptionTool()
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
    public override string Description => "Adds a numbered caption ('Table 2: Revenue by region', 'Figure 3: …') to a table, picture, chart or paragraph by its id. Captions are real sequence fields: they renumber themselves, a list of figures can collect them, and add_word_cross_reference can point at them.";

    /// <summary>
    /// Adds the caption.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var target = arguments.GetString("target") ?? throw new WordToolException("Pass 'target': the id of the element to caption.");

        var (line, document) = await context.EditAsync(arguments.Document(), "Added a caption", edit =>
        {
            var element = WordBlockLocator.Require(edit.Package, target);
            var block = WordSections.TopLevel(edit.Package.Body, element) ?? element;

            // An element has one caption: a second request changes its words, keeping its number and every
            // cross-reference to it.
            if (WordCaptions.FindCaptionOf(block) is { } existing)
            {
                WordCaptions.SetText(existing, arguments.GetString("text"), edit.Package.MainPart);
                WordCaptions.Renumber(edit.Package);

                return Task.FromResult("[" + WordParagraphIds.Of(existing) + "] already captioned it, so its words were changed: " +
                    WordDescriber.Line(WordBlockReader.ReadParagraph(existing, new WordStyleIndex(edit.Package.MainPart))) +
                    " Refer to a captioned element by this caption's id.");
            }

            var label = arguments.GetString("label") ?? (block is Table ? "Table" : "Figure");
            var above = string.Equals(arguments.GetString("position") ?? (block is Table ? "above" : "below"), "above", StringComparison.OrdinalIgnoreCase);
            var caption = new WordContentBuilder(edit, context, block).Caption(label, arguments.GetString("text"));

            if (above)
            {
                caption.ParagraphProperties.KeepNext = new KeepNext();
                WordBlockLocator.Insert(edit.Package, [caption], after: null, before: WordParagraphIds.Of(block), at: null);
            }
            else
            {
                if (block is Paragraph paragraph)
                {
                    (paragraph.ParagraphProperties ??= new ParagraphProperties()).KeepNext = new KeepNext();
                }

                WordBlockLocator.Insert(edit.Package, [caption], after: WordParagraphIds.Of(block), before: null, at: null);
            }

            WordCaptions.Renumber(edit.Package);

            return Task.FromResult(WordDescriber.Line(WordBlockReader.ReadParagraph(caption, new WordStyleIndex(edit.Package.MainPart))));
        }, arguments.SaveAs(), cancellationToken);

        return $"Added to \"{document.Name}\" (version {document.Version}): {line}";
    }
}
