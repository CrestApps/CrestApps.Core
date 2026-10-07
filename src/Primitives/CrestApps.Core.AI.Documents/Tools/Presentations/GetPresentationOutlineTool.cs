using System.Text;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Ingestion;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Lists the conversation's decks and each slide of one with its layout, title, elements and notes.
/// </summary>
internal sealed class GetPresentationOutlineTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.GetPresentationOutline;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetPresentationOutlineTool"/> class.
    /// </summary>
    public GetPresentationOutlineTool()
        : base(
            TheName,
            "Lists the presentations in this conversation and outlines one: every slide with its number, layout, title, elements (each with the #id other tools address it by, its kind, and a text excerpt, table, chart or picture summary) and speaker notes. Call this first to learn a deck, and again after big changes. Also lists the uploaded pictures and templates you can use.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slides": { "type": ["integer", "string", "array"], "description": "Optional slides to outline, such as 3, [2, 5] or \"4-8\". Omit for every slide." },
                "include_notes": { "type": "boolean", "description": "Include speaker notes. Defaults to true." }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Gets a value indicating whether the tool needs a deck.
    /// </summary>
    protected override bool RequiresDeck => false;

    /// <summary>
    /// Outlines the deck.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();

        if (call.Session.Workspace.State.Decks.Count == 0)
        {
            builder.AppendLine("There is no presentation in this conversation yet. Start one with create_presentation.");
        }
        else
        {
            var deck = call.Deck();
            var model = await call.ReadAsync(deck, cancellationToken);
            var slides = call.Slides(model, false);

            builder.AppendLine(PresentationDescriber.Outline(deck, model, call.Session.Workspace, call.Arguments.Bool("include_notes") != false, slides));
        }

        var pictures = call.Session.Documents.Where(document => MediaTypeHelper.IsVisionImageExtension(Path.GetExtension(document.FileName))).ToList();
        var templates = call.Session.Documents.Where(document => call.Session.Options.IsPresentationFile(document.FileName)).ToList();

        if (pictures.Count > 0)
        {
            builder.AppendLine();
            builder.Append("Uploaded pictures you can place (image_document_id): ").AppendJoin(", ", pictures.Select(document => $"\"{document.FileName}\" ({document.ItemId})")).AppendLine(".");
        }

        if (templates.Count > 0)
        {
            builder.AppendLine();
            builder.Append("Decks and templates you can build on (template_document_id): ").AppendJoin(", ", templates.Select(document => $"\"{document.FileName}\" ({document.ItemId})")).AppendLine(".");
        }

        return builder.ToString().TrimEnd();
    }
}
