using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Reruns the queries behind linked tables and charts and updates them.
/// </summary>
internal sealed class RefreshSlideDataTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.RefreshSlideData;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshSlideDataTool"/> class.
    /// </summary>
    public RefreshSlideDataTool()
        : base(
            TheName,
            "Reruns the queries behind the presentation's linked tables and charts (see link_slide_data) and updates them with the current spreadsheet data. Use it after the user uploads new data or changes it.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slides": { "type": ["integer", "string", "array"], "description": "Only refresh these slides. Omit for all." }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Refreshes the data.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();

        if (deck.DataLinks.Count == 0)
        {
            return $"No table or chart in \"{deck.Name}\" is linked to data. Link one with link_slide_data, or pass link_data with tabular_sql when inserting it.";
        }

        var model = await call.ReadAsync(deck, cancellationToken);
        var slides = call.Slides(model, false);
        var edits = new List<PresentationEdit>();
        var notes = new List<string>();
        var refreshed = new List<PresentationDataLink>();
        var stale = new List<PresentationDataLink>();

        foreach (var link in deck.DataLinks)
        {
            var slide = model.Slides.FirstOrDefault(candidate => candidate.SlideId == link.SlideId);
            var element = slide?.AllElements().FirstOrDefault(candidate => candidate.Id == link.ElementId && (candidate.Table is not null || candidate.Chart is not null));

            if (element is null)
            {
                stale.Add(link);
                continue;
            }

            if (slides.Count > 0 && !slides.Contains(slide.Number))
            {
                continue;
            }

            var (edit, note) = await PresentationDataTargets.BuildAsync(call.Services, slide.Number, element, link.Sql, link.CategoryColumn, link.ValueColumns, link.MaxRows, cancellationToken);

            edits.Add(edit);
            notes.Add(note);
            refreshed.Add(link);
        }

        foreach (var link in stale)
        {
            deck.DataLinks.Remove(link);
            notes.Add($"Dropped the link of a {link.Kind} that no longer exists.");
        }

        if (edits.Count == 0)
        {
            await call.Session.Workspace.SaveStateAsync();

            return "Nothing was refreshed: no linked table or chart is on the chosen slides." + (stale.Count > 0 ? " " + string.Join(" ", notes) : string.Empty);
        }

        var result = await call.Session.ApplyAsync(deck, edits, "refreshed linked data", cancellationToken);
        var now = PresentationDataLinks.Now(call.Services);

        foreach (var link in refreshed)
        {
            link.RefreshedUtc = now;
        }

        await call.Session.Workspace.SaveStateAsync();

        return Changed(deck, result, notes);
    }
}
