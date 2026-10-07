using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Recolours and re-fonts a deck through its theme, so everything that uses theme colours and fonts follows.
/// </summary>
internal sealed class UpdatePresentationThemeTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.UpdatePresentationTheme;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatePresentationThemeTool"/> class.
    /// </summary>
    public UpdatePresentationThemeTool()
        : base(
            TheName,
            "Changes the presentation's theme: its colour palette (accent1-accent6, text and background colours) and its heading and body fonts. Every slide, chart, table and shape that uses theme colours or fonts changes with it, which is the cleanest way to recolour a deck. 'preset' switches to one of the built-in themes: office, modern, corporate, dark, vibrant, minimal, nature, warm, ocean.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "preset": { "type": "string", "enum": ["office", "modern", "corporate", "dark", "vibrant", "minimal", "nature", "warm", "ocean"] },
                "colors": { "type": "object", "description": "Colours by slot: primary/accent1 ... accent6, text1, text2, background1, background2, hyperlink.", "additionalProperties": { "type": "string" } },
                "heading_font": { "type": "string" },
                "body_font": { "type": "string" },
                "name": { "type": "string", "description": "A new name for the theme." }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Updates the theme.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var arguments = call.Arguments;
        var edit = new UpdateThemeEdit
        {
            HeadingFont = arguments.String("heading_font", "title_font", "headings_font"),
            BodyFont = arguments.String("body_font", "text_font"),
            Name = arguments.String("name", "theme_name"),
        };

        if (arguments.String("preset", "theme") is { } presetName)
        {
            if (!PresentationThemePresets.TryGet(presetName, out var preset))
            {
                throw new PresentationArgumentException($"\"{presetName}\" is not a theme. Use one of: {string.Join(", ", PresentationThemePresets.All.Select(candidate => candidate.Name))}.");
            }

            foreach (var (slot, color) in preset.Colors)
            {
                edit.Colors[slot] = "#" + color;
            }

            edit.HeadingFont ??= preset.HeadingFont;
            edit.BodyFont ??= preset.BodyFont;
            edit.Name ??= char.ToUpperInvariant(preset.Name[0]) + preset.Name[1..];
        }

        if (arguments.Object("colors", "palette", "theme_colors") is { } colors)
        {
            foreach (var (slot, value) in colors)
            {
                if (PresentationArguments.ReadString(value) is { } color)
                {
                    edit.Colors[slot] = color;
                }
            }
        }

        if (edit.Colors.Count == 0 && edit.HeadingFont is null && edit.BodyFont is null && edit.Name is null)
        {
            throw new PresentationArgumentException("Give a preset, theme colors, or fonts to change.");
        }

        var result = await call.Session.ApplyAsync(deck, [edit], "changed the theme", cancellationToken);

        return Changed(deck, result);
    }
}
