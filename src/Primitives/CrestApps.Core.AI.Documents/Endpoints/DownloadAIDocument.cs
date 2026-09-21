using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

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
        [FromServices] IAIProfileManager profileManager,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(DownloadAIDocument).FullName);

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return Results.BadRequest();
        }

        var document = await documentStore.FindByIdAsync(documentId);

        // Each reason is reported separately. A bare 404 says only that the address did not resolve, which
        // is the same answer for a document that was never created, one whose row has not been committed
        // yet, and one whose bytes are missing — three different faults that need three different fixes.
        if (document is null)
        {
            logger.LogWarning(
                "Download of AI document '{DocumentId}' returned 404: no document with that id is readable. If it was created during the request that produced this link, its row may not be committed yet.",
                documentId);

            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(document.StoredFilePath))
        {
            logger.LogWarning(
                "Download of AI document '{DocumentId}' ('{FileName}') returned 404: the document record carries no stored file path.",
                documentId,
                document.FileName);

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
            logger.LogWarning(
                "Download of AI document '{DocumentId}' ('{FileName}') returned 404: no file exists at '{StoredFilePath}'.",
                documentId,
                document.FileName,
                document.StoredFilePath);

            return Results.NotFound();
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Serving AI document '{DocumentId}' ('{FileName}') as '{ContentType}', {FileSize} bytes.",
                documentId,
                document.FileName,
                document.ContentType,
                document.FileSize);
        }

        return Results.File(
            stream,
            string.IsNullOrWhiteSpace(document.ContentType) ? "application/octet-stream" : document.ContentType,
            document.FileName,
            enableRangeProcessing: true);
    }
}
