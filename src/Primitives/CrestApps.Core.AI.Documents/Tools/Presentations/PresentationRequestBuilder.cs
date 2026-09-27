using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Turns slide and element requests into the edits the engine applies, after fetching what they refer to,
/// and records the data links they ask for once the edits are made.
/// </summary>
internal static class PresentationRequestBuilder
{
    /// <summary>
    /// Builds the edits that add slides.
    /// </summary>
    /// <param name="session">The tool session.</param>
    /// <param name="requests">The slides to add.</param>
    /// <param name="notes">Where to record what was fetched.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The edits.</returns>
    public static async Task<List<PresentationEdit>> SlidesAsync(PresentationToolSession session, IList<PresentationSlideRequest> requests, List<string> notes, CancellationToken cancellationToken)
    {
        var edits = new List<PresentationEdit>();

        foreach (var request in requests)
        {
            PrepareLinks(request.Elements);
            await PresentationContentResolver.ResolveAsync(session, request.Elements, notes, cancellationToken);

            request.Edit.Elements = request.Elements.Select(element => element.Spec).ToList();

            if (!string.IsNullOrWhiteSpace(request.BackgroundImageDocument))
            {
                request.Edit.Background ??= new PresentationBackgroundSpec();
                request.Edit.Background.Image = await session.LoadImageAsync(request.BackgroundImageDocument);
            }

            edits.Add(request.Edit);
        }

        return edits;
    }

    /// <summary>
    /// Gives every element that asks to stay linked to its query a name it can be found by after the edit.
    /// </summary>
    /// <param name="requests">The element requests.</param>
    public static void PrepareLinks(IEnumerable<PresentationElementRequest> requests)
    {
        foreach (var request in requests)
        {
            if (request.Link && !string.IsNullOrWhiteSpace(request.TabularSql) && string.IsNullOrWhiteSpace(request.Spec.Name))
            {
                var hash = (uint)StringComparer.Ordinal.GetHashCode(request.TabularSql + Guid.NewGuid().ToString("N"));
                request.Spec.Name = (request.Spec.Kind == PresentationElementSpecKind.Chart ? "Linked chart " : "Linked table ") + hash.ToString("x6", CultureInfo.InvariantCulture)[..6];
            }
        }
    }

    /// <summary>
    /// Records the data links the requests asked for, now that the elements exist.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="deck">The deck.</param>
    /// <param name="requests">The element requests.</param>
    /// <param name="result">What the edit created.</param>
    /// <returns>The number of links recorded.</returns>
    public static int RecordLinks(IServiceProvider services, PresentationDeckState deck, IEnumerable<PresentationElementRequest> requests, PresentationEditResult result)
    {
        var recorded = 0;

        foreach (var request in requests.Where(request => request.Link && !string.IsNullOrWhiteSpace(request.TabularSql)))
        {
            var created = result.CreatedElements.LastOrDefault(element => element.Name == request.Spec.Name);

            if (created is null)
            {
                continue;
            }

            PresentationDataLinks.Set(
                services,
                deck,
                created.SlideId,
                created.ElementId,
                request.Spec.Kind == PresentationElementSpecKind.Chart ? "chart" : "table",
                request.TabularSql,
                request.CategoryColumn,
                request.ValueColumns,
                request.MaxRows);

            recorded++;
        }

        return recorded;
    }
}
