using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Moves slides to new positions.
/// </summary>
internal sealed class MoveSlideTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.MoveSlide;

    /// <summary>
    /// Initializes a new instance of the <see cref="MoveSlideTool"/> class.
    /// </summary>
    public MoveSlideTool()
        : base(
            TheName,
            "Reorders the presentation. Either move one slide with 'slide' and 'position', or give the whole new order in 'order' as the current slide numbers, such as [1, 3, 2, 4, 5].",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slide": { "type": "integer", "description": "The slide to move." },
                "position": { "type": "integer", "description": "Its new position, counting from 1." },
                "order": { "type": "array", "items": { "type": "integer" }, "description": "Every slide number in the new order." }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Moves the slides.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var order = call.Arguments.Slides(model.Slides.Count, "order", "new_order");
        var edits = new List<PresentationEdit>();

        if (order.Count > 0)
        {
            if (order.Count != model.Slides.Count || order.Any(number => number < 1 || number > model.Slides.Count))
            {
                throw new PresentationArgumentException($"'order' must list each of the {model.Slides.Count} slides exactly once.");
            }

            // Moves each slide into its place in turn, tracking where the others end up.
            var current = Enumerable.Range(1, model.Slides.Count).ToList();

            for (var target = 0; target < order.Count; target++)
            {
                var from = current.IndexOf(order[target]);

                if (from == target)
                {
                    continue;
                }

                edits.Add(new MoveSlideEdit { Slide = from + 1, Position = target + 1 });

                var moved = current[from];
                current.RemoveAt(from);
                current.Insert(target, moved);
            }
        }
        else
        {
            var slide = call.Slide(model);
            var position = call.Arguments.Int("position", "to", "new_position", "index")
                ?? throw new PresentationArgumentException("Give the slide's new 'position', counting from 1.");

            if (position < 1 || position > model.Slides.Count)
            {
                throw new PresentationArgumentException($"The position must be between 1 and {model.Slides.Count}.");
            }

            if (position != slide)
            {
                edits.Add(new MoveSlideEdit { Slide = slide, Position = position });
            }
        }

        if (edits.Count == 0)
        {
            return "The slides are already in that order.";
        }

        var result = await call.Session.ApplyAsync(deck, edits, "reordered the slides", cancellationToken);

        return Changed(deck, result);
    }
}
