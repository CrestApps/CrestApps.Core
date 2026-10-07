using System.Text.Json.Nodes;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Aligns, distributes, sizes, layers and lays out elements.
/// </summary>
internal sealed class ArrangeSlideElementsTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.ArrangeSlideElements;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrangeSlideElementsTool"/> class.
    /// </summary>
    public ArrangeSlideElementsTool()
        : base(
            TheName,
            "Arranges elements on a slide: align them (left, center, right, top, middle, bottom) to each other or to the slide; distribute them evenly; make them the same width or height; bring forward or send back; snap to a grid; fit or center on the slide; or auto_layout them into a tidy grid with even spacing. Several actions can run in order with 'actions'.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slide": { "type": "integer" },
                "elements": { "type": "array", "items": { "type": ["integer", "string"] }, "description": "The elements' #ids or names. Omit to arrange every element that is not a title, footer or slide number." },
                "action": { "type": "string", "enum": ["align_left", "align_center", "align_right", "align_top", "align_middle", "align_bottom", "distribute_horizontally", "distribute_vertically", "match_width", "match_height", "match_size", "bring_forward", "send_backward", "bring_to_front", "send_to_back", "snap_to_grid", "auto_layout", "fit_to_slide", "center_on_slide"] },
                "relative_to": { "type": "string", "enum": ["selection", "slide", "content"], "description": "Align to the elements' own extent (default), the whole slide, or the content area." },
                "spacing": LENGTH,
                "columns": { "type": "integer", "description": "For auto_layout: the number of columns." },
                "actions": { "type": "array", "description": "Several actions in order, each with 'action' and optionally its own 'elements', 'relative_to', 'spacing' and 'columns'.", "items": { "type": "object" } }
              },
              "required": ["slide"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Arranges the elements.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var slide = call.Slide(model);
        var steps = call.Arguments.Array("actions", "steps").OfType<JsonObject>().ToList();

        if (steps.Count == 0)
        {
            steps.Add(call.Arguments.Root);
        }

        var shared = call.Arguments.Strings("elements", "element", "ids");
        var edits = new List<PresentationEdit>();

        foreach (var step in steps)
        {
            var action = PresentationArguments.ReadString(PresentationArguments.Find(step, "action", "operation"))?.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_')
                ?? throw new PresentationArgumentException($"Say which 'action' to take: {string.Join(", ", ArrangeElementsEdit.Actions)}.");

            if (!ArrangeElementsEdit.Actions.Contains(action))
            {
                throw new PresentationArgumentException($"\"{action}\" is not an arrangement. Use one of: {string.Join(", ", ArrangeElementsEdit.Actions)}.");
            }

            var own = PresentationArguments.Find(step, "elements", "element", "ids") is { } node && !ReferenceEquals(step, call.Arguments.Root)
                ? PresentationArguments.ReadStrings(node)
                : shared;

            edits.Add(new ArrangeElementsEdit
            {
                Slide = slide,
                Elements = own,
                Action = action,
                RelativeTo = PresentationArguments.ReadString(PresentationArguments.Find(step, "relative_to", "align_to")) ?? "selection",
                Spacing = PresentationArguments.ReadLength(PresentationArguments.Find(step, "spacing", "gap")),
                Columns = PresentationArguments.ReadInt(PresentationArguments.Find(step, "columns", "cols")),
            });
        }

        var result = await call.Session.ApplyAsync(deck, edits, $"arranged elements on slide {slide}", cancellationToken);

        return Changed(deck, result);
    }
}
