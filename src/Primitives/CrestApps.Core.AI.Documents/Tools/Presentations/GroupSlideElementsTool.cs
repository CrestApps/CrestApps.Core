using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Groups elements so they move together, or ungroups a group.
/// </summary>
internal sealed class GroupSlideElementsTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.GroupSlideElements;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupSlideElementsTool"/> class.
    /// </summary>
    public GroupSlideElementsTool()
        : base(
            TheName,
            "Groups two or more elements on a slide into one that moves and resizes together, or with ungroup=true splits a group back into its elements.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slide": { "type": "integer" },
                "elements": { "type": "array", "items": { "type": ["integer", "string"] }, "description": "The elements to group, or the one group to ungroup." },
                "ungroup": { "type": "boolean" },
                "name": { "type": "string", "description": "A name for the new group." }
              },
              "required": ["slide", "elements"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Groups or ungroups the elements.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var slide = call.Slide(model);
        var ungroup = call.Arguments.Bool("ungroup") == true;
        var edit = new GroupElementsEdit
        {
            Slide = slide,
            Elements = PresentationElementReferences.Read(call.Arguments),
            Ungroup = ungroup,
            Name = call.Arguments.String("name", "group_name"),
        };

        if (!ungroup && edit.Elements.Count < 2)
        {
            throw new PresentationArgumentException("Name at least two elements to group.");
        }

        var result = await call.Session.ApplyAsync(deck, [edit], ungroup ? $"ungrouped on slide {slide}" : $"grouped elements on slide {slide}", cancellationToken);

        return Changed(deck, result);
    }
}
