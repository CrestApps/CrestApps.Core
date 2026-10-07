using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Sets slide backgrounds: a colour, a gradient, an uploaded or generated picture, or the layout's own.
/// </summary>
internal sealed class SetSlideBackgroundTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.SetSlideBackground;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetSlideBackgroundTool"/> class.
    /// </summary>
    public SetSlideBackgroundTool()
        : base(
            TheName,
            "Sets the background of slides, or of the whole deck through its master: a colour, a gradient, an uploaded picture, or a picture generated from image_prompt, optionally faded with image_transparency so text stays readable. reset=true returns slides to their layout's background. Text colours are not changed, so check contrast with check_presentation.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slides": { "type": ["integer", "string", "array"], "description": "The slides. Omit, or 'all', for every slide." },
                "apply_to_master": { "type": "boolean", "description": "Set it on the slide master, so every slide and new slide gets it." },
                "color": COLOR,
                "gradient": { "type": "array", "items": { "type": "string" } },
                "gradient_angle": { "type": "number" },
                "image_document_id": { "type": "string" },
                "image_prompt": { "type": "string", "description": "Describe a background picture to generate." },
                "image_transparency": { "type": "number", "description": "0 to 100; 60-80 keeps text readable over a photo." },
                "reset": { "type": "boolean" }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Sets the background.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var slides = call.Slides(model, false);
        var arguments = call.Arguments;
        var background = PresentationArguments.ReadBackground(arguments.Object("background") ?? arguments.Root, out var imageDocument) ?? new PresentationBackgroundSpec();
        var notes = new List<string>();

        if (!string.IsNullOrWhiteSpace(imageDocument))
        {
            background.Image = await call.Session.LoadImageAsync(imageDocument);
        }
        else if (arguments.String("image_prompt", "prompt") is { } prompt)
        {
            var (image, error) = await PresentationImageGenerator.GenerateAsync(call.Services, prompt, "landscape", cancellationToken);

            background.Image = image ?? throw new PresentationArgumentException(error);
            notes.Add("Generated a background picture of: " + prompt);
        }

        if (background.IsEmpty)
        {
            throw new PresentationArgumentException("Give a color, a gradient, image_document_id, image_prompt, or reset=true.");
        }

        var edit = new SetBackgroundEdit
        {
            Slides = slides,
            ApplyToMaster = arguments.Bool("apply_to_master", "master", "whole_deck") == true,
            Background = background,
        };

        var result = await call.Session.ApplyAsync(deck, [edit], "changed the background", cancellationToken);

        return Changed(deck, result, notes);
    }
}
