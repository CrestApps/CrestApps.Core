using CrestApps.Core.AI.Documents.Presentations;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Adds one or more slides, each from a layout with its title, body, notes and elements.
/// </summary>
internal sealed class AddSlideTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.AddSlide;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddSlideTool"/> class.
    /// </summary>
    public AddSlideTool()
        : base(
            TheName,
            "Adds slides to the presentation. Pass 'slides' with one object per slide, each with a layout (title, title_and_content, section_header, two_content, comparison, title_only, blank, content_with_caption, picture_with_caption, or one of the deck's own layouts), title, body bullets, notes and optional elements such as charts, tables, pictures, shapes and icons. Slides go at the end unless a slide gives its position. Text that is too long for its box is shrunk to fit and reported, so keep bullets short.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slides": { "type": "array", "items": SLIDE, "description": "The slides to add, in order." }
              },
              "required": ["slides"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Adds the slides.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var items = call.Arguments.Array("slides", "slide");

        // A single slide written at the top level, as models sometimes do.
        if (items.Count == 0 && call.Arguments.Node("title", "layout", "body") is not null)
        {
            items.Add(call.Arguments.Root);
        }

        if (items.Count == 0)
        {
            throw new PresentationArgumentException("Give the slides to add in 'slides', such as [{\"title\": \"Overview\", \"body\": [\"First point\", \"Second point\"]}].");
        }

        var requests = items.Select(PresentationArguments.ReadSlide).ToList();
        var model = await call.ReadAsync(deck, cancellationToken);

        if (model.Slides.Count + requests.Count > call.Session.Options.MaxSlides)
        {
            throw new PresentationArgumentException($"A presentation can have at most {call.Session.Options.MaxSlides} slides; this one has {model.Slides.Count}.");
        }

        var notes = new List<string>();
        var edits = await PresentationRequestBuilder.SlidesAsync(call.Session, requests, notes, cancellationToken);
        var result = await call.Session.ApplyAsync(deck, edits, requests.Count == 1 ? "added a slide" : $"added {requests.Count} slides", cancellationToken);

        if (PresentationRequestBuilder.RecordLinks(call.Services, deck, requests.SelectMany(request => request.Elements), result) > 0)
        {
            await call.Session.Workspace.SaveStateAsync();
        }

        return Changed(deck, result, notes);
    }
}
