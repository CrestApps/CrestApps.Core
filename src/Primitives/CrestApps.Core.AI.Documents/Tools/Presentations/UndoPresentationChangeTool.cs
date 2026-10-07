using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Presentations;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Steps a deck back through its recent versions, or lists them.
/// </summary>
internal sealed class UndoPresentationChangeTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.UndoPresentationChange;

    /// <summary>
    /// Initializes a new instance of the <see cref="UndoPresentationChangeTool"/> class.
    /// </summary>
    public UndoPresentationChangeTool()
        : base(
            TheName,
            "Undoes the most recent changes to the presentation, restoring it exactly as it was before them. 'steps' undoes several at once; list=true shows the recent changes that can be undone. Only the last few versions are kept.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "steps": { "type": "integer", "description": "How many changes to undo. Defaults to 1." },
                "list": { "type": "boolean" }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Undoes the changes.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();

        if (call.Arguments.Bool("list", "history") == true)
        {
            if (deck.History.Count == 0)
            {
                return $"\"{deck.Name}\" has no earlier versions to go back to.";
            }

            var builder = new StringBuilder();
            builder.Append('"').Append(deck.Name).AppendLine("\" changes that can be undone, most recent first:");

            var step = 1;

            foreach (var revision in deck.History.AsEnumerable().Reverse())
            {
                builder.Append(step.ToString(CultureInfo.InvariantCulture)).Append(". ").AppendLine(revision.Description ?? "a change");
                step++;
            }

            return builder.ToString().TrimEnd();
        }

        var steps = Math.Max(1, call.Arguments.Int("steps", "count", "times") ?? 1);

        if (deck.History.Count == 0)
        {
            return $"There is nothing to undo in \"{deck.Name}\".";
        }

        var undone = await call.Session.Workspace.UndoAsync(deck, steps);
        var model = await call.ReadAsync(deck, cancellationToken);
        var response = new StringBuilder();

        response.Append("Undid ").Append(PresentationDescriber.Count(undone.Count, "change")).Append(": ").AppendJoin("; ", undone).AppendLine(".");
        response.Append('"').Append(deck.Name).Append("\" is back to ").Append(PresentationDescriber.Count(model.Slides.Count, "slide")).AppendLine(".");

        if (undone.Count < steps)
        {
            response.AppendLine("No earlier versions are kept, so it could not go back further.");
        }

        response.Append("Preview it with preview_presentation to show the user.");

        return response.ToString();
    }
}
