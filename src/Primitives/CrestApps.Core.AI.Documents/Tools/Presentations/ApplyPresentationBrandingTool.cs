using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Brands a deck in one step: brand colours and fonts into the theme, a logo on the slides, and footer text.
/// </summary>
internal sealed class ApplyPresentationBrandingTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.ApplyPresentationBranding;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplyPresentationBrandingTool"/> class.
    /// </summary>
    public ApplyPresentationBrandingTool()
        : base(
            TheName,
            "Applies brand identity in one step: brand colours and fonts go into the theme (so every slide follows), an uploaded logo is placed in the same corner of every slide (through the slide master) or of chosen slides, and footer text such as the company name or a confidentiality notice is added. Pass only the parts you have.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "primary_color": COLOR,
                "secondary_color": COLOR,
                "accent_colors": { "type": "array", "items": { "type": "string" }, "description": "Further brand colours, used for accent3 onwards." },
                "heading_font": { "type": "string" },
                "body_font": { "type": "string" },
                "logo_document_id": { "type": "string", "description": "An uploaded logo picture." },
                "logo_position": { "type": "string", "enum": ["top_left", "top_right", "bottom_left", "bottom_right"] },
                "logo_width": LENGTH,
                "footer": { "type": "string" },
                "slide_numbers": { "type": "boolean" },
                "slides": { "type": ["integer", "string", "array"], "description": "Where the logo goes. Omit to put it on the slide master, so every slide shows it." }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Applies the branding.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var arguments = call.Arguments;
        var edits = new List<PresentationEdit>();
        var theme = new UpdateThemeEdit
        {
            HeadingFont = arguments.String("heading_font", "title_font"),
            BodyFont = arguments.String("body_font", "font"),
        };

        if (arguments.String("primary_color", "primary", "brand_color") is { } primary)
        {
            theme.Colors["accent1"] = primary;
        }

        if (arguments.String("secondary_color", "secondary") is { } secondary)
        {
            theme.Colors["accent2"] = secondary;
        }

        var accents = arguments.Strings("accent_colors", "colors", "palette");

        for (var index = 0; index < accents.Count && index < 4; index++)
        {
            theme.Colors["accent" + (index + 3)] = accents[index];
        }

        if (theme.Colors.Count > 0 || theme.HeadingFont is not null || theme.BodyFont is not null)
        {
            edits.Add(theme);
        }

        if (arguments.String("logo_document_id", "logo", "logo_image") is { } logo)
        {
            var slides = call.Slides(model, false);

            edits.Add(new AddLogoEdit
            {
                Logo = await call.Session.LoadImageAsync(logo),
                Position = arguments.String("logo_position", "position") ?? "top_right",
                Width = PresentationArguments.ReadLength(arguments.Node("logo_width", "width")),
                Slides = slides,
            });
        }

        var footer = arguments.String("footer", "footer_text");
        var numbers = arguments.Bool("slide_numbers", "numbers");

        if (footer is not null || numbers is not null)
        {
            edits.Add(new SetFooterEdit { FooterText = footer, SlideNumbers = numbers });
        }

        if (edits.Count == 0)
        {
            throw new PresentationArgumentException("Give brand colours, fonts, a logo_document_id or footer text.");
        }

        var result = await call.Session.ApplyAsync(deck, edits, "applied the branding", cancellationToken);

        return Changed(deck, result);
    }
}
