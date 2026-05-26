using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Endpoints;

/// <summary>
/// Represents the AI Chat Document Endpoint Base.
/// </summary>
public abstract class AIChatDocumentEndpointBase
{
    private const long DefaultMaxFileSizeBytes = 100 * 1024 * 1024; // 100 MB

    /// <summary>
    /// Processs file.
    /// </summary>
    protected static async Task<(bool Success, string Error, AIChatUploadedDocument UploadedDocument)> ProcessFileAsync(
        IFormFile file,
        string referenceId,
        string referenceType,
        ChatDocumentsOptions documentOptions,
        IAIDocumentProcessingService documentProcessingService,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IAIDocumentStore documentStore,
        IAIDocumentChunkStore chunkStore,
        IDocumentFileStore fileStore,
        TimeProvider timeProvider,
        bool allowVisionImages,
        ILogger logger,
        IStringLocalizer S)
    {
        if (file == null || file.Length == 0)
        {
            return (false, S["No file was uploaded."].Value, null);
        }

        if (file.Length > DefaultMaxFileSizeBytes)
        {
            return (false, S["The uploaded file exceeds the maximum allowed size of {0} MB.", DefaultMaxFileSizeBytes / (1024 * 1024)].Value, null);
        }

        var extension = Path.GetExtension(file.FileName);

        if (MediaTypeHelper.IsVisionImageExtension(extension) && !allowVisionImages)
        {
            return (false, S["Image uploads require a vision-capable chat deployment."].Value, null);
        }

        if (!documentOptions.IsAllowedFileExtension(extension, allowVisionImages))
        {
            return (false, S["File type '{0}' is not supported.", extension].Value, null);
        }

        if (fileStore == null)
        {
            logger.LogError("No IDocumentFileStore is registered for uploaded AI documents.");

            return (false, S["Failed to process file."].Value, null);
        }

        try
        {
            if (allowVisionImages && MediaTypeHelper.IsVisionImageExtension(extension))
            {
                return await ProcessVisionImageAsync(file, referenceId, referenceType, documentStore, fileStore, timeProvider);
            }

            var result = await documentProcessingService.ProcessFileAsync(file, referenceId, referenceType, embeddingGenerator);

            return await PersistProcessedDocumentAsync(file, referenceId, referenceType, result, documentStore, chunkStore, fileStore, logger, S);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process file {FileName}", file.FileName);

            return (false, S["Failed to process file."].Value, null);
        }
    }

    /// <summary>
    /// Determines whether the deployment supports vision uploads.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    protected static bool SupportsVisionUploads(AIDeployment deployment)
    {
        return deployment?.Capability.Supports(AIDeploymentCapability.Vision) == true;
    }

    /// <summary>
    /// Gets files.
    /// </summary>
    /// <param name="form">The form.</param>
    protected static IReadOnlyList<IFormFile> GetFiles(IFormCollection form)
    {
        var files = form.Files.GetFiles("files");
        if (files.Count > 0)
        {
            return files;
        }

        var singleFile = form.Files.GetFile("file");

        return singleFile == null ? [] : [singleFile];
    }

    /// <summary>
    /// Determines whether session document upload enabled.
    /// </summary>
    /// <param name="profile">The profile.</param>
    protected static bool IsSessionDocumentUploadEnabled(AIProfile profile)
    {
        return profile.TryGet<AIProfileSessionDocumentsMetadata>(out var sessionDocMetadata) && sessionDocMetadata.AllowSessionDocuments;
    }

    /// <summary>
    /// Resolves session deployment.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="deploymentManager">The deployment manager.</param>
    protected static async Task<AIDeployment> ResolveSessionDeploymentAsync(AIProfile profile, IAIDeploymentManager deploymentManager)
    {
        return await deploymentManager.ResolveOrDefaultAsync(AIDeploymentCapability.Chat, deploymentName: profile.ChatDeploymentName)
            ?? await deploymentManager.ResolveOrDefaultAsync(AIDeploymentCapability.Utility, deploymentName: profile.UtilityDeploymentName);
    }

    /// <summary>
    /// Determines whether duplicate document.
    /// </summary>
    /// <param name="documents">The documents.</param>
    /// <param name="file">The file.</param>
    protected static bool IsDuplicateDocument(ICollection<ChatDocumentInfo> documents, IFormFile file)
    {
        if (documents == null || file == null)
        {
            return false;
        }

        return documents.Any(document =>
            document != null &&
            string.Equals(document.FileName, file.FileName, StringComparison.OrdinalIgnoreCase) &&
            document.FileSize == file.Length);
    }

    /// <summary>
    /// Invokes removed handlers.
    /// </summary>
    /// <param name="eventHandlers">The event handlers.</param>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected static async Task InvokeRemovedHandlersAsync(IEnumerable<IAIChatDocumentEventHandler> eventHandlers, AIChatDocumentRemoveContext context, CancellationToken cancellationToken)
    {
        foreach (var handler in eventHandlers)
        {
            await handler.RemovedAsync(context, cancellationToken);
        }
    }

    private static async Task<(bool Success, string Error, AIChatUploadedDocument UploadedDocument)> PersistProcessedDocumentAsync(
        IFormFile file,
        string referenceId,
        string referenceType,
        DocumentProcessingResult result,
        IAIDocumentStore documentStore,
        IAIDocumentChunkStore chunkStore,
        IDocumentFileStore fileStore,
        ILogger logger,
        IStringLocalizer S)
    {
        if (result == null)
        {
            logger.LogError("Document processing returned no result for file {FileName}", file.FileName);

            return (false, S["Failed to process file."].Value, null);
        }

        if (!result.Success)
        {
            return (false, result.Error, null);
        }

        if (result.Document == null || result.DocumentInfo == null)
        {
            logger.LogError("Document processing returned an incomplete result for file {FileName}", file.FileName);

            return (false, S["Failed to process file."].Value, null);
        }

        var (storedFileName, storagePath) = DocumentFileStoragePath.Create(referenceType, referenceId, file.FileName);

        using (var stream = file.OpenReadStream())
        {
            await fileStore.SaveFileAsync(storagePath, stream);
        }

        result.Document.StoredFileName = storedFileName;
        result.Document.StoredFilePath = storagePath;

        await documentStore.CreateAsync(result.Document);

        foreach (var chunk in result.Chunks ?? [])
        {
            await chunkStore.CreateAsync(chunk);
        }

        return (true, null, new AIChatUploadedDocument
        {
            File = file,
            Document = result.Document,
            DocumentInfo = result.DocumentInfo,
            Chunks = result.Chunks ?? [],
        });
    }

    private static async Task<(bool Success, string Error, AIChatUploadedDocument UploadedDocument)> ProcessVisionImageAsync(
        IFormFile file,
        string referenceId,
        string referenceType,
        IAIDocumentStore documentStore,
        IDocumentFileStore fileStore,
        TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var contentType = MediaTypeHelper.InferMediaType(Path.GetExtension(file.FileName), file.ContentType);
        var document = new AIDocument
        {
            ItemId = UniqueId.GenerateId(),
            ReferenceId = referenceId,
            ReferenceType = referenceType,
            FileName = file.FileName,
            ContentType = contentType,
            FileSize = file.Length,
            UploadedUtc = now,
        };

        var (storedFileName, storagePath) = DocumentFileStoragePath.Create(referenceType, referenceId, file.FileName);

        using (var stream = file.OpenReadStream())
        {
            await fileStore.SaveFileAsync(storagePath, stream);
        }

        document.StoredFileName = storedFileName;
        document.StoredFilePath = storagePath;

        var documentInfo = new ChatDocumentInfo
        {
            DocumentId = document.ItemId,
            FileName = document.FileName,
            FileSize = document.FileSize,
            ContentType = document.ContentType,
        };

        await documentStore.CreateAsync(document);

        return (true, null, new AIChatUploadedDocument
        {
            File = file,
            Document = document,
            DocumentInfo = documentInfo,
        });
    }
}
