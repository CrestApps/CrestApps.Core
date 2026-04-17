using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Filters;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Endpoints;

public static class UploadChatInteractionDocument
{
    /// <summary>
    /// Adds the chat interaction document upload endpoint.
    /// </summary>
    /// <param name = "builder">The route builder.</param>
    /// <param name = "routeName">An optional route name for URL generation.</param>
    /// <returns>The route builder.</returns>
    public static IEndpointRouteBuilder AddUploadChatInteractionDocumentEndpoint(this IEndpointRouteBuilder builder, string routeName = null)
    {
        var endpoint = builder.MapPost("ai/chat-interactions/upload-document", UploadChatInteractionDocumentEndpoint.HandleAsync)
            .AddEndpointFilter<StoreCommitterEndpointFilter>()
            .DisableAntiforgery();
        if (!string.IsNullOrEmpty(routeName))
        {
            _ = endpoint.WithName(routeName);
        }

        return builder;
    }

    private sealed class UploadChatInteractionDocumentEndpoint : AIChatDocumentEndpointBase
    {
        public static async Task<IResult> HandleAsync(
            HttpRequest request,
            [FromServices] ICatalogManager<ChatInteraction> interactionManager,
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
            var interactionId = form["chatInteractionId"].ToString();
            var files = GetFiles(form);
            var logger = loggerFactory.CreateLogger("AIChatDocumentEndpoints");
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Chat interaction document upload started with {FileCount} file(s).", files.Count);
            }

            if (string.IsNullOrWhiteSpace(interactionId))
            {
                return TypedResults.BadRequest("Chat interaction ID is required.");
            }

            if (files.Count == 0)
            {
                return TypedResults.BadRequest("No files uploaded.");
            }

            var interaction = await interactionManager.FindByIdAsync(interactionId);
            if (interaction == null)
            {
                return TypedResults.NotFound();
            }

            var authorization = await authorizationService.AuthorizeAsync(request.HttpContext.User, interaction, [AIChatDocumentOperations.ManageDocuments]);
            if (!authorization.Succeeded)
            {
                return TypedResults.Forbid();
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Chat interaction document upload authorized for interaction '{InteractionId}'.", interaction.ItemId);
            }

            var deployment = await deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.Chat, deploymentName: interaction.ChatDeploymentName);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Resolved chat deployment '{DeploymentName}' for interaction '{InteractionId}'.", deployment?.Name, interaction.ItemId);
            }

            var embeddingDeployment = await deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.Embedding, clientName: deployment?.ClientName);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Resolved embedding deployment '{DeploymentName}' for interaction '{InteractionId}'.", embeddingDeployment?.Name, interaction.ItemId);
            }

            var embeddingGenerator = embeddingDeployment == null ? null : await aiClientFactory.CreateEmbeddingGeneratorAsync(embeddingDeployment);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Created embedding generator for interaction '{InteractionId}': {HasEmbeddingGenerator}.", interaction.ItemId, embeddingGenerator != null);
            }

            var S = localizerFactory.Create(typeof(AIChatDocumentEndpointBase));
            interaction.Documents ??= [];
            var uploadedDocuments = new List<AIChatUploadedDocument>();
            var failedFiles = new List<object>();
            foreach (var file in files)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Processing uploaded file '{FileName}' ({FileSize} bytes) for interaction '{InteractionId}'.", file.FileName, file.Length, interaction.ItemId);
                }

                if (IsDuplicateDocument(interaction.Documents, file))
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
                    interaction.ItemId,
                    AIReferenceTypes.Document.ChatInteraction,
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
                    logger.LogWarning("Document upload failed for file '{FileName}' in interaction '{InteractionId}': {Error}", file.FileName, interaction.ItemId, result.Error);
                    failedFiles.Add(new { fileName = file.FileName, error = result.Error });
                    continue;
                }

                interaction.Documents.Add(result.UploadedDocument.DocumentInfo);
                uploadedDocuments.Add(result.UploadedDocument);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Document upload processed successfully for file '{FileName}' in interaction '{InteractionId}'.", file.FileName, interaction.ItemId);
                }
            }

            await interactionManager.UpdateAsync(interaction);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Chat interaction '{InteractionId}' document metadata saved.", interaction.ItemId);
            }

            if (uploadedDocuments.Count > 0)
            {
                var context = new AIChatDocumentUploadContext
                {
                    HttpContext = request.HttpContext,
                    Interaction = interaction,
                    ReferenceId = interaction.ItemId,
                    ReferenceType = AIReferenceTypes.Document.ChatInteraction,
                    UploadedDocuments = uploadedDocuments,
                };
                foreach (var handler in eventHandlers)
                {
                    await handler.UploadedAsync(context, request.HttpContext.RequestAborted);
                }
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Chat interaction document upload completed for interaction '{InteractionId}' with {UploadedCount} successful file(s) and {FailedCount} failed file(s).",
                    interaction.ItemId,
                    uploadedDocuments.Count,
                    failedFiles.Count);
            }

            return TypedResults.Ok(new
            {
                uploaded = uploadedDocuments.Select(document => document.DocumentInfo),
                failed = failedFiles,
            });
        }
    }
}
