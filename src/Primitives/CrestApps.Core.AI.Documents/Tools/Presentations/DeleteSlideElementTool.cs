using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Deletes elements from a slide.
/// </summary>
internal sealed class DeleteSlideElementTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.DeleteSlideElement;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteSlideElementTool"/> class.
    /// </summary>
    public DeleteSlideElementTool()
        : base(
            TheName,
            "Deletes elements from a slide, found by #id, name or role. Deleting a layout placeholder such as the title leaves the slide without it.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slide": { "type": "integer" },
                "elements": { "type": "array", "items": { "type": ["integer", "string"] }, "description": "The elements' #ids, names or roles." }
              },
              "required": ["slide", "elements"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Deletes the elements.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var slide = call.Slide(model);
        var elements = PresentationElementReferences.Read(call.Arguments);
        var slideId = model.Slides[slide - 1].SlideId;
        var removedIds = model.Slides[slide - 1].AllElements()
            .Where(element => elements.Any(reference => reference.TrimStart('#') == element.Id.ToString(CultureInfo.InvariantCulture) || string.Equals(reference, element.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(element => element.Id)
            .ToHashSet();

        var result = await call.Session.ApplyAsync(deck, [new DeleteElementsEdit { Slide = slide, Elements = elements }], $"deleted elements from slide {slide}", cancellationToken);

        if (deck.DataLinks.RemoveAll(link => link.SlideId == slideId && removedIds.Contains(link.ElementId)) > 0)
        {
            await call.Session.Workspace.SaveStateAsync();
        }

        return Changed(deck, result);
    }
}
