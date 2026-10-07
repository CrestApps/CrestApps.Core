using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Adds, or rewrites, an agenda slide whose entries link to the slides they name.
/// </summary>
internal sealed class AddPresentationTocTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.AddPresentationToc;

    private const int MaxEntries = 14;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddPresentationTocTool"/> class.
    /// </summary>
    public AddPresentationTocTool()
        : base(
            TheName,
            "Adds an agenda (table of contents) slide listing the deck's sections, or its section-header slides, or else its slide titles, each entry a link that jumps to its slide during the show. Pass 'slide' to rewrite an existing agenda slide instead, for example after slides were added.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "title": { "type": "string", "description": "Defaults to 'Agenda'." },
                "position": { "type": "integer", "description": "Where the new slide goes. Defaults to 2, after the title slide." },
                "slide": { "type": "integer", "description": "Rewrite this existing agenda slide instead of adding one." },
                "source": { "type": "string", "enum": ["auto", "sections", "section_headers", "titles"], "description": "What to list. Defaults to auto: sections, else section headers, else titles." }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Adds the agenda.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var arguments = call.Arguments;
        var existing = arguments.Int("slide", "replace_slide", "update_slide");

        if (existing is < 1 || existing > model.Slides.Count)
        {
            throw new PresentationArgumentException($"There is no slide {existing}: the deck has {model.Slides.Count} slide(s).");
        }

        var skip = existing is { } number ? model.Slides[number - 1].SlideId : 0;
        var entries = Entries(model, arguments.String("source", "from")?.Trim().ToLowerInvariant() ?? "auto", skip);

        if (entries.Count == 0)
        {
            throw new PresentationArgumentException("The deck has no titled slides to list yet.");
        }

        var notes = new List<string>();

        if (entries.Count > MaxEntries)
        {
            notes.Add($"Listed the first {MaxEntries} of {entries.Count} entries; organise the deck into sections with update_presentation_sections for a shorter agenda.");
            entries = entries.Take(MaxEntries).ToList();
        }

        var body = entries
            .Select(entry => new PresentationParagraphSpec
            {
                Runs = [new PresentationRunSpec { Text = entry.Text, Link = new PresentationLinkSpec { SlideId = entry.SlideId } }],
            })
            .ToList();

        var title = arguments.String("title", "heading") ?? "Agenda";
        PresentationEdit edit = existing is { } slide
            ? new UpdateSlideEdit { Slide = slide, Title = title, Body = body }
            : new AddSlideEdit { Position = Math.Clamp(arguments.Int("position", "at") ?? 2, 1, model.Slides.Count + 1), Layout = "title_and_content", Title = title, Body = body };

        var result = await call.Session.ApplyAsync(deck, [edit], existing is null ? "added an agenda slide" : "rewrote the agenda slide", cancellationToken);

        notes.Add($"Each of the {entries.Count} entries links to its slide during the show.");

        return Changed(deck, result, notes);
    }

    private static List<(string Text, uint SlideId)> Entries(PresentationModel model, string source, uint skip)
    {
        var slides = model.Slides.Where(slide => slide.SlideId != skip).ToList();

        if (source is "auto" or "sections" && model.Sections.Count > 1)
        {
            var sections = new List<(string Text, uint SlideId)>();

            foreach (var section in model.Sections)
            {
                var first = slides.FirstOrDefault(slide => slide.SectionName == section.Name);

                if (first is not null && !string.IsNullOrWhiteSpace(section.Name) && section.Name != "Default Section")
                {
                    sections.Add((section.Name, first.SlideId));
                }
            }

            if (sections.Count > 0 || source == "sections")
            {
                return sections;
            }
        }

        var headers = slides.Where(slide => slide.LayoutType == "secHead" && !string.IsNullOrWhiteSpace(slide.Title)).ToList();

        if (source is "section_headers" || (source == "auto" && headers.Count >= 2))
        {
            return headers.Select(slide => (slide.Title.Trim(), slide.SlideId)).ToList();
        }

        return slides
            .Where(slide => slide.LayoutType is not "title" && !slide.Hidden && !string.IsNullOrWhiteSpace(slide.Title))
            .Select(slide => (slide.Title.Trim(), slide.SlideId))
            .ToList();
    }
}
