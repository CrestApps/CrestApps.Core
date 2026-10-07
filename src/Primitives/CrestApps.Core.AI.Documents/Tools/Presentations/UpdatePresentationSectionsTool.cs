using System.Text.Json.Nodes;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Organises a deck into named sections.
/// </summary>
internal sealed class UpdatePresentationSectionsTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.UpdatePresentationSections;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatePresentationSectionsTool"/> class.
    /// </summary>
    public UpdatePresentationSectionsTool()
        : base(
            TheName,
            "Organises the presentation into named sections, as PowerPoint's section feature shows them. Give every section with the slide it starts at; slides before the first section go into a 'Default Section'. An empty list removes the sections. by_section_headers=true creates a section at every section-header slide, named after it.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "sections": { "type": "array", "items": { "type": "object", "properties": { "name": { "type": "string" }, "first_slide": { "type": "integer" } }, "required": ["name", "first_slide"] } },
                "by_section_headers": { "type": "boolean" }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Updates the sections.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var edit = new UpdateSectionsEdit();

        if (call.Arguments.Bool("by_section_headers", "from_headers") == true)
        {
            foreach (var slide in model.Slides.Where(slide => slide.LayoutType is "secHead" || slide.Number == 1))
            {
                edit.Sections.Add(new PresentationSectionSpec { Name = slide.Number == 1 && slide.LayoutType is not "secHead" ? "Introduction" : slide.Title ?? "Section", FirstSlide = slide.Number });
            }
        }
        else if (call.Arguments.Node("sections") is null)
        {
            throw new PresentationArgumentException("Give the 'sections', each with a name and first_slide, or by_section_headers=true.");
        }
        else
        {
            foreach (var item in call.Arguments.Array("sections").OfType<JsonObject>())
            {
                var name = PresentationArguments.ReadString(PresentationArguments.Find(item, "name", "title")) ?? throw new PresentationArgumentException("Each section needs a name.");
                var first = PresentationArguments.ReadInt(PresentationArguments.Find(item, "first_slide", "start", "slide", "from")) ?? throw new PresentationArgumentException($"Section \"{name}\" needs its first_slide.");

                if (first < 1 || first > model.Slides.Count)
                {
                    throw new PresentationArgumentException($"Section \"{name}\" starts at slide {first}, but the deck has {model.Slides.Count} slide(s).");
                }

                edit.Sections.Add(new PresentationSectionSpec { Name = name, FirstSlide = first });
            }
        }

        var result = await call.Session.ApplyAsync(deck, [edit], edit.Sections.Count == 0 ? "removed the sections" : "organised the sections", cancellationToken);

        return Changed(deck, result);
    }
}
