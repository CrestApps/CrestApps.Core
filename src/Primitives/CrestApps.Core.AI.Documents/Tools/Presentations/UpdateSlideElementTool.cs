using System.Text.Json.Nodes;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Changes existing elements: their text, position and size, style, shape, picture, crop, link and
/// accessibility properties.
/// </summary>
internal sealed class UpdateSlideElementTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.UpdateSlideElement;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateSlideElementTool"/> class.
    /// </summary>
    public UpdateSlideElementTool()
        : base(
            TheName,
            "Changes an element on a slide, found by its #id (from get_slide_content), its name, or a role such as title, subtitle or body: replace its text; move or resize it (position, x/y/width/height, scale); rotate or flip it; restyle its text (style) or its fill and outline; change its shape; replace a picture (image_document_id or image_prompt) or crop it; set its link, alt text, name, or hide it. Only what you pass changes. Give several changes in 'updates' to change many elements in one call.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slide": { "type": "integer" },
                "element": { "type": "string", "description": "The element's #id, name, or role (title, subtitle, body, footer, slide_number, picture)." },
                "text": TEXT,
                "paragraph": { "type": "integer", "description": "Replace only this paragraph (counting from 1) with 'text'." },
                "append": { "type": "boolean", "description": "Add 'text' after the existing paragraphs instead of replacing them." },
                "position": { "type": "string", "description": "A named area, as in insert_slide_element." },
                "x": LENGTH, "y": LENGTH, "width": LENGTH, "height": LENGTH,
                "scale": { "type": "number", "description": "Resize by this factor about the element's centre, such as 1.2." },
                "keep_aspect_ratio": { "type": "boolean", "description": "When only width or height is given, keep the proportions." },
                "rotation": { "type": "number" },
                "flip_horizontal": { "type": "boolean" }, "flip_vertical": { "type": "boolean" },
                "style": TEXTSTYLE,
                SHAPESTYLE,
                "shape": { "type": "string", "description": "Change the shape's geometry, such as rounded_rectangle or ellipse." },
                "image_document_id": { "type": "string", "description": "Replace the picture with this uploaded one, keeping its frame." },
                "image_prompt": { "type": "string", "description": "Replace the picture with a generated one." },
                "crop": { "type": "object", "description": "Percentages to cut from each edge.", "properties": { "left": { "type": "number" }, "top": { "type": "number" }, "right": { "type": "number" }, "bottom": { "type": "number" } } },
                "link": { "type": "string", "description": "An https address, 'slide 4', next/previous/first/last, or 'none' to remove." },
                "alt_text": { "type": "string" },
                "decorative": { "type": "boolean" },
                "name": { "type": "string", "description": "Rename the element." },
                "hidden": { "type": "boolean" },
                "updates": { "type": "array", "description": "Several changes, each with 'slide', 'element' and any of the properties above.", "items": { "type": "object" } }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Updates the elements.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var updates = call.Arguments.Array("updates", "changes").OfType<JsonObject>().ToList();
        var defaultSlide = PresentationArguments.ReadInt(PresentationArguments.Find(call.Arguments.Root, "slide", "slide_number"));

        if (updates.Count == 0)
        {
            updates.Add(call.Arguments.Root);
        }

        var edits = new List<PresentationEdit>();
        var notes = new List<string>();

        foreach (var update in updates)
        {
            var slide = PresentationArguments.ReadInt(PresentationArguments.Find(update, "slide", "slide_number")) ?? defaultSlide
                ?? throw new PresentationArgumentException("Each change needs the 'slide' its element is on.");

            if (slide < 1 || slide > model.Slides.Count)
            {
                throw new PresentationArgumentException($"There is no slide {slide}: the deck has {model.Slides.Count} slide(s).");
            }

            edits.Add(await ReadEditAsync(call.Session, update, slide, notes, cancellationToken));
        }

        var result = await call.Session.ApplyAsync(deck, edits, edits.Count == 1 ? "changed an element" : $"changed {edits.Count} elements", cancellationToken);

        return Changed(deck, result, notes);
    }

    private static async Task<UpdateElementEdit> ReadEditAsync(PresentationToolSession session, JsonObject update, int slide, List<string> notes, CancellationToken cancellationToken)
    {
        var element = PresentationArguments.ReadString(PresentationArguments.Find(update, "element", "element_id", "id", "target", "shape"))
            ?? throw new PresentationArgumentException("Name the element to change by its #id, its name, or a role such as title or body.");

        var geometryNode = PresentationArguments.Find(update, "geometry", "new_shape");
        var edit = new UpdateElementEdit
        {
            Slide = slide,
            Element = element,
            Paragraphs = PresentationArguments.ReadParagraphs(PresentationArguments.Find(update, "text", "paragraphs", "bullets", "content")),
            ParagraphNumber = PresentationArguments.ReadInt(PresentationArguments.Find(update, "paragraph", "paragraph_number")),
            AppendParagraphs = PresentationArguments.ReadBool(PresentationArguments.Find(update, "append")) == true,
            Bounds = PresentationArguments.ReadBounds(update) is { IsEmpty: false } bounds ? bounds : null,
            Scale = PresentationArguments.ReadNumber(PresentationArguments.Find(update, "scale", "resize_by")),
            KeepAspectRatio = PresentationArguments.ReadBool(PresentationArguments.Find(update, "keep_aspect_ratio", "lock_aspect_ratio")) == true,
            Rotation = PresentationArguments.ReadNumber(PresentationArguments.Find(update, "rotation", "rotate", "angle")),
            FlipHorizontal = PresentationArguments.ReadBool(PresentationArguments.Find(update, "flip_horizontal", "flip_h")),
            FlipVertical = PresentationArguments.ReadBool(PresentationArguments.Find(update, "flip_vertical", "flip_v")),
            TextStyle = PresentationArguments.ReadTextStyle(update),
            ShapeStyle = PresentationArguments.ReadShapeStyle(update),
            Geometry = PresentationArguments.ReadString(geometryNode),
            AltText = PresentationArguments.ReadString(PresentationArguments.Find(update, "alt_text", "alt", "description")),
            Decorative = PresentationArguments.ReadBool(PresentationArguments.Find(update, "decorative")),
            Name = PresentationArguments.ReadString(PresentationArguments.Find(update, "name", "rename", "new_name")),
            Link = PresentationArguments.ReadLink(PresentationArguments.Find(update, "link", "hyperlink", "url")),
            Hidden = PresentationArguments.ReadBool(PresentationArguments.Find(update, "hidden", "hide")),
        };

        // "shape" names the element when it is the only way the call names one; otherwise it is the geometry.
        if (edit.Geometry is null &&
            PresentationArguments.Find(update, "element", "element_id", "id", "target") is not null &&
            PresentationArguments.ReadString(PresentationArguments.Find(update, "shape")) is { } shape)
        {
            edit.Geometry = shape;
        }

        if (PresentationArguments.Find(update, "crop") is JsonObject crop)
        {
            edit.Crop = new PresentationCropSpec
            {
                Left = Percent(PresentationArguments.Find(crop, "left")),
                Top = Percent(PresentationArguments.Find(crop, "top")),
                Right = Percent(PresentationArguments.Find(crop, "right")),
                Bottom = Percent(PresentationArguments.Find(crop, "bottom")),
            };
        }

        if (PresentationArguments.ReadString(PresentationArguments.Find(update, "image_document_id", "image_document", "image")) is { } document)
        {
            edit.ReplacementImage = await session.LoadImageAsync(document);
            edit.AltText ??= Path.GetFileNameWithoutExtension(edit.ReplacementImage.FileName);
        }
        else if (PresentationArguments.ReadString(PresentationArguments.Find(update, "image_prompt", "prompt")) is { } prompt)
        {
            var (image, error) = await PresentationImageGenerator.GenerateAsync(session.Services, prompt, "landscape", cancellationToken);

            edit.ReplacementImage = image ?? throw new PresentationArgumentException(error);
            edit.AltText ??= prompt.Length > 150 ? prompt[..147] + "..." : prompt;
            notes.Add("Generated a picture of: " + prompt);
        }

        return edit;
    }

    private static double Percent(JsonNode node)
    {
        var value = PresentationArguments.ReadNumber(node) ?? 0;

        // A fraction such as 0.1 means 10%.
        return Math.Clamp(value is > 0 and < 1 ? value * 100 : value, 0, 99);
    }
}
