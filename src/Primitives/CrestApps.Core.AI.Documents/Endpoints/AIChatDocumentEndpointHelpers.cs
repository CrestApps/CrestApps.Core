using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Endpoints;

public static partial class AIChatDocumentEndpoints
{
    private static async Task<(bool Success, string Error, AIChatUploadedDocument UploadedDocument)> ProcessFileAsync(
        IFormFile file,
        string referenceId,
        string referenceType,
        ChatDocumentsOptions documentOptions,
        IAIDocumentProcessingService documentProcessingService,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IAIDocumentStore documentStore,
        IAIDocumentChunkStore chunkStore,
        IDocumentFileStore fileStore,
        ILogger logger,
        IStringLocalizer S)
    {
        if (file == null || file.Length == 0)
        {
            return (false, S["No file was uploaded."].Value, null);
        }

        var extension = Path.GetExtension(file.FileName);
        if (!documentOptions.AllowedFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return (false, S["File type '{0}' is not supported.", extension].Value, null);
        }

        try
        {
            var result = await documentProcessingService.ProcessFileAsync(file, referenceId, referenceType, embeddingGenerator);
            if (!result.Success)
            {
                return (false, result.Error, null);
            }

            var storageLocation = DocumentFileStoragePath.Create(referenceType, referenceId, file.FileName);

            using (var stream = file.OpenReadStream())
            {
                await fileStore.SaveFileAsync(storageLocation.StoragePath, stream);
            }

            result.Document.StoredFileName = storageLocation.StoredFileName;
            result.Document.StoredFilePath = storageLocation.StoragePath;

            await documentStore.CreateAsync(result.Document);
            foreach (var chunk in result.Chunks)
            {
                await chunkStore.CreateAsync(chunk);
            }

            return (true, null, new AIChatUploadedDocument { File = file, Document = result.Document, DocumentInfo = result.DocumentInfo, Chunks = result.Chunks, });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process file {FileName}", file.FileName);
            return (false, S["Failed to process file."].Value, null);
        }
    }

    private static IReadOnlyList<IFormFile> GetFiles(IFormCollection form)
    {
        var files = form.Files.GetFiles("files");
        if (files.Count > 0)
        {
            return files;
        }

        var singleFile = form.Files.GetFile("file");
        return singleFile == null ? [] : [singleFile];
    }

    private static bool IsSessionDocumentUploadEnabled(AIProfile profile)
    {
        return profile.TryGet<AIProfileSessionDocumentsMetadata>(out var sessionDocMetadata) && sessionDocMetadata.AllowSessionDocuments;
    }

    private static async Task<AIDeployment> ResolveSessionDeploymentAsync(AIProfile profile, IAIDeploymentManager deploymentManager)
    {
        return await deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.Chat, deploymentName: profile.ChatDeploymentName) ?? await deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.Utility, deploymentName: profile.UtilityDeploymentName);
    }

    private static bool IsDuplicateDocument(ICollection<ChatDocumentInfo> documents, IFormFile file)
    {
        if (documents == null || file == null)
        {
            return false;
        }

        return documents.Any(document => document != null && string.Equals(document.FileName, file.FileName, StringComparison.OrdinalIgnoreCase) && document.FileSize == file.Length);
    }

    private static async Task InvokeRemovedHandlersAsync(IEnumerable<IAIChatDocumentEventHandler> eventHandlers, AIChatDocumentRemoveContext context, CancellationToken cancellationToken)
    {
        foreach (var handler in eventHandlers)
        {
            await handler.RemovedAsync(context, cancellationToken);
        }
    }
}
