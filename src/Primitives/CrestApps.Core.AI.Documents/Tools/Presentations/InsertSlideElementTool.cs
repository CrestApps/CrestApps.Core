using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Places elements on a slide: text boxes, bullet lists, shapes, lines and connectors, tables, charts,
/// pictures, icons, media links and groups.
/// </summary>
internal sealed class InsertSlideElementTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.InsertSlideElement;

    /// <summary>
    /// Initializes a new instance of the <see cref="InsertSlideElementTool"/> class.
    /// </summary>
    public InsertSlideElementTool()
        : base(
            TheName,
            "Places elements on a slide: text boxes, bullet lists, shapes (callouts, arrows, stars and 30 more), lines and connectors between elements, tables, charts, pictures (uploaded, or generated from image_prompt), icons, video or audio links, and groups. Place each with a named 'position' such as right_half or top_right, or with x/y/width/height. A table or chart can take its rows from the conversation's spreadsheet data with tabular_sql, and stay linked to it with link_data. Give pictures, charts and icons alt_text.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slide": { "type": "integer" },
                "elements": { "type": "array", "items": ELEMENT }
              },
              "required": ["slide", "elements"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Inserts the elements.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var slide = call.Slide(model);
        var items = call.Arguments.Array("elements", "element", "shapes");

        // A single element written at the top level, as models sometimes do.
        if (items.Count == 0 && call.Arguments.Node("type", "kind") is not null)
        {
            items.Add(call.Arguments.Root);
        }

        if (items.Count == 0)
        {
            throw new PresentationArgumentException("Give the elements to place in 'elements', such as [{\"type\": \"text\", \"text\": \"Hello\", \"position\": \"center\"}].");
        }

        var requests = items.Select(PresentationArguments.ReadElement).ToList();
        var notes = new List<string>();

        PresentationRequestBuilder.PrepareLinks(requests);
        await PresentationContentResolver.ResolveAsync(call.Session, requests, notes, cancellationToken);

        var edit = new InsertElementsEdit
        {
            Slide = slide,
            Elements = requests.Select(request => request.Spec).ToList(),
        };

        var result = await call.Session.ApplyAsync(deck, [edit], $"added {PresentationDescriber.Count(requests.Count, "element")} to slide {slide}", cancellationToken);

        if (PresentationRequestBuilder.RecordLinks(call.Services, deck, requests, result) > 0)
        {
            await call.Session.Workspace.SaveStateAsync();
            notes.Add("The data stays linked: refresh_slide_data updates it from its query.");
        }

        return Changed(deck, result, notes);
    }
}
