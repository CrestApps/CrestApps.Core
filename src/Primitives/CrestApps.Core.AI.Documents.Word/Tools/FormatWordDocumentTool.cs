using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Changes a document's whole look: its theme, typefaces, colors, sizes and spacing, through its styles.
/// </summary>
internal sealed class FormatWordDocumentTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.FormatWordDocument;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            {{WordToolSchemas.Theme}},
            "justify_body": { "type": "boolean", "description": "Justify body text (true) or align it left (false)." },
            {{WordToolSchemas.Properties}},
            {{WordToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="FormatWordDocumentTool"/> class.
    /// </summary>
    public FormatWordDocumentTool()
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
    public override string Description => "Changes the whole look of a Word document through its styles: a 'theme' preset and/or fonts, sizes, text, heading, accent and link colors, line and paragraph spacing, table colors — every heading, paragraph, list, quote, caption and data table that uses the styles follows. Also justifies body text and sets document properties. Page size, margins and columns are set_word_page_layout. An uploaded document keeps its other styles.";

    /// <summary>
    /// Restyles the document.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var (summary, document) = await context.EditAsync(arguments.Document(), "Changed the document's look", edit =>
        {
            var changes = new List<string>();
            var hasTheme = arguments.TryGetObject("theme", out var theme);
            var justify = arguments.GetBoolean("justify_body");

            if (hasTheme)
            {
                var design = WordSetupReader.ReadTheme(theme, edit.Design);

                WordStyleSheet.Apply(edit.Package.MainPart, design);
                edit.Design = design;
                changes.Add($"theme {design.Preset}: {design.BodyFont} {design.BodySize:0.#}pt body in #{design.TextColor}, {design.HeadingFont} headings in #{design.HeadingColor}, accent #{design.AccentColor}, line spacing {design.LineSpacing:0.##}");
            }

            if (justify is not null)
            {
                var normal = WordStyleSheet.GetOrCreateStyles(edit.Package.MainPart).Elements<Style>().First(style => string.Equals(style.StyleId?.Value, WordStyleSheet.Ensure(edit.Package.MainPart, WordStyleSheet.Normal, edit.Design), StringComparison.Ordinal));
                var paragraphProperties = normal.StyleParagraphProperties ??= new StyleParagraphProperties();

                paragraphProperties.Justification = justify.Value ? new Justification { Val = JustificationValues.Both } : null;
                changes.Add(justify.Value ? "body text justified" : "body text aligned left");
            }

            if (arguments.TryGetObject("properties", out var properties))
            {
                var set = WordProperties.Apply(edit.Package.Document, properties, edit.Now);

                if (set.Count > 0)
                {
                    changes.Add("properties " + string.Join(", ", set));
                }
            }

            if (changes.Count == 0)
            {
                throw new WordToolException("Say what to change: 'theme', 'justify_body' or 'properties'.");
            }

            return Task.FromResult(string.Join("; ", changes));
        }, arguments.SaveAs(), cancellationToken);

        return $"Restyled \"{document.Name}\" (version {document.Version}): {summary}. preview_word shows the new look.";
    }
}
