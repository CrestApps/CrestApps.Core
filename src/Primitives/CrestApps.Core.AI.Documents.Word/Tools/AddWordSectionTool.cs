using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Structure;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Starts a new section with its own page layout, headers, footers and numbering.
/// </summary>
internal sealed class AddWordSectionTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.AddWordSection;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "after": { "type": "string", "description": "The new section starts after this element. Defaults to the end of the document." },
            "before": { "type": "string", "description": "The new section starts with this element." },
            "start": { "type": "string", "enum": ["next_page", "continuous", "even_page", "odd_page"], "description": "How the new section starts. Default next_page." },
            {{WordToolSchemas.PageSetup}},
            "unlink_headers": { "type": "boolean", "description": "Give the new section no headers or footers of its own until add_word_header_footer sets them, instead of continuing the previous section's." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddWordSectionTool"/> class.
    /// </summary>
    public AddWordSectionTool()
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
    public override string Description => "Starts a new section of a Word document at an element, with its own page layout ('page_setup': size, landscape orientation, margins, columns) and its own headers, footers and page numbering. Use it for a landscape page in a portrait document, a two-column part, or an appendix numbered differently. The content before the break keeps its layout.";

    /// <summary>
    /// Adds the section.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var (description, document) = await context.EditAsync(arguments.Document(), "Added a section", edit =>
        {
            var (section, number) = WordSectionBreaks.Insert(edit.Package, arguments.GetString("after"), arguments.GetString("before"), arguments.GetString("start"));

            if (arguments.TryGetObject("page_setup", out var setup))
            {
                WordSetupReader.ApplyPageSetup(section, setup);
            }

            if (arguments.GetBoolean("unlink_headers") == true)
            {
                // A section without references of its own would continue the previous one's, so it gets empty ones.
                foreach (var reference in section.Elements<HeaderReference>().ToList())
                {
                    reference.Remove();
                }

                foreach (var reference in section.Elements<FooterReference>().ToList())
                {
                    reference.Remove();
                }

                Formatting.WordHeadersFooters.AddEmpty(edit.Package, section);
            }

            return Task.FromResult($"Section {number} starts there: {WordDescriber.DescribeSection(section)}.");
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {description} preview_word shows the result.";
    }
}
