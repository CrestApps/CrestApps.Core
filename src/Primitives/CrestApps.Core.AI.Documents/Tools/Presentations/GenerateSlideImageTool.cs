using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Generates a picture with the image model and places it on a slide, or behind it as its background.
/// </summary>
internal sealed class GenerateSlideImageTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.GenerateSlideImage;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenerateSlideImageTool"/> class.
    /// </summary>
    public GenerateSlideImageTool()
        : base(
            TheName,
            "Generates a picture with the image model from a description and places it on a slide: in a named position such as right_half, or at x/y/width/height, cropped to fill the box (fit cover) or shown whole (contain). as_background=true puts it behind the slide instead, faded so text stays readable. Describe the style as well as the subject, such as 'flat illustration of a team planning around a whiteboard, soft blue palette'.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slide": { "type": "integer" },
                "prompt": { "type": "string" },
                "shape": { "type": "string", "enum": ["landscape", "portrait", "square"], "description": "The picture's proportions. Defaults to landscape." },
                "position": { "type": "string", "description": "A named area, as in insert_slide_element. Defaults to the right half next to body text, else the content area." },
                "x": LENGTH, "y": LENGTH, "width": LENGTH, "height": LENGTH,
                "fit": { "type": "string", "enum": ["cover", "contain", "stretch"] },
                "alt_text": { "type": "string" },
                "as_background": { "type": "boolean" },
                "transparency": { "type": "number", "description": "For a background: 0 to 100. Defaults to 70." }
              },
              "required": ["slide", "prompt"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Generates and places the picture.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var slide = call.Slide(model);
        var arguments = call.Arguments;
        var prompt = arguments.String("prompt", "description", "image_prompt");

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new PresentationArgumentException("Describe the picture to generate in 'prompt'.");
        }

        var background = arguments.Bool("as_background", "background") == true;
        var shape = arguments.String("shape", "orientation", "aspect") ?? (background ? "landscape" : null);
        var bounds = PresentationArguments.ReadBounds(arguments.Root);

        if (shape is null)
        {
            shape = bounds is { Width: not null, Height: not null } && bounds.Height.Value.Value > bounds.Width.Value.Value ? "portrait" : "landscape";
        }

        var (image, error) = await PresentationImageGenerator.GenerateAsync(call.Services, prompt, shape, cancellationToken);

        if (image is null)
        {
            throw new PresentationArgumentException(error);
        }

        var alt = arguments.String("alt_text", "alt") ?? (prompt.Length > 150 ? prompt[..147] + "..." : prompt);
        PresentationEdit edit = background
            ? new SetBackgroundEdit
            {
                Slides = [slide],
                Background = new PresentationBackgroundSpec { Image = image, ImageTransparency = Math.Clamp(arguments.Number("transparency", "image_transparency") ?? 70, 0, 95) },
            }
            : new InsertElementsEdit
            {
                Slide = slide,
                Elements =
                [
                    new PresentationElementSpec
                    {
                        Kind = PresentationElementSpecKind.Image,
                        Image = image,
                        Bounds = bounds is { IsEmpty: false } ? bounds : null,
                        ImageFit = arguments.String("fit", "image_fit") ?? "cover",
                        AltText = alt,
                    },
                ],
            };

        var result = await call.Session.ApplyAsync(deck, [edit], $"added a generated picture to slide {slide}", cancellationToken);

        return Changed(deck, result, ["Generated a picture of: " + prompt]);
    }
}
