using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Ties a table or chart to a query over the conversation's tabular data, lists the ties, or removes one.
/// </summary>
internal sealed class LinkSlideDataTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.LinkSlideData;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkSlideDataTool"/> class.
    /// </summary>
    public LinkSlideDataTool()
        : base(
            TheName,
            "Ties an existing table or chart to a read-only query over the conversation's spreadsheet data and fills it now, so refresh_slide_data can update it whenever the data changes. With unlink=true removes the tie; with list=true shows every linked table and chart.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slide": { "type": "integer" },
                "element": { "type": ["integer", "string"], "description": "The table's or chart's #id or name. Optional when the slide has one." },
                "tabular_sql": { "type": "string" },
                "category_column": { "type": "string" },
                "value_columns": { "type": "array", "items": { "type": "string" } },
                "max_rows": { "type": "integer" },
                "unlink": { "type": "boolean" },
                "list": { "type": "boolean" }
              },
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Links, unlinks or lists.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var arguments = call.Arguments;

        if (arguments.Bool("list") == true || (arguments.Node("slide") is null && arguments.Node("tabular_sql", "sql") is null))
        {
            return List(deck, model);
        }

        var slide = call.Slide(model);
        var element = PresentationDataTargets.Find(model.Slides[slide - 1], arguments.String("element", "element_id"), null);
        var slideId = model.Slides[slide - 1].SlideId;

        if (arguments.Bool("unlink", "remove") == true)
        {
            var removed = deck.DataLinks.RemoveAll(link => link.SlideId == slideId && link.ElementId == element.Id);
            await call.Session.Workspace.SaveStateAsync();

            return removed > 0
                ? $"#{element.Id} on slide {slide} is no longer linked; its current data stays."
                : $"#{element.Id} on slide {slide} was not linked.";
        }

        var sql = arguments.String("tabular_sql", "sql", "query")
            ?? throw new PresentationArgumentException("Give the read-only 'tabular_sql' query the element takes its data from.");
        var categoryColumn = arguments.String("category_column");
        var valueColumns = arguments.Strings("value_columns", "series_columns");
        var maxRows = Math.Clamp(arguments.Int("max_rows", "limit") ?? 25, 1, 60);
        var (edit, note) = await PresentationDataTargets.BuildAsync(call.Services, slide, element, sql, categoryColumn, valueColumns, maxRows, cancellationToken);
        var result = await call.Session.ApplyAsync(deck, [edit], $"linked #{element.Id} on slide {slide} to data", cancellationToken);

        PresentationDataLinks.Set(call.Services, deck, slideId, element.Id, element.Chart is null ? "table" : "chart", sql, categoryColumn, valueColumns, maxRows);
        await call.Session.Workspace.SaveStateAsync();

        return Changed(deck, result, [note, "It stays linked: refresh_slide_data updates it from its query."]);
    }

    private static string List(PresentationDeckState deck, PresentationModel model)
    {
        if (deck.DataLinks.Count == 0)
        {
            return $"No table or chart in \"{deck.Name}\" is linked to data.";
        }

        var builder = new StringBuilder();

        builder.Append('"').Append(deck.Name).AppendLine("\" linked data:");

        foreach (var link in deck.DataLinks)
        {
            var slide = model.Slides.FirstOrDefault(candidate => candidate.SlideId == link.SlideId);

            builder.Append("- ").Append(link.Kind).Append(' ')
                .Append(slide is null ? "(slide deleted)" : $"#{link.ElementId.ToString(CultureInfo.InvariantCulture)} on slide {slide.Number.ToString(CultureInfo.InvariantCulture)}")
                .Append(": ").Append(link.Sql)
                .Append(" (refreshed ").Append(link.RefreshedUtc.ToString("u", CultureInfo.InvariantCulture)).AppendLine(")");
        }

        return builder.ToString().TrimEnd();
    }
}
