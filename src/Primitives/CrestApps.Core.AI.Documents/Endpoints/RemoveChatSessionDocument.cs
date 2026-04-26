using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CrestApps.Core.AI.Documents.Endpoints;

/// <summary>
/// Provides functionality for remove Chat Session Document.
/// </summary>
public static class RemoveChatSessionDocument
{
    /// <summary>
    /// Adds the chat session document removal endpoint.
    /// </summary>
    /// <param name="builder">The route builder.</param>
    /// <param name="routeName">An optional route name for URL generation.</param>
    /// <returns>The route builder.</returns>
    public static IEndpointRouteBuilder AddRemoveChatSessionDocumentEndpoint(this IEndpointRouteBuilder builder, string routeName = null)
    {
        var endpoint = builder.MapPost("ai/chat-sessions/remove-document", RemoveChatSessionDocumentEndpoint.HandleAsync)
            .AddEndpointFilter<StoreCommitterEndpointFilter>()
            .DisableAntiforgery();
        if (!string.IsNullOrEmpty(routeName))
        {
            _ = endpoint.WithName(routeName);
        }

        return builder;
    }

    private sealed class RemoveChatSessionDocumentEndpoint : AIChatDocumentEndpointBase
    {
        /// <summary>
        /// Handles the operation.
        /// </summary>
        /// <param name="requestModel">The request model.</param>
        /// <param name="httpContext">The http context.</param>
        /// <param name="sessionManager">The session manager.</param>
        /// <param name="profileManager">The profile manager.</param>
        /// <param name="documentStore">The document store.</param>
        /// <param name="chunkStore">The chunk store.</param>
        /// <param name="fileStore">The file store.</param>
        /// <param name="authorizationService">The authorization service.</param>
        /// <param name="eventHandlers">The event handlers.</param>
        public static async Task<IResult> HandleAsync(
            [FromBody] RemoveDocumentRequest requestModel,
            HttpContext httpContext,
            [FromServices] IAIChatSessionManager sessionManager,
            [FromServices] IAIProfileManager profileManager,
            [FromServices] IAIDocumentStore documentStore,
            [FromServices] IAIDocumentChunkStore chunkStore,
            [FromServices] IDocumentFileStore fileStore,
            [FromServices] IAuthorizationService authorizationService,
            [FromServices] IEnumerable<IAIChatDocumentEventHandler> eventHandlers)
        {
            if (requestModel == null)
            {
                return TypedResults.BadRequest("Request body is required.");
            }

            if (string.IsNullOrWhiteSpace(requestModel.ItemId) || string.IsNullOrWhiteSpace(requestModel.DocumentId))
            {
                return TypedResults.BadRequest("Item ID and document ID are required.");
            }

            var session = await sessionManager.FindAsync(requestModel.ItemId);
            if (session == null)
            {
                return TypedResults.NotFound();
            }

            var profile = await profileManager.FindByIdAsync(session.ProfileId);
            if (profile == null)
            {
                return TypedResults.NotFound();
            }

            var authorization = await authorizationService.AuthorizeAsync(
                httpContext.User,
                new AIChatSessionDocumentAuthorizationContext(profile, session),
                [AIChatDocumentOperations.ManageDocuments]);
            if (!authorization.Succeeded)
            {
                return TypedResults.Forbid();
            }

            var documentInfo = session.Documents?.FirstOrDefault(document => document.DocumentId == requestModel.DocumentId);
            if (documentInfo == null)
            {
                return TypedResults.NotFound("Document not found.");
            }

            session.Documents.Remove(documentInfo);
            var document = await documentStore.FindByIdAsync(requestModel.DocumentId);
            var chunkIds = new List<string>();
            if (document != null)
            {
                var chunks = await chunkStore.GetChunksByAIDocumentIdAsync(document.ItemId);
                chunkIds = chunks.Select(chunk => chunk.ItemId).ToList();
                await chunkStore.DeleteByDocumentIdAsync(document.ItemId);
                if (!string.IsNullOrWhiteSpace(document.StoredFilePath))
                {
                    await fileStore.DeleteFileAsync(document.StoredFilePath);
                }

                await documentStore.DeleteAsync(document);
            }

            await sessionManager.SaveAsync(session);
            var context = new AIChatDocumentRemoveContext
            {
                HttpContext = httpContext,
                Session = session,
                Profile = profile,
                DocumentInfo = documentInfo,
                Document = document,
                ChunkIds = chunkIds,
                ReferenceId = session.SessionId,
                ReferenceType = AIReferenceTypes.Document.ChatSession,
            };
            await InvokeRemovedHandlersAsync(eventHandlers, context, httpContext.RequestAborted);

return TypedResults.Ok(new
            {
                documents = session.Documents ?? [],
            });
        }
    }
}
