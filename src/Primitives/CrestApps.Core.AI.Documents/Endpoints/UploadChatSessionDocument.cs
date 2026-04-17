using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Endpoints;

public static class UploadChatSessionDocument
{
    /// <summary>
    /// Adds the chat session document upload endpoint.
    /// </summary>
    /// <param name = "builder">The route builder.</param>
    /// <param name = "routeName">An optional route name for URL generation.</param>
    /// <returns>The route builder.</returns>
    public static IEndpointRouteBuilder AddUploadChatSessionDocumentEndpoint(this IEndpointRouteBuilder builder, string routeName = null)
    {
        var endpointHandler = new UploadChatSessionDocumentEndpoint();
        var endpoint = builder.MapPost("ai/chat-sessions/upload-document", endpointHandler.HandleAsync)
            .AddEndpointFilter<StoreCommitterEndpointFilter>()
            .DisableAntiforgery();
        if (!string.IsNullOrEmpty(routeName))
        {
            _ = endpoint.WithName(routeName);
        }

        return builder;
    }
    
    private sealed class UploadChatSessionDocumentEndpoint : AIChatDocumentEndpointBase
    {
        public async Task<IResult> HandleAsync(
            HttpRequest request,
            [FromServices] IAIChatSessionManager sessionManager,
            [FromServices] IAIProfileManager profileManager,
            [FromServices] IAIDeploymentManager deploymentManager,
            [FromServices] IAIClientFactory aiClientFactory,
            [FromServices] IAIDocumentStore documentStore,
            [FromServices] IAIDocumentChunkStore chunkStore,
            [FromServices] IDocumentFileStore fileStore,
            [FromServices] IAIDocumentProcessingService documentProcessingService,
            [FromServices] IAuthorizationService authorizationService,
            [FromServices] IEnumerable<IAIChatDocumentEventHandler> eventHandlers,
            [FromServices] IOptions<ChatDocumentsOptions> documentOptions,
            [FromServices] ILoggerFactory loggerFactory,
            [FromServices] IStringLocalizerFactory localizerFactory)
        {
            var form = await request.ReadFormAsync();
            var sessionId = form["sessionId"].ToString();
            var profileId = form["profileId"].ToString();
            var files = GetFiles(form);
            if (string.IsNullOrWhiteSpace(sessionId) && string.IsNullOrWhiteSpace(profileId))
            {
                return TypedResults.BadRequest("Session ID or profile ID is required.");
            }

            if (files.Count == 0)
            {
                return TypedResults.BadRequest("No files uploaded.");
            }

            AIChatSession session = null;
            AIProfile profile = null;
            var isNewSession = false;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                session = await sessionManager.FindAsync(sessionId);
                if (session == null)
                {
                    return TypedResults.NotFound();
                }

                profile = await profileManager.FindByIdAsync(session.ProfileId);
            }
            else
            {
                profile = await profileManager.FindByIdAsync(profileId);
                if (profile == null)
                {
                    return TypedResults.NotFound();
                }

                if (!IsSessionDocumentUploadEnabled(profile))
                {
                    return TypedResults.BadRequest("Session document uploads are not enabled for this AI profile.");
                }

                session = await sessionManager.NewAsync(profile, new NewAIChatSessionContext());
                session.Title = "Untitled";
                session.UserId = request.HttpContext.User.Identity?.Name;
                await sessionManager.SaveAsync(session);
                isNewSession = true;
            }

            if (profile == null)
            {
                return TypedResults.NotFound();
            }

            if (!IsSessionDocumentUploadEnabled(profile))
            {
                return TypedResults.BadRequest("Session document uploads are not enabled for this AI profile.");
            }

            var authorization = await authorizationService.AuthorizeAsync(
                request.HttpContext.User,
                new AIChatSessionDocumentAuthorizationContext(profile, session),
                [AIChatDocumentOperations.ManageDocuments]);
            if (!authorization.Succeeded)
            {
                return TypedResults.Forbid();
            }

            var deployment = await ResolveSessionDeploymentAsync(profile, deploymentManager);
            var embeddingDeployment = await deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.Embedding, clientName: deployment?.ClientName);
            var embeddingGenerator = embeddingDeployment == null ? null : await aiClientFactory.CreateEmbeddingGeneratorAsync(embeddingDeployment);
            var logger = loggerFactory.CreateLogger("AIChatDocumentEndpoints");
            var S = localizerFactory.Create(typeof(AIChatDocumentEndpointBase));
            session.Documents ??= [];
            var uploadedDocuments = new List<AIChatUploadedDocument>();
            var failedFiles = new List<object>();
            foreach (var file in files)
            {
                if (IsDuplicateDocument(session.Documents, file))
                {
                    failedFiles.Add(new
                    {
                        fileName = file.FileName,
                        error = S["This document is already attached. Remove the existing file before uploading it again."].Value,
                    });
                    continue;
                }

                var result = await ProcessFileAsync(
                    file,
                    session.SessionId,
                    AIReferenceTypes.Document.ChatSession,
                    documentOptions.Value,
                    documentProcessingService,
                    embeddingGenerator,
                    documentStore,
                    chunkStore,
                    fileStore,
                    logger,
                    S);
                if (!result.Success)
                {
                    failedFiles.Add(new { fileName = file.FileName, error = result.Error });
                    continue;
                }

                session.Documents.Add(result.UploadedDocument.DocumentInfo);
                uploadedDocuments.Add(result.UploadedDocument);
            }

            await sessionManager.SaveAsync(session);
            if (uploadedDocuments.Count > 0)
            {
                var context = new AIChatDocumentUploadContext
                {
                    HttpContext = request.HttpContext,
                    Session = session,
                    Profile = profile,
                    ReferenceId = session.SessionId,
                    ReferenceType = AIReferenceTypes.Document.ChatSession,
                    UploadedDocuments = uploadedDocuments,
                    IsNewSession = isNewSession,
                };
                foreach (var handler in eventHandlers)
                {
                    await handler.UploadedAsync(context, request.HttpContext.RequestAborted);
                }
            }

            return TypedResults.Ok(new
            {
                sessionId = session.SessionId,
                isNewSession,
                uploaded = uploadedDocuments.Select(document => document.DocumentInfo),
                failed = failedFiles,
            });
        }
    }
}
