using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CrestApps.Core.AI.Documents.Endpoints;

/// <summary>
/// Serves the stored picture of an ingested figure or chart, which is what a citation on a figure answer
/// links to.
/// </summary>
public static class DownloadKnowledgeFigure
{
    /// <summary>
    /// The default route name.
    /// </summary>
    public const string DefaultRouteName = "DownloadKnowledgeFigure";

    /// <summary>
    /// Adds the figure download endpoint used by citation links on ingested data sources.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="routeName">The route name.</param>
    public static IEndpointRouteBuilder AddDownloadKnowledgeFigureEndpoint(this IEndpointRouteBuilder builder, string routeName = DefaultRouteName)
    {
        var endpoint = builder.MapGet("ai/knowledge/{dataSourceId}/figures/{canonicalId}", HandleAsync);

        if (!string.IsNullOrEmpty(routeName))
        {
            _ = endpoint.WithName(routeName);
        }

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        string dataSourceId,
        string canonicalId,
        HttpContext httpContext,
        [FromServices] IAIDataSourceStore dataSourceStore,
        [FromServices] IKnowledgeObjectStore knowledgeStore,
        [FromServices] IDocumentFileStore fileStore,
        [FromServices] IAuthorizationService authorizationService)
    {
        if (string.IsNullOrWhiteSpace(dataSourceId) || string.IsNullOrWhiteSpace(canonicalId))
        {
            return Results.BadRequest();
        }

        var dataSource = await dataSourceStore.FindByIdAsync(dataSourceId);

        if (dataSource is null || !string.Equals(dataSource.Source, AIDataSourceSourceTypes.File, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        var authorization = await authorizationService.AuthorizeAsync(
            httpContext.User,
            dataSource,
            [AIKnowledgeOperations.ViewFigures]);

        if (!authorization.Succeeded)
        {
            return httpContext.User.Identity?.IsAuthenticated == true
                ? Results.Forbid()
                : Results.Challenge();
        }

        var entry = await knowledgeStore.FindByCanonicalIdAsync(dataSourceId, canonicalId);

        if (entry is null ||
            entry.ObjectType is not (KnowledgeContentTypes.Figure or KnowledgeContentTypes.Chart) ||
            string.IsNullOrWhiteSpace(entry.StoragePath))
        {
            return Results.NotFound();
        }

        var stream = await fileStore.GetFileAsync(entry.StoragePath);

        if (stream is null)
        {
            return Results.NotFound();
        }

        return Results.File(
            stream,
            string.IsNullOrWhiteSpace(entry.MediaType) ? "application/octet-stream" : entry.MediaType,
            Path.GetFileName(entry.StoragePath),
            enableRangeProcessing: true);
    }
}
