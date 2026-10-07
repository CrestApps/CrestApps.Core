using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Duplicates a slide with everything on it.
/// </summary>
internal sealed class DuplicateSlideTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.DuplicateSlide;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateSlideTool"/> class.
    /// </summary>
    public DuplicateSlideTool()
        : base(
            TheName,
            "Duplicates a slide, with its layout, elements, charts, pictures and notes. The copy goes right after the original unless 'position' says where. Use it to make a new slide that looks like an existing one, then change its text with update_slide or update_slide_text.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slide": { "type": "integer", "description": "The slide to copy." },
                "position": { "type": "integer", "description": "Where the copy goes, counting from 1." },
                "copies": { "type": "integer", "description": "How many copies. Defaults to 1." }
              },
              "required": ["slide"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Duplicates the slide.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var slide = call.Slide(model);
        var copies = Math.Clamp(call.Arguments.Int("copies", "count", "times") ?? 1, 1, 20);
        var position = call.Arguments.Int("position", "at", "index");

        if (model.Slides.Count + copies > call.Session.Options.MaxSlides)
        {
            throw new PresentationArgumentException($"A presentation can have at most {call.Session.Options.MaxSlides} slides.");
        }

        if (position is < 1 || position > model.Slides.Count + 1)
        {
            throw new PresentationArgumentException($"The position must be between 1 and {model.Slides.Count + 1}.");
        }

        // The original moves down when a copy goes before it, so each copy is taken from where it now is.
        var edits = new List<PresentationEdit>();
        var source = slide;

        for (var copy = 0; copy < copies; copy++)
        {
            var target = position is { } at ? at + copy : slide + 1 + copy;

            edits.Add(new DuplicateSlideEdit { Slide = source, Position = target });

            if (target <= source)
            {
                source++;
            }
        }

        var result = await call.Session.ApplyAsync(deck, edits, $"duplicated slide {slide}", cancellationToken);

        return Changed(deck, result);
    }
}
