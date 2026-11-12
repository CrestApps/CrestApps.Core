using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Security;
using CrestApps.Core.Filters;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Endpoints;

/// <summary>
/// Provides functionality for upload Chat Session Document.
/// </summary>
public static class UploadChatSessionDocument
{
    /// <summary>
    /// Adds the chat session document upload endpoint.
    /// </summary>
    /// <param name="builder">The route builder.</param>
    /// <param name="routeName">An optional route name for URL generation.</param>
    /// <returns>The route handler builder of the endpoint, so a host can add its own endpoint filters or metadata.</returns>
    public static RouteHandlerBuilder AddUploadChatSessionDocumentEndpoint(this IEndpointRouteBuilder builder, string routeName = null)
    {
        // The antiforgery filter runs first, so a forged request from another site never reaches the handler. The
        // endpoint keeps DisableAntiforgery so the antiforgery middleware does not validate the request a second time;
        // the filter decides, and it only asks for a token from a request the browser authenticated on its own.
        var endpoint = builder.MapPost("ai/chat-sessions/upload-document", UploadChatSessionDocumentEndpoint.HandleAsync)
            .AddEndpointFilter<CookieAuthenticationAntiforgeryEndpointFilter>()
            .AddEndpointFilter<StoreCommitterEndpointFilter>()
            .DisableAntiforgery();
        if (!string.IsNullOrEmpty(routeName))
        {
            _ = endpoint.WithName(routeName);
        }

        return endpoint;
    }

    private sealed class UploadChatSessionDocumentEndpoint : AIChatDocumentEndpointBase
    {
        /// <summary>
        /// Handles the operation.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="sessionManager">The session manager.</param>
        /// <param name="profileManager">The profile manager.</param>
        /// <param name="deploymentManager">The deployment manager.</param>
        /// <param name="aiClientFactory">The ai client factory.</param>
        /// <param name="documentStore">The document store.</param>
        /// <param name="chunkStore">The chunk store.</param>
        /// <param name="fileStore">The file store.</param>
        /// <param name="documentProcessingService">The document processing service.</param>
        /// <param name="imageAnalysisService">The image analysis service.</param>
        /// <param name="authorizationService">The authorization service.</param>
        /// <param name="eventHandlers">The event handlers.</param>
        /// <param name="documentOptions">The document options.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <param name="localizerFactory">The localizer factory.</param>
        /// <param name="fileScanner">The uploaded file security scanner.</param>
        public static async Task<IResult> HandleAsync(
            HttpRequest request,
            [FromServices] IAIChatSessionManager sessionManager,
            [FromServices] IAIProfileManager profileManager,
            [FromServices] IAIDeploymentManager deploymentManager,
            [FromServices] IAIClientFactory aiClientFactory,
            [FromServices] IAIDocumentStore documentStore,
            [FromServices] IAIDocumentChunkStore chunkStore,
            [FromServices] IDocumentFileStore fileStore,
            [FromServices] IAIDocumentProcessingService documentProcessingService,
            [FromServices] IImageAnalysisService imageAnalysisService,
            [FromServices] TimeProvider timeProvider,
            [FromServices] IAuthorizationService authorizationService,
            [FromServices] IEnumerable<IAIChatDocumentEventHandler> eventHandlers,
            [FromServices] IOptions<ChatDocumentsOptions> documentOptions,
            [FromServices] ILoggerFactory loggerFactory,
            [FromServices] IStringLocalizerFactory localizerFactory,
            [FromServices] IUploadedFileScanner fileScanner)
        {
            var form = await request.ReadFormAsync();
            var lockedSessionId = form["sessionId"].ToString().Trim();

            if (string.IsNullOrEmpty(lockedSessionId))
            {
                return await UploadAsync();
            }

            // Uploads to one session run one at a time, and the last one commits before the next starts. Each reads the
            // session's document list, adds to it and saves it, so overlapping uploads would each save their own copy:
            // the document cap would be overshot, and the documents of all but the last would stay stored under the
            // session without being listed on it.
            using (await ChatSessionLocks.AcquireAsync(lockedSessionId, request.HttpContext.RequestAborted))
            {
                var uploaded = await UploadAsync();
                var committer = request.HttpContext.RequestServices.GetService<IStoreCommitter>();

                if (committer != null)
                {
                    await committer.CommitAsync(request.HttpContext.RequestAborted);
                }

                return uploaded;
            }

            async Task<IResult> UploadAsync()
            {
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

                var httpContext = request.HttpContext;
                var visitorIdentityResolver = httpContext.RequestServices.GetService<IAIVisitorIdentityResolver>();
                AIChatSession session = null;

                if (!string.IsNullOrWhiteSpace(sessionId))
                {
                    session = await sessionManager.FindAsync(sessionId);

                    // A session id that does not exist, or that belongs to someone else, is no session at all: the
                    // caller gets exactly what they would get without it, so the id says nothing about which exist.
                    if (session != null && !IsSessionOwner(session, httpContext.User, visitorIdentityResolver))
                    {
                        session = null;
                    }

                    if (session == null && string.IsNullOrWhiteSpace(profileId))
                    {
                        return TypedResults.NotFound();
                    }
                }

                var isNewSession = session == null;
                var profile = await profileManager.FindByIdAsync(isNewSession ? profileId : session.ProfileId);

                if (profile == null)
                {
                    return TypedResults.NotFound();
                }

                if (!IsSessionUploadEnabled(profile))
                {
                    return TypedResults.BadRequest("Session document or image uploads are not enabled for this AI profile.");
                }

                if (isNewSession)
                {
                    // Not saved yet: the caller is authorized, and counted by the session-start limiter, before the
                    // session exists. NewAsync stamps the owner, the signed-in user or the anonymous visitor.
                    session = await sessionManager.NewAsync(profile, new NewAIChatSessionContext());
                    session.Title = "Untitled";
                }

                var authorization = await authorizationService.AuthorizeAsync(
                    httpContext.User,
                    new AIChatSessionDocumentAuthorizationContext(profile, session),
                    [AIChatDocumentOperations.ManageDocuments]);
                if (!authorization.Succeeded)
                {
                    return TypedResults.Json(new { error = "You do not have permission to manage documents here." }, statusCode: StatusCodes.Status403Forbidden);
                }

                if (isNewSession)
                {
                    // An upload that starts a session starts one like the chat hub does, so it is limited like it.
                    var sessionStartRateLimiter = httpContext.RequestServices.GetService<IChatSessionStartRateLimiter>();

                    if (sessionStartRateLimiter != null)
                    {
                        var visitorIdentity = visitorIdentityResolver?.Resolve() ?? new AIVisitorIdentity();
                        var sessionStartLimit = await sessionStartRateLimiter.EvaluateAsync(new PromptSecurityContext
                        {
                            ProfileId = profile.ItemId,
                            Profile = profile,
                            User = httpContext.User,
                            VisitorId = visitorIdentity.VisitorId,
                            RemoteAddressHash = visitorIdentity.RemoteAddressHash,
                            RemoteAddress = visitorIdentity.RemoteAddress,
                        }, httpContext.RequestAborted);

                        if (sessionStartLimit.IsThrottled)
                        {
                            return TypedResults.Json(
                                new { error = "You've reached the limit for starting new chats. Please wait a few minutes and try again." },
                                statusCode: StatusCodes.Status429TooManyRequests);
                        }
                    }

                    await sessionManager.SaveAsync(session);
                }

                var deployment = await ResolveSessionDeploymentAsync(profile, deploymentManager);
                var embeddingDeployment = await deploymentManager.ResolveSlotAsync(AIDeploymentSlotNames.Embedding, clientName: deployment?.ClientName);

                using var usageScope = AIUsageScope.Begin(purpose: AIUsagePurposes.Indexing);
                var embeddingGenerator = embeddingDeployment == null ? null : await aiClientFactory.CreateEmbeddingGeneratorAsync(embeddingDeployment);

                profile.TryGet<AIProfileSessionDocumentsMetadata>(out var sessionDocMetadata);
                var visionDeployment = await deploymentManager.ResolveSlotAsync(AIDeploymentSlotNames.Vision);
                var allowVisionImages = sessionDocMetadata?.AllowSessionImageUploads == true && visionDeployment != null;
                var logger = loggerFactory.CreateLogger("AIChatDocumentEndpoints");
                var S = localizerFactory.Create(typeof(AIChatDocumentEndpointBase));
                session.Documents ??= [];
                var uploadedDocuments = new List<AIChatUploadedDocument>();
                var failedFiles = new List<object>();
                var maxDocumentsPerSession = documentOptions.Value.MaxDocumentsPerSession;

                foreach (var file in files)
                {
                    if (maxDocumentsPerSession > 0 && session.Documents.Count >= maxDocumentsPerSession)
                    {
                        failedFiles.Add(new
                        {
                            fileName = file.FileName,
                            error = S["A chat can hold at most {0} documents. Remove one before uploading another.", maxDocumentsPerSession].Value,
                        });
                        continue;
                    }

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
                        imageAnalysisService,
                        embeddingGenerator,
                        documentStore,
                        chunkStore,
                        fileStore,
                        fileScanner,
                        timeProvider,
                        allowVisionImages,
                        sessionDocMetadata?.AllowSessionDocuments == true,
                        visionDeployment?.Name,
                        // The profile's own ceiling when it set one; the site's otherwise.
                        profile?.TryGet<DocumentsMetadata>(out var documentsMetadata) == true ? documentsMetadata.MaxIndexableCharacters : null,
                        // The profile's choice about describing figures, which is the expensive
                        // half of ingestion and recurs across every session on that profile.
                        profile?.TryGet<DocumentsMetadata>(out var figureMetadata) == true ? figureMetadata.DescribeFiguresInUploads : null,
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
                    documents = session.Documents ?? [],
                    uploaded = uploadedDocuments.Select(document => document.DocumentInfo),
                    failed = failedFiles,
                });
            }
        }
    }
}
