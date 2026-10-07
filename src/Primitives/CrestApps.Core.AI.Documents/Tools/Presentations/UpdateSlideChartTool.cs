using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Changes a chart's type, data, titles and look, or refills it from a query.
/// </summary>
internal sealed class UpdateSlideChartTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.UpdateSlideChart;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateSlideChartTool"/> class.
    /// </summary>
    public UpdateSlideChartTool()
        : base(
            TheName,
            "Changes a chart on a slide: switch its type (column, bar, line, pie, doughnut, area, scatter, radar, stacked or 100% variants), replace its categories and series, set its title and axis titles, or restyle it (series colours, legend, data labels, gridlines, number format, fonts). tabular_sql refills it from the conversation's spreadsheet data. The chart's embedded data is updated too, so it stays editable in PowerPoint. The chart is found by 'element', or is the only chart on the slide.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slide": { "type": "integer" },
                "element": { "type": ["integer", "string"], "description": "The chart's #id or name. Optional when the slide has one chart." },
                "chart": CHART,
                "tabular_sql": { "type": "string", "description": "Refill the chart from this read-only query: the first column gives the categories, numeric columns the series." },
                "category_column": { "type": "string" },
                "value_columns": { "type": "array", "items": { "type": "string" } },
                "max_rows": { "type": "integer" },
                "link_data": { "type": "boolean", "description": "Keep the chart tied to tabular_sql so refresh_slide_data can update it." }
              },
              "required": ["slide"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Updates the chart.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var slide = call.Slide(model);
        var arguments = call.Arguments;
        var element = PresentationDataTargets.Find(model.Slides[slide - 1], arguments.String("element", "chart_id", "element_id"), PresentationElementKind.Chart);

        // The chart's properties may be nested under "chart" or given at the top level.
        var chart = PresentationArguments.ReadChart(arguments.Object("chart") ?? arguments.Root);
        var sql = arguments.String("tabular_sql", "sql", "query");
        var categoryColumn = arguments.String("category_column");
        var valueColumns = arguments.Strings("value_columns", "series_columns");
        var maxRows = Math.Clamp(arguments.Int("max_rows", "limit") ?? 25, 1, 60);
        var notes = new List<string>();

        if (!string.IsNullOrWhiteSpace(sql))
        {
            var result = await PresentationContentResolver.QueryAsync(call.Services, sql, maxRows, cancellationToken);
            PresentationContentResolver.FillChart(chart, result, categoryColumn, valueColumns);
            notes.Add($"Filled the chart from the query ({result.Rows.Count.ToString(CultureInfo.InvariantCulture)} categories, {chart.Series.Count.ToString(CultureInfo.InvariantCulture)} series).");
        }

        if (chart.Kind is null && chart.Title is null && chart.Categories is null && chart.Series is null &&
            chart.CategoryAxisTitle is null && chart.ValueAxisTitle is null && chart.Style is null)
        {
            throw new PresentationArgumentException("Say what to change in 'chart': kind, title, categories, series, axis titles or style; or give tabular_sql.");
        }

        var edit = new UpdateChartEdit
        {
            Slide = slide,
            Element = element.Id.ToString(CultureInfo.InvariantCulture),
            Chart = chart,
        };

        var applied = await call.Session.ApplyAsync(deck, [edit], $"changed the chart on slide {slide}", cancellationToken);

        if (!string.IsNullOrWhiteSpace(sql) && arguments.Bool("link_data", "keep_linked", "linked") == true)
        {
            PresentationDataLinks.Set(call.Services, deck, model.Slides[slide - 1].SlideId, element.Id, "chart", sql, categoryColumn, valueColumns, maxRows);
            await call.Session.Workspace.SaveStateAsync();
            notes.Add("The chart stays linked: refresh_slide_data updates it from its query.");
        }

        return Changed(deck, applied, notes);
    }
}
