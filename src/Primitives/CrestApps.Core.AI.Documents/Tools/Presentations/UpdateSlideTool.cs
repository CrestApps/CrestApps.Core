using System.Text.Json.Nodes;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Rewrites slides' titles, bodies and notes, and changes their layout, background, transition or visibility,
/// one slide or many in one call.
/// </summary>
internal sealed class UpdateSlideTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.UpdateSlide;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateSlideTool"/> class.
    /// </summary>
    public UpdateSlideTool()
        : base(
            TheName,
            "Changes slides: replaces the title, subtitle, body or speaker notes, switches the layout, sets the background, the transition, or hides the slide. Give one slide's changes at the top level, or several in 'updates' to change many slides in one call. Only what you pass changes; the body replaces the whole body, so use update_slide_text to change a single paragraph.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slide": { "type": "integer" },
                "title": { "type": "string" },
                "subtitle": { "type": "string" },
                "body": TEXT,
                "right_body": TEXT,
                "notes": { "type": "string", "description": "Speaker notes; an empty string removes them." },
                "layout": { "type": "string", "description": "Switch to this layout, keeping the slide's content." },
                "background": { "description": "A colour, a list of gradient colours, or {color, gradient, image_document_id, image_transparency, reset}." },
                "transition": { "type": "string", "enum": ["none", "fade", "push", "wipe", "split", "cover", "cut", "zoom"] },
                "hidden": { "type": "boolean" },
                "updates": { "type": "array", "description": "Several slides' changes, each with 'slide' and any of the properties above.", "items": { "type": "object" } }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Updates the slides.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var updates = call.Arguments.Array("updates", "changes", "slides").OfType<JsonObject>().ToList();

        if (updates.Count == 0)
        {
            updates.Add(call.Arguments.Root);
        }

        var edits = new List<PresentationEdit>();

        foreach (var update in updates)
        {
            var slide = PresentationArguments.ReadInt(PresentationArguments.Find(update, "slide", "slide_number", "number"))
                ?? throw new PresentationArgumentException("Each update needs the 'slide' it changes.");

            if (slide < 1 || slide > model.Slides.Count)
            {
                throw new PresentationArgumentException($"There is no slide {slide}: the deck has {model.Slides.Count} slide(s).");
            }

            await AddEditsAsync(call.Session, update, slide, edits);
        }

        if (edits.Count == 0)
        {
            throw new PresentationArgumentException("Say what to change: title, subtitle, body, right_body, notes, layout, background, transition or hidden.");
        }

        var result = await call.Session.ApplyAsync(deck, edits, updates.Count == 1 ? "updated a slide" : $"updated {updates.Count} slides", cancellationToken);

        return Changed(deck, result);
    }

    private static async Task AddEditsAsync(PresentationToolSession session, JsonObject update, int slide, List<PresentationEdit> edits)
    {
        // The layout changes first, so the new text lands in the new layout's placeholders.
        if (PresentationArguments.ReadString(PresentationArguments.Find(update, "layout")) is { } layout)
        {
            edits.Add(new ApplyLayoutEdit { Slides = [slide], Layout = layout });
        }

        var edit = new UpdateSlideEdit
        {
            Slide = slide,
            Title = PresentationArguments.ReadString(PresentationArguments.Find(update, "title", "heading")),
            Subtitle = PresentationArguments.ReadString(PresentationArguments.Find(update, "subtitle")),
            Body = PresentationArguments.ReadParagraphs(PresentationArguments.Find(update, "body", "bullets", "content", "text", "left_body")),
            SecondBody = PresentationArguments.ReadParagraphs(PresentationArguments.Find(update, "right_body", "second_body")),
            Notes = PresentationArguments.ReadString(PresentationArguments.Find(update, "notes", "speaker_notes")),
            Hidden = PresentationArguments.ReadBool(PresentationArguments.Find(update, "hidden", "hide")),
            Transition = PresentationArguments.ReadString(PresentationArguments.Find(update, "transition"))?.Trim().ToLowerInvariant(),
        };

        if (edit.Title is not null || edit.Subtitle is not null || edit.Body is not null || edit.SecondBody is not null || edit.Notes is not null || edit.Hidden is not null || edit.Transition is not null)
        {
            edits.Add(edit);
        }

        if (PresentationArguments.Find(update, "background") is { } node)
        {
            var background = PresentationArguments.ReadBackground(node, out var imageDocument)
                ?? throw new PresentationArgumentException("The background must be a colour, a list of gradient colours or an object.");

            if (!string.IsNullOrWhiteSpace(imageDocument))
            {
                background.Image = await session.LoadImageAsync(imageDocument);
            }

            edits.Add(new SetBackgroundEdit { Slides = [slide], Background = background });
        }
    }
}
