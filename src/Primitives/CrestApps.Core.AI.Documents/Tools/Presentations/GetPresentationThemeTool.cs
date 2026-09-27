using System.Text;
using CrestApps.Core.AI.Documents.Presentations;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Describes the deck's theme, slide masters and layouts.
/// </summary>
internal sealed class GetPresentationThemeTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.GetPresentationTheme;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetPresentationThemeTool"/> class.
    /// </summary>
    public GetPresentationThemeTool()
        : base(
            TheName,
            "Describes the presentation's design: theme colours (dk1, lt1, dk2, lt2, accent1-accent6) and fonts, slide size, each slide master with its background and title and body text styles, and each layout with its type, how many slides use it, and where its placeholders sit. Use it before choosing layouts for new slides, restyling, or applying branding, and to list the theme presets create_presentation offers.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "include_placeholders": { "type": "boolean", "description": "List each layout's placeholders. Defaults to true." }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Gets a value indicating whether the tool needs a deck.
    /// </summary>
    protected override bool RequiresDeck => false;

    /// <summary>
    /// Describes the theme.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();

        if (call.Session.Workspace.State.Decks.Count > 0)
        {
            var deck = call.Deck();
            var model = await call.ReadAsync(deck, cancellationToken);

            builder.AppendLine(PresentationDescriber.Heading(deck, model));
            builder.AppendLine(PresentationDescriber.Theme(model, call.Arguments.Bool("include_placeholders") != false));
            builder.AppendLine();
        }

        builder.AppendLine("Theme presets for create_presentation and update_presentation_theme:");

        foreach (var preset in PresentationThemePresets.All)
        {
            builder.Append("- ").Append(preset.Name).Append(": ").AppendLine(preset.Description);
        }

        return builder.ToString().TrimEnd();
    }
}
