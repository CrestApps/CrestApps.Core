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

public static class DownloadAIDocument
{
    public const string DefaultRouteName = "DownloadAIDocument";

    /// <summary>
    /// Adds the shared AI document download endpoint used by citation links.
    /// </summary>
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

        var authorizationResult = await AuthorizeAsync(
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

    private static async Task<IResult> AuthorizeAsync(
        HttpContext httpContext,
        IAuthorizationService authorizationService,
        ICatalogManager<ChatInteraction> interactionManager,
        IAIChatSessionManager sessionManager,
        IAIProfileManager profileManager,
        AIDocument document)
    {
        switch (document.ReferenceType)
        {
            case AIReferenceTypes.Document.ChatInteraction:
                {
                    var interaction = await interactionManager.FindByIdAsync(document.ReferenceId);

                    if (interaction is null)
                    {
                        return Results.NotFound();
                    }

                    var authorization = await authorizationService.AuthorizeAsync(
                        httpContext.User,
                        interaction,
                        [AIChatDocumentOperations.ManageDocuments]);

                    return authorization.Succeeded ? null : CreateUnauthorizedResult(httpContext);
                }
            case AIReferenceTypes.Document.ChatSession:
                {
                    var session = await sessionManager.FindAsync(document.ReferenceId);

                    if (session is null)
                    {
                        return Results.NotFound();
                    }

                    var profile = await profileManager.FindByIdAsync(session.ProfileId);

                    if (profile is null)
                    {
                        return Results.NotFound();
                    }

                    var authorization = await authorizationService.AuthorizeAsync(
                        httpContext.User,
                        new AIChatSessionDocumentAuthorizationContext(profile, session),
                        [AIChatDocumentOperations.ManageDocuments]);

                    return authorization.Succeeded ? null : CreateUnauthorizedResult(httpContext);
                }
            default:
                return Results.NotFound();
        }
    }

    private static IResult CreateUnauthorizedResult(HttpContext httpContext)
    {
        return httpContext.User.Identity?.IsAuthenticated == true
            ? Results.Forbid()
            : Results.Challenge();
    }
}
