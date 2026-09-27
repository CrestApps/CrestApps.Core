using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Restyles text, shapes, tables, charts and backgrounds across a deck, some slides, or one element, and
/// remembers a deck-wide style as the deck's house style.
/// </summary>
internal sealed class FormatPresentationTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.FormatPresentation;

    /// <summary>
    /// Initializes a new instance of the <see cref="FormatPresentationTool"/> class.
    /// </summary>
    public FormatPresentationTool()
        : base(
            TheName,
            "Restyles the presentation consistently: title, subtitle and body text (font, size, colour, bold, alignment, spacing), free text boxes, shapes (fill, gradient, outline, shadow), tables, charts and slide backgrounds. Applies to every slide, to chosen 'slides', or to one 'element' on one slide. match_slide copies the text styles of an existing slide. Deck-wide formatting is remembered as the house style, so slides and elements added later match it.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slides": { "type": ["integer", "string", "array"], "description": "Only these slides. Omit for every slide." },
                "element": { "type": ["integer", "string"], "description": "Only this element (needs a single slide)." },
                "title": TEXTSTYLE,
                "subtitle": TEXTSTYLE,
                "body": TEXTSTYLE,
                "text": TEXTSTYLE,
                "shape": { "type": "object", "description": "Shape fill and outline.", "properties": { SHAPESTYLE } },
                "table": TABLESTYLE,
                "chart": CHARTSTYLE,
                "background": { "description": "A colour, or a list of gradient colours." },
                "match_slide": { "type": "integer", "description": "Copy the title and body text styles of this slide." },
                "remember": { "type": "boolean", "description": "Keep as the house style for new content. Defaults to true for deck-wide formatting." }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Formats the deck.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var arguments = call.Arguments;
        var slides = call.Slides(model, false);
        var element = arguments.String("element", "element_id");
        var formatting = new PresentationFormatting
        {
            Title = PresentationArguments.ReadTextStyle(arguments.Object("title", "title_style", "headings")),
            Subtitle = PresentationArguments.ReadTextStyle(arguments.Object("subtitle", "subtitle_style")),
            Body = PresentationArguments.ReadTextStyle(arguments.Object("body", "body_style", "content")),
            Text = PresentationArguments.ReadTextStyle(arguments.Object("text", "text_style", "text_boxes", "style")),
            Shape = PresentationArguments.ReadShapeStyle(arguments.Object("shape", "shapes", "shape_style")),
            Table = PresentationArguments.ReadTableStyle(arguments.Object("table", "tables", "table_style")),
            Chart = PresentationArguments.ReadChartStyle(arguments.Object("chart", "charts", "chart_style")),
        };

        if (arguments.Node("background", "background_color") is { } background)
        {
            var spec = PresentationArguments.ReadBackground(background, out _);
            formatting.BackgroundColor = spec?.Color;
            formatting.BackgroundGradient = spec?.GradientColors;
        }

        if (arguments.Int("match_slide", "like_slide", "copy_from_slide") is { } source)
        {
            if (source < 1 || source > model.Slides.Count)
            {
                throw new PresentationArgumentException($"There is no slide {source} to match.");
            }

            var matched = Match(model.Slides[source - 1]);
            formatting.Title = PresentationTextStyle.Combine(matched.Title, formatting.Title);
            formatting.Body = PresentationTextStyle.Combine(matched.Body, formatting.Body);
        }

        if (formatting.IsEmpty)
        {
            throw new PresentationArgumentException("Say how to format: title, subtitle, body, text, shape, table, chart or background styles, or match_slide.");
        }

        var edit = new FormatEdit { Slides = slides, Element = element, Formatting = formatting };
        var result = await call.Session.ApplyAsync(deck, [edit], string.IsNullOrWhiteSpace(element) ? "reformatted slides" : "reformatted an element", cancellationToken);
        var notes = new List<string>();
        var deckWide = slides.Count == 0 && string.IsNullOrWhiteSpace(element);

        if (arguments.Bool("remember", "house_style", "save_as_default") ?? deckWide)
        {
            deck.Formatting = (deck.Formatting ?? new PresentationFormatting()).Merge(formatting);
            await call.Session.Workspace.SaveStateAsync();
            notes.Add("Kept as the deck's house style: slides and elements added later will match.");
        }

        return Changed(deck, result, notes);
    }

    private static PresentationFormatting Match(PresentationSlide slide)
    {
        var title = slide.AllElements().FirstOrDefault(element => element.IsTitle && element.Text?.HasText == true);
        var body = slide.AllElements().FirstOrDefault(element => !element.IsTitle && element.PlaceholderType is "body" or "obj" or null && element.Text?.HasText == true && element.Kind == PresentationElementKind.Shape);

        return new PresentationFormatting
        {
            Title = StyleOf(title),
            Body = StyleOf(body),
        };
    }

    private static PresentationTextStyle StyleOf(PresentationElement element)
    {
        var paragraph = element?.Text?.Paragraphs.FirstOrDefault(candidate => candidate.Runs.Any(run => !string.IsNullOrWhiteSpace(run.Text)));
        var run = paragraph?.Runs.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate.Text));

        if (run is null)
        {
            return null;
        }

        return new PresentationTextStyle
        {
            Font = run.Font,
            Size = run.Size,
            Bold = run.Bold,
            Italic = run.Italic,
            Color = string.IsNullOrEmpty(run.Color) ? null : "#" + run.Color.TrimStart('#'),
            Alignment = paragraph.Alignment,
        };
    }
}
