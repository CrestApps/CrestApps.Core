using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CrestApps.Core.AI.Documents.Endpoints;

/// <summary>
/// Serves a figure read out of an uploaded document, so an answer that mentions a chart can show it.
/// </summary>
/// <remarks>
/// The figure bytes are stored under the owning document when the upload is processed and listed on the
/// document as a <see cref="DocumentFigureList"/>. Whoever may download the document may see its figures,
/// which is the same rule the document download applies.
/// </remarks>
public static class DownloadAIDocumentFigure
{
    /// <summary>
    /// The default route name.
    /// </summary>
    public const string DefaultRouteName = "DownloadAIDocumentFigure";

    /// <summary>
    /// Adds the endpoint that serves the figures read out of an uploaded document.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="routeName">The route name.</param>
    public static IEndpointRouteBuilder AddDownloadAIDocumentFigureEndpoint(this IEndpointRouteBuilder builder, string routeName = DefaultRouteName)
    {
        var endpoint = builder.MapGet("ai/documents/{documentId}/figures/{figureId}", HandleAsync);

        if (!string.IsNullOrEmpty(routeName))
        {
            _ = endpoint.WithName(routeName);
        }

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        string documentId,
        string figureId,
        HttpContext httpContext,
        [FromServices] IAIDocumentStore documentStore,
        [FromServices] IDocumentFileStore fileStore,
        [FromServices] IAuthorizationService authorizationService,
        [FromServices] ICatalogManager<ChatInteraction> interactionManager,
        [FromServices] IAIChatSessionManager sessionManager,
        [FromServices] IAIProfileManager profileManager)
    {
        if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(figureId))
        {
            return Results.BadRequest();
        }

        var document = await documentStore.FindByIdAsync(documentId);

        if (document is null)
        {
            return Results.NotFound();
        }

        var authorizationResult = await AIDocumentDownloadAuthorization.AuthorizeAsync(
            httpContext,
            authorizationService,
            interactionManager,
            sessionManager,
            profileManager,
            document);

        if (authorizationResult is not null)
        {
            return authorizationResult;
        }

        // The figure is looked up on the document rather than trusted from the address, so a caller can only
        // ever read a picture the document actually lists.
        var figure = document.FindFigure(figureId);

        if (figure is null || string.IsNullOrWhiteSpace(figure.StoragePath))
        {
            return Results.NotFound();
        }

        var stream = await fileStore.GetFileAsync(figure.StoragePath);

        if (stream is null)
        {
            return Results.NotFound();
        }

        return Results.File(
            stream,
            string.IsNullOrWhiteSpace(figure.MediaType) ? "application/octet-stream" : figure.MediaType,
            Path.GetFileName(figure.StoragePath),
            enableRangeProcessing: true);
    }
}
