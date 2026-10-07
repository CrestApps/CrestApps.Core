using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Turns a list of steps, stages or people into a diagram of editable shapes, on an existing slide or a new
/// one.
/// </summary>
internal sealed class GenerateSlideDiagramTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.GenerateSlideDiagram;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenerateSlideDiagramTool"/> class.
    /// </summary>
    public GenerateSlideDiagramTool()
        : base(
            TheName,
            "Draws a diagram from a list: process (steps with arrows), chevron, cycle, timeline (milestones), hierarchy (org chart: give each item its parent), pyramid, funnel, matrix (2x2), venn (2-3 circles) or cards (a grid of points). It is built from ordinary shapes coloured from the theme, so it stays editable and restylable. Put it on an existing slide, or pass new_slide_title to add a slide for it. Use it to turn a text-heavy bullet slide into a visual.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "diagram_type": { "type": "string", "enum": ["process", "chevron", "cycle", "timeline", "hierarchy", "pyramid", "funnel", "matrix", "venn", "cards"] },
                "items": { "type": "array", "items": { "type": ["string", "object"], "properties": { "text": { "type": "string" }, "detail": { "type": "string" }, "parent": { "type": "string" }, "color": { "type": "string" } } }, "description": "The steps, stages or nodes, in order: strings, or {text, detail, parent, color}. At most 12." },
                "center_text": { "type": "string", "description": "For cycle or venn: text in the middle." },
                "colors": { "type": "array", "items": { "type": "string" }, "description": "Fill colours used in turn. Defaults to the theme's accents." },
                "slide": { "type": "integer", "description": "The slide to draw it on." },
                "new_slide_title": { "type": "string", "description": "Add a new slide with this title for the diagram instead." },
                "new_slide_position": { "type": "integer" },
                "replace_body": { "type": "boolean", "description": "Replace the slide's body text with the diagram. Defaults to true when the body is empty." },
                "position": { "type": "string", "description": "A named area, as in insert_slide_element. Defaults to the content area, or beside body text." },
                "x": LENGTH, "y": LENGTH, "width": LENGTH, "height": LENGTH,
                "name": { "type": "string" },
                "alt_text": { "type": "string", "description": "Defaults to a description built from the items." }
              },
              "required": ["diagram_type", "items"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Draws the diagram.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var arguments = call.Arguments;
        var diagram = PresentationArguments.ReadDiagram(arguments.Root, null);

        if (diagram.Items.Count == 0)
        {
            throw new PresentationArgumentException("Give the diagram's 'items', such as [\"Plan\", \"Build\", \"Launch\"].");
        }

        var bounds = PresentationArguments.ReadBounds(arguments.Root);
        var element = new PresentationElementSpec
        {
            Kind = PresentationElementSpecKind.Diagram,
            Diagram = diagram,
            Name = arguments.String("name") ?? char.ToUpperInvariant(diagram.Kind[0]) + diagram.Kind[1..] + " diagram",
            Bounds = bounds is { IsEmpty: false } ? bounds : null,
            AltText = arguments.String("alt_text", "alt") ?? $"{char.ToUpperInvariant(diagram.Kind[0]) + diagram.Kind[1..]} diagram: {string.Join(", ", diagram.Items.Select(item => item.Text))}.",
        };

        var edits = new List<PresentationEdit>();
        string description;

        if (arguments.String("new_slide_title", "title") is { } title)
        {
            edits.Add(new AddSlideEdit
            {
                Position = arguments.Int("new_slide_position", "position_in_deck"),
                Layout = "title_only",
                Title = title,
                Elements = [element],
            });

            description = "added a diagram slide";
        }
        else
        {
            var slide = call.Slide(model);
            var body = BodyPlaceholder(model.Slides[slide - 1]);
            var replace = arguments.Bool("replace_body", "replace_text");

            // The diagram takes the place of an empty body, or of the bullets it was drawn from when asked.
            if (body is not null && element.Bounds is null && (replace == true || (replace is null && body.Text?.HasText != true)))
            {
                edits.Add(new DeleteElementsEdit { Slide = slide, Elements = [body.Id.ToString(CultureInfo.InvariantCulture)] });
            }

            edits.Add(new InsertElementsEdit { Slide = slide, Elements = [element] });
            description = $"added a diagram to slide {slide}";
        }

        var result = await call.Session.ApplyAsync(deck, edits, description, cancellationToken);

        return Changed(deck, result, [$"The {diagram.Kind} diagram is a group of editable shapes; restyle it with update_slide_element or format_presentation."]);
    }

    private static PresentationElement BodyPlaceholder(PresentationSlide slide)
    {
        return slide.Elements.FirstOrDefault(element => element.PlaceholderType is "body" or "obj" || (element.PlaceholderType is null && element.PlaceholderIndex is not null));
    }
}
