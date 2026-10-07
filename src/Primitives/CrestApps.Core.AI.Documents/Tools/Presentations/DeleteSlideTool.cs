using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Deletes slides.
/// </summary>
internal sealed class DeleteSlideTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.DeleteSlide;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteSlideTool"/> class.
    /// </summary>
    public DeleteSlideTool()
        : base(
            TheName,
            "Deletes slides from the presentation. The remaining slides are renumbered, and links to a deleted slide are reported. undo_presentation_change brings them back.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slides": { "type": ["integer", "string", "array"], "description": "The slides to delete, such as 4, [2, 5] or \"6-8\"." }
              },
              "required": ["slides"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Deletes the slides.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var slides = call.Slides(model, true);
        var removedIds = model.Slides.Where(slide => slides.Contains(slide.Number)).Select(slide => slide.SlideId).ToHashSet();
        var result = await call.Session.ApplyAsync(deck, [new DeleteSlidesEdit { Slides = slides }], "deleted slide " + PresentationDescriber.Range(slides), cancellationToken);

        if (deck.DataLinks.RemoveAll(link => removedIds.Contains(link.SlideId)) > 0)
        {
            await call.Session.Workspace.SaveStateAsync();
        }

        return Changed(deck, result);
    }
}
