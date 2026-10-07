using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Switches slides to another layout, carrying their content into the new layout's placeholders.
/// </summary>
internal sealed class ApplySlideLayoutTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.ApplySlideLayout;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplySlideLayoutTool"/> class.
    /// </summary>
    public ApplySlideLayoutTool()
        : base(
            TheName,
            "Switches slides to another layout of the deck — such as title_and_content, two_content, comparison, section_header, title_only, blank, or a layout name listed by get_presentation_theme — moving their title and body into the new layout's placeholders. Content the new layout has no place for is kept as free elements.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slides": { "type": ["integer", "string", "array"] },
                "layout": { "type": "string" }
              },
              "required": ["slides", "layout"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Applies the layout.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var slides = call.Slides(model, true);
        var layout = call.Arguments.String("layout", "layout_name")
            ?? throw new PresentationArgumentException("Name the 'layout' to switch to; get_presentation_theme lists the deck's layouts.");

        var result = await call.Session.ApplyAsync(deck, [new ApplyLayoutEdit { Slides = slides, Layout = layout }], $"applied the {layout} layout", cancellationToken);

        return Changed(deck, result);
    }
}
