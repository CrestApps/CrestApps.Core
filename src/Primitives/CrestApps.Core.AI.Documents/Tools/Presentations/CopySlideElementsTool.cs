using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Copies or moves elements to the same or another slide.
/// </summary>
internal sealed class CopySlideElementsTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.CopySlideElements;

    /// <summary>
    /// Initializes a new instance of the <see cref="CopySlideElementsTool"/> class.
    /// </summary>
    public CopySlideElementsTool()
        : base(
            TheName,
            "Copies elements, with their pictures and charts, to another slide or the same one, or moves them with move=true. Copies on the same slide are offset so they do not sit on top of the originals.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slide": { "type": "integer", "description": "The slide the elements are on." },
                "elements": { "type": "array", "items": { "type": ["integer", "string"] }, "description": "The elements' #ids or names." },
                "target_slide": { "type": "integer", "description": "The slide to copy them to. Defaults to the same slide." },
                "move": { "type": "boolean" },
                "offset_x": LENGTH,
                "offset_y": LENGTH
              },
              "required": ["slide", "elements"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Copies the elements.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var slide = call.Slide(model);
        var elements = PresentationElementReferences.Read(call.Arguments);
        var target = call.Arguments.Slides(model.Slides.Count, "target_slide", "to_slide", "destination").FirstOrDefault(slide);

        if (target < 1 || target > model.Slides.Count)
        {
            throw new PresentationArgumentException($"There is no slide {target}: the deck has {model.Slides.Count} slide(s).");
        }

        var move = call.Arguments.Bool("move", "cut") == true;
        var edit = new CopyElementsEdit
        {
            Slide = slide,
            Elements = elements,
            TargetSlide = target,
            Move = move,
            OffsetX = PresentationArguments.ReadLength(call.Arguments.Node("offset_x", "dx")),
            OffsetY = PresentationArguments.ReadLength(call.Arguments.Node("offset_y", "dy")),
        };

        var result = await call.Session.ApplyAsync(deck, [edit], $"{(move ? "moved" : "copied")} elements from slide {slide} to slide {target}", cancellationToken);

        return Changed(deck, result);
    }
}
