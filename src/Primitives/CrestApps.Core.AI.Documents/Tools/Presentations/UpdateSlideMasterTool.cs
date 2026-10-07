using System.Text.Json.Nodes;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Changes a slide master or one of its layouts: background, title and body text styles, and where its
/// placeholders sit.
/// </summary>
internal sealed class UpdateSlideMasterTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.UpdateSlideMaster;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateSlideMasterTool"/> class.
    /// </summary>
    public UpdateSlideMasterTool()
        : base(
            TheName,
            "Changes the slide master, which every slide inherits from, or one layout: its background, the text style of titles and body text, where its title and body placeholders sit, or its name. Slides that override these keep their own formatting.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "target": { "type": "string", "description": "'master' (default) or the name of a layout, as get_presentation_theme lists them." },
                "background": { "description": "A colour, a list of gradient colours, or {color, gradient, image_document_id, image_transparency}." },
                "title_style": TEXTSTYLE,
                "body_style": TEXTSTYLE,
                "placeholders": { "type": "object", "description": "Where placeholders sit, by kind (title, body, footer, date, slide_number): each {position | x, y, width, height}.", "additionalProperties": { "type": "object" } },
                "rename": { "type": "string" }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Updates the master.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var arguments = call.Arguments;
        var edit = new UpdateMasterEdit
        {
            Target = arguments.String("target", "layout", "master") ?? "master",
            TitleStyle = PresentationArguments.ReadTextStyle(arguments.Object("title_style", "title")),
            BodyStyle = PresentationArguments.ReadTextStyle(arguments.Object("body_style", "body")),
            Rename = arguments.String("rename", "new_name"),
        };

        if (arguments.Node("background") is { } background)
        {
            edit.Background = PresentationArguments.ReadBackground(background, out var imageDocument);

            if (edit.Background is not null && !string.IsNullOrWhiteSpace(imageDocument))
            {
                edit.Background.Image = await call.Session.LoadImageAsync(imageDocument);
            }
        }

        if (arguments.Object("placeholders", "placeholder_positions") is { } placeholders)
        {
            foreach (var (kind, value) in placeholders)
            {
                if (value is JsonObject bounds && PresentationArguments.ReadBounds(bounds) is { IsEmpty: false } spec)
                {
                    edit.PlaceholderBounds[kind] = spec;
                }
            }
        }

        if (edit.Background is null && edit.TitleStyle is null && edit.BodyStyle is null && edit.PlaceholderBounds.Count == 0 && edit.Rename is null)
        {
            throw new PresentationArgumentException("Say what to change: background, title_style, body_style, placeholders or rename.");
        }

        var result = await call.Session.ApplyAsync(deck, [edit], $"changed the {edit.Target}", cancellationToken);

        return Changed(deck, result);
    }
}
