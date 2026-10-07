using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Gives a deck the design of an uploaded template or another deck: its theme, masters and layouts.
/// </summary>
internal sealed class ApplyPresentationTemplateTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.ApplyPresentationTemplate;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplyPresentationTemplateTool"/> class.
    /// </summary>
    public ApplyPresentationTemplateTool()
        : base(
            TheName,
            "Redesigns the presentation with the look of an uploaded .pptx or .potx template, or of another presentation in the conversation: its colours, fonts, slide masters, layouts and backgrounds. Each slide moves to the template's matching layout and keeps its content. Use it for 'make this match our company template'.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "template": { "type": "string", "description": "The uploaded template's id or file name, or the name of another presentation." }
              },
              "required": ["template"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Applies the template.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var reference = call.Arguments.String("template", "template_document_id", "document_id", "from")
            ?? throw new PresentationArgumentException("Name the 'template': an uploaded .pptx or .potx, or another presentation.");

        byte[] package;
        var document = call.Session.FindDocument(reference);

        if (document is not null)
        {
            package = await call.Session.ImportAsync(document, cancellationToken)
                ?? throw new PresentationArgumentException($"\"{document.FileName}\" could not be read as a presentation or template.");
        }
        else
        {
            var other = call.Session.Workspace.FindDeck(reference);

            if (other is null || other.Id == deck.Id)
            {
                throw new PresentationArgumentException($"There is no uploaded template or other presentation \"{reference}\" in this conversation.");
            }

            package = await call.Session.Workspace.ReadAsync(other);
        }

        var result = await call.Session.ApplyAsync(deck, [new ApplyTemplateEdit { TemplatePackage = package }], $"applied the design of {reference}", cancellationToken);

        return Changed(deck, result);
    }
}
