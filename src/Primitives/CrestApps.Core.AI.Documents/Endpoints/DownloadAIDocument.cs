using CrestApps.Core.AI.Chat;
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
/// Provides functionality for download AI Document.
/// </summary>
public static class DownloadAIDocument
{
    public const string DefaultRouteName = "DownloadAIDocument";

    /// <summary>
    /// Adds the shared AI document download endpoint used by citation links.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="routeName">The route name.</param>
    public static IEndpointRouteBuilder AddDownloadAIDocumentEndpoint(this IEndpointRouteBuilder builder, string routeName = DefaultRouteName)
    {
        var endpoint = builder.MapGet("ai/documents/{documentId}/download", HandleAsync);

        if (!string.IsNullOrEmpty(routeName))
        {
            _ = endpoint.WithName(routeName);
        }

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        string documentId,
        HttpContext httpContext,
        [FromServices] IAIDocumentStore documentStore,
        [FromServices] IDocumentFileStore fileStore,
        [FromServices] IAuthorizationService authorizationService,
        [FromServices] ICatalogManager<ChatInteraction> interactionManager,
        [FromServices] IAIChatSessionManager sessionManager,
        [FromServices] IAIProfileManager profileManager)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return Results.BadRequest();
        }

        var document = await documentStore.FindByIdAsync(documentId);

        if (document is null || string.IsNullOrWhiteSpace(document.StoredFilePath))
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

        var stream = await fileStore.GetFileAsync(document.StoredFilePath);

        if (stream is null)
        {
            return Results.NotFound();
        }

        return Results.File(
            stream,
            string.IsNullOrWhiteSpace(document.ContentType) ? "application/octet-stream" : document.ContentType,
            document.FileName,
            enableRangeProcessing: true);
    }
}
