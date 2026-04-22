using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Filters;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CrestApps.Core.AI.Documents.Endpoints;

public static class RemoveChatInteractionDocument
{
    /// <summary>
    /// Adds the chat interaction document removal endpoint.
    /// </summary>
    /// <param name = "builder">The route builder.</param>
    /// <param name = "routeName">An optional route name for URL generation.</param>
    /// <returns>The route builder.</returns>
    public static IEndpointRouteBuilder AddRemoveChatInteractionDocumentEndpoint(this IEndpointRouteBuilder builder, string routeName = null)
    {
        var endpoint = builder.MapPost("ai/chat-interactions/remove-document", RemoveChatInteractionDocumentEndpoint.HandleAsync)
            .AddEndpointFilter<StoreCommitterEndpointFilter>()
            .DisableAntiforgery();
        if (!string.IsNullOrEmpty(routeName))
        {
            _ = endpoint.WithName(routeName);
        }

        return builder;
    }

    private sealed class RemoveChatInteractionDocumentEndpoint : AIChatDocumentEndpointBase
    {
        public static async Task<IResult> HandleAsync(
            [FromBody] RemoveDocumentRequest requestModel,
            HttpContext httpContext,
            [FromServices] ICatalogManager<ChatInteraction> interactionManager,
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

            var interaction = await interactionManager.FindByIdAsync(requestModel.ItemId);

            if (interaction == null)
            {
                return TypedResults.NotFound();
            }

            var authorization = await authorizationService.AuthorizeAsync(httpContext.User, interaction, [AIChatDocumentOperations.ManageDocuments]);

            if (!authorization.Succeeded)
            {
                return TypedResults.Forbid();
            }

            var document = await documentStore.FindByIdAsync(requestModel.DocumentId);
            var documentInfo = interaction.Documents?.FirstOrDefault(document => document.DocumentId == requestModel.DocumentId);

            if (documentInfo == null && document != null)
            {
                documentInfo = new ChatDocumentInfo
                {
                    DocumentId = document.ItemId,
                    FileName = document.FileName,
                    FileSize = document.FileSize,
                    ContentType = document.ContentType,
                };
            }

            if (documentInfo == null)
            {
                return TypedResults.NotFound("Document not found.");
            }

            if (interaction.Documents != null)
            {
                var attachedDocument = interaction.Documents.FirstOrDefault(existingDocument => existingDocument.DocumentId == requestModel.DocumentId);
                if (attachedDocument != null)
                {
                    interaction.Documents.Remove(attachedDocument);
                }
            }

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

            await interactionManager.UpdateAsync(interaction);

            var context = new AIChatDocumentRemoveContext
            {
                HttpContext = httpContext,
                Interaction = interaction,
                DocumentInfo = documentInfo,
                Document = document,
                ChunkIds = chunkIds,
                ReferenceId = interaction.ItemId,
                ReferenceType = AIReferenceTypes.Document.ChatInteraction,
            };

            await InvokeRemovedHandlersAsync(eventHandlers, context, httpContext.RequestAborted);

            return TypedResults.Ok(new
            {
                documents = interaction.Documents ?? [],
            });
        }
    }
}
