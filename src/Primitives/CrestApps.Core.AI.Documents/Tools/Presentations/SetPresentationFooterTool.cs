using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Sets footer text, slide numbers and the date on slides.
/// </summary>
internal sealed class SetPresentationFooterTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.SetPresentationFooter;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetPresentationFooterTool"/> class.
    /// </summary>
    public SetPresentationFooterTool()
        : base(
            TheName,
            "Sets the footer of slides: footer text (an empty string removes it), slide numbers on or off, and the date (today's, updated automatically, or a fixed date_text). Applies to every slide except title slides unless 'slides' says otherwise.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "text": { "type": "string" },
                "slide_numbers": { "type": "boolean" },
                "date": { "type": "boolean" },
                "date_text": { "type": "string", "description": "A fixed date to show instead of today's." },
                "include_title_slides": { "type": "boolean" },
                "slides": { "type": ["integer", "string", "array"] }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Sets the footer.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var arguments = call.Arguments;
        var edit = new SetFooterEdit
        {
            FooterText = arguments.String("text", "footer", "footer_text"),
            SlideNumbers = arguments.Bool("slide_numbers", "numbers", "page_numbers"),
            Date = arguments.Bool("date", "show_date") ?? (arguments.String("date_text") is not null ? true : null),
            DateText = arguments.String("date_text", "fixed_date"),
            SkipTitleSlides = arguments.Bool("include_title_slides") != true,
            Slides = call.Slides(model, false),
        };

        if (edit.FooterText is null && edit.SlideNumbers is null && edit.Date is null)
        {
            throw new PresentationArgumentException("Give footer text, slide_numbers or date.");
        }

        var result = await call.Session.ApplyAsync(deck, [edit], "set the footer", cancellationToken);

        return Changed(deck, result);
    }
}
