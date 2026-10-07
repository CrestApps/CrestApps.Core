using CrestApps.Core.AI.Documents.Presentations;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Records the tables and charts that stay tied to a query over the conversation's tabular data.
/// </summary>
internal static class PresentationDataLinks
{
    /// <summary>
    /// Ties an element to a query, replacing any link it had.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="deck">The deck.</param>
    /// <param name="slideId">The slide's identifier.</param>
    /// <param name="elementId">The element's identifier.</param>
    /// <param name="kind"><c>table</c> or <c>chart</c>.</param>
    /// <param name="sql">The query.</param>
    /// <param name="categoryColumn">For a chart, the column its categories come from.</param>
    /// <param name="valueColumns">For a chart, the columns it plots.</param>
    /// <param name="maxRows">The most rows used.</param>
    public static void Set(IServiceProvider services, PresentationDeckState deck, uint slideId, uint elementId, string kind, string sql, string categoryColumn, IList<string> valueColumns, int maxRows)
    {
        deck.DataLinks.RemoveAll(link => link.SlideId == slideId && link.ElementId == elementId);
        deck.DataLinks.Add(new PresentationDataLink
        {
            SlideId = slideId,
            ElementId = elementId,
            Kind = kind,
            Sql = sql,
            CategoryColumn = categoryColumn,
            ValueColumns = valueColumns ?? [],
            MaxRows = maxRows,
            RefreshedUtc = Now(services),
        });
    }

    /// <summary>
    /// Gets the current time.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <returns>The time.</returns>
    public static DateTime Now(IServiceProvider services)
    {
        return (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }
}
