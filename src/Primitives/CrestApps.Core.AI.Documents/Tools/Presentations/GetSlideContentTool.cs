using System.Text;
using CrestApps.Core.AI.Documents.Presentations;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Describes one slide completely.
/// </summary>
internal sealed class GetSlideContentTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.GetSlideContent;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetSlideContentTool"/> class.
    /// </summary>
    public GetSlideContentTool()
        : base(
            TheName,
            "Returns everything about one slide: its layout and background, then every element back to front with its #id, name, position and size in points, rotation, fill and outline, every paragraph with its level, bullet, font, size and colour, full table cells, chart data, picture details, alternative text and links, and the speaker notes. Use it before editing a slide precisely, such as moving elements or matching a style.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slide": { "type": "integer", "description": "The slide number." }
              },
              "required": ["slide"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Describes the slide.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var number = call.Slide(model);
        var slide = model.FindSlide(number);
        var builder = new StringBuilder();

        builder.AppendLine(PresentationDescriber.Heading(deck, model));
        builder.AppendLine(PresentationDescriber.SlideContent(model, slide));

        var layout = model.Masters.SelectMany(master => master.Layouts).FirstOrDefault(candidate => candidate.Name == slide.LayoutName);

        if (layout is not null)
        {
            builder.AppendLine();
            builder.Append("Its layout offers placeholders: ").AppendJoin(", ", layout.Placeholders.Where(placeholder => placeholder.Type is not ("dt" or "ftr" or "sldNum")).Select(placeholder => placeholder.Type + " " + PresentationDescriber.Bounds(placeholder.Bounds))).AppendLine(".");
        }

        return builder.ToString().TrimEnd();
    }
}
