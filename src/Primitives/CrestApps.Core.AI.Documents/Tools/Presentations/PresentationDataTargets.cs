using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Finds the table or chart a call means, and turns a query over the conversation's tabular data into the
/// edit that refills it.
/// </summary>
internal static class PresentationDataTargets
{
    /// <summary>
    /// Finds a table or chart on a slide by id or name, or the only one there when none is named.
    /// </summary>
    /// <param name="slide">The slide.</param>
    /// <param name="reference">The element's #id or name, or <see langword="null"/>.</param>
    /// <param name="kind">The kind wanted, or <see langword="null"/> for either.</param>
    /// <returns>The element.</returns>
    /// <exception cref="PresentationArgumentException">No single matching element exists.</exception>
    public static PresentationElement Find(PresentationSlide slide, string reference, PresentationElementKind? kind)
    {
        var candidates = slide.AllElements()
            .Where(element => kind is null
                ? element.Kind is PresentationElementKind.Table or PresentationElementKind.Chart
                : element.Kind == kind)
            .ToList();

        var noun = kind switch
        {
            PresentationElementKind.Table => "table",
            PresentationElementKind.Chart => "chart",
            _ => "table or chart",
        };

        if (!string.IsNullOrWhiteSpace(reference))
        {
            var trimmed = reference.Trim().TrimStart('#');
            var found = uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? candidates.FirstOrDefault(element => element.Id == id)
                : candidates.FirstOrDefault(element => string.Equals(element.Name, trimmed, StringComparison.OrdinalIgnoreCase));

            return found ?? throw new PresentationArgumentException(candidates.Count == 0
                ? $"Slide {slide.Number} has no {noun}."
                : $"Slide {slide.Number} has no {noun} \"{reference}\". It has: {Describe(candidates)}.");
        }

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new PresentationArgumentException($"Slide {slide.Number} has no {noun}."),
            _ => throw new PresentationArgumentException($"Slide {slide.Number} has {candidates.Count} of them, so name the 'element': {Describe(candidates)}."),
        };
    }

    /// <summary>
    /// Runs a query and builds the edit that puts its rows into a table or chart.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="slide">The slide number.</param>
    /// <param name="element">The table or chart.</param>
    /// <param name="sql">The query.</param>
    /// <param name="categoryColumn">For a chart, the column its categories come from.</param>
    /// <param name="valueColumns">For a chart, the columns it plots.</param>
    /// <param name="maxRows">The most rows used.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The edit and a note of what the query returned.</returns>
    public static async Task<(PresentationEdit Edit, string Note)> BuildAsync(
        IServiceProvider services,
        int slide,
        PresentationElement element,
        string sql,
        string categoryColumn,
        IList<string> valueColumns,
        int maxRows,
        CancellationToken cancellationToken)
    {
        var result = await PresentationContentResolver.QueryAsync(services, sql, maxRows, cancellationToken);
        var rows = result.Rows.Count.ToString(CultureInfo.InvariantCulture);
        var reference = element.Id.ToString(CultureInfo.InvariantCulture);

        if (element.Kind == PresentationElementKind.Table)
        {
            var table = PresentationContentResolver.BuildTable(result, null);

            return (new UpdateTableEdit { Slide = slide, Element = reference, ReplaceRows = table.Rows }, $"Filled table #{reference} on slide {slide} from the query ({rows} row(s){(result.Truncated ? ", truncated" : string.Empty)}).");
        }

        var chart = new PresentationChartSpec();
        PresentationContentResolver.FillChart(chart, result, categoryColumn, valueColumns);

        return (new UpdateChartEdit { Slide = slide, Element = reference, Chart = chart }, $"Filled chart #{reference} on slide {slide} from the query ({rows} categor{(result.Rows.Count == 1 ? "y" : "ies")}, {chart.Series.Count.ToString(CultureInfo.InvariantCulture)} series).");
    }

    private static string Describe(List<PresentationElement> elements)
    {
        return string.Join(", ", elements.Select(element => $"#{element.Id.ToString(CultureInfo.InvariantCulture)} \"{element.Name}\""));
    }
}
