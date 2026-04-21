using CrestApps.Core.AI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.Areas.Indexing.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Blazor.Web.Areas.AI.Services;

public sealed class AIProfileTemplateDocumentService
{
    private readonly IAIDocumentStore _documentStore;
    private readonly IAIDocumentChunkStore _chunkStore;
    private readonly IDocumentFileStore _fileStore;
    private readonly IAIDocumentProcessingService _documentProcessingService;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIClientFactory _aiClientFactory;
    private readonly MvcAIDocumentIndexingService _documentIndexingService;
    private readonly ILogger<AIProfileTemplateDocumentService> _logger;

    public AIProfileTemplateDocumentService(
        IAIDocumentStore documentStore,
        IAIDocumentChunkStore chunkStore,
        IDocumentFileStore fileStore,
        IAIDocumentProcessingService documentProcessingService,
        IAIDeploymentManager deploymentManager,
        IAIClientFactory aiClientFactory,
        MvcAIDocumentIndexingService documentIndexingService,
        ILogger<AIProfileTemplateDocumentService> logger)
    {
        _documentStore = documentStore;
        _chunkStore = chunkStore;
        _fileStore = fileStore;
        _documentProcessingService = documentProcessingService;
        _deploymentManager = deploymentManager;
        _aiClientFactory = aiClientFactory;
        _documentIndexingService = documentIndexingService;
        _logger = logger;
    }

    public async Task UploadDocumentsAsync(AIProfileTemplate template, IReadOnlyCollection<IFormFile> files, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(files);

        var embeddingGenerator = await CreateEmbeddingGeneratorAsync(template);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file is null || file.Length == 0)
            {
                continue;
            }

            try
            {
                var result = await _documentProcessingService.ProcessFileAsync(
                    file,
                    template.ItemId,
                    AIReferenceTypes.Document.ProfileTemplate,
                    embeddingGenerator);

                if (!result.Success)
                {
                    _logger.LogWarning("Failed to process file '{FileName}': {Error}", file.FileName, result.Error);
                    continue;
                }

                var storageLocation = DocumentFileStoragePath.Create(
                    AIReferenceTypes.Document.ProfileTemplate,
                    template.ItemId,
                    file.FileName);

                using (var stream = file.OpenReadStream())
                {
                    await _fileStore.SaveFileAsync(storageLocation.StoragePath, stream);
                }

                result.Document.StoredFileName = storageLocation.StoredFileName;
                result.Document.StoredFilePath = storageLocation.StoragePath;

                await _documentStore.CreateAsync(result.Document);

                foreach (var chunk in result.Chunks)
                {
                    await _chunkStore.CreateAsync(chunk);
                }

                await _documentIndexingService.IndexAsync(result.Document, result.Chunks, cancellationToken);

                var documentsMetadata = template.GetOrCreate<DocumentsMetadata>();
                documentsMetadata.Documents ??= [];
                documentsMetadata.Documents.Add(result.DocumentInfo);
                template.Put(documentsMetadata);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing uploaded file '{FileName}'.", file.FileName);
            }
        }
    }

    public async Task RemoveDocumentsAsync(AIProfileTemplate template, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(documentIds);

        var documentsMetadata = template.GetOrCreate<DocumentsMetadata>();

        if (documentsMetadata?.Documents == null || documentsMetadata.Documents.Count == 0)
        {
            return;
        }

        foreach (var documentId in documentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(documentId))
            {
                continue;
            }

            try
            {
                var docInfo = documentsMetadata.Documents.FirstOrDefault(d =>
                    string.Equals(d.DocumentId, documentId, StringComparison.OrdinalIgnoreCase));

                if (docInfo == null)
                {
                    continue;
                }

                documentsMetadata.Documents.Remove(docInfo);

                var chunks = await _chunkStore.GetChunksByAIDocumentIdAsync(documentId);

                if (chunks.Count > 0)
                {
                    await _documentIndexingService.DeleteChunksAsync(chunks.Select(c => c.ItemId), cancellationToken);
                }

                await _chunkStore.DeleteByDocumentIdAsync(documentId);

                var document = await _documentStore.FindByIdAsync(documentId);

                if (document != null)
                {
                    if (!string.IsNullOrWhiteSpace(document.StoredFilePath))
                    {
                        await _fileStore.DeleteFileAsync(document.StoredFilePath);
                    }

                    await _documentStore.DeleteAsync(document);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing template document '{DocumentId}'.", documentId);
            }
        }

        template.Put(documentsMetadata);
    }

    public async Task CloneDocumentsToProfileAsync(AIProfileTemplate template, AIProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(profile);

        var templateDocuments = template.TryGet<DocumentsMetadata>(out var templateDocMetadata)
            ? templateDocMetadata.Documents
            : null;

        if (templateDocuments == null || templateDocuments.Count == 0)
        {
            return;
        }

        var profileDocuments = profile.GetOrCreate<DocumentsMetadata>();
        profileDocuments.Documents ??= [];

        foreach (var docInfo in templateDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(docInfo?.DocumentId))
            {
                continue;
            }

            var templateDocument = await _documentStore.FindByIdAsync(docInfo.DocumentId);

            if (templateDocument == null)
            {
                continue;
            }

            string storedFileName = null;
            string storedFilePath = null;

            if (!string.IsNullOrWhiteSpace(templateDocument.StoredFilePath))
            {
                var storageLocation = DocumentFileStoragePath.Create(
                    AIReferenceTypes.Document.Profile,
                    profile.ItemId,
                    templateDocument.FileName);

                await using var sourceStream = await _fileStore.GetFileAsync(templateDocument.StoredFilePath);

                if (sourceStream != null)
                {
                    await _fileStore.SaveFileAsync(storageLocation.StoragePath, sourceStream);
                    storedFileName = storageLocation.StoredFileName;
                    storedFilePath = storageLocation.StoragePath;
                }
                else
                {
                    _logger.LogWarning(
                        "Template document '{DocumentId}' referenced stored file '{StoredFilePath}', but the file was not found.",
                        templateDocument.ItemId,
                        templateDocument.StoredFilePath);
                }
            }

            var clonedDocument = new AIDocument
            {
                ItemId = UniqueId.GenerateId(),
                ReferenceId = profile.ItemId,
                ReferenceType = AIReferenceTypes.Document.Profile,
                FileName = templateDocument.FileName,
                StoredFileName = storedFileName,
                StoredFilePath = storedFilePath,
                ContentType = templateDocument.ContentType,
                FileSize = templateDocument.FileSize,
                UploadedUtc = templateDocument.UploadedUtc,
            };

            await _documentStore.CreateAsync(clonedDocument);

            var templateChunks = await _chunkStore.GetChunksByAIDocumentIdAsync(templateDocument.ItemId);
            var clonedChunks = new List<AIDocumentChunk>(templateChunks.Count);

            foreach (var templateChunk in templateChunks)
            {
                var clonedChunk = new AIDocumentChunk
                {
                    ItemId = UniqueId.GenerateId(),
                    AIDocumentId = clonedDocument.ItemId,
                    ReferenceId = profile.ItemId,
                    ReferenceType = AIReferenceTypes.Document.Profile,
                    Content = templateChunk.Content,
                    Embedding = templateChunk.Embedding,
                    Index = templateChunk.Index,
                };

                clonedChunks.Add(clonedChunk);
                await _chunkStore.CreateAsync(clonedChunk);
            }

            if (clonedChunks.Count > 0)
            {
                await _documentIndexingService.IndexAsync(clonedDocument, clonedChunks, cancellationToken);
            }

            profileDocuments.Documents.Add(new ChatDocumentInfo
            {
                DocumentId = clonedDocument.ItemId,
                FileName = clonedDocument.FileName,
                ContentType = clonedDocument.ContentType,
                FileSize = clonedDocument.FileSize,
            });
        }

        profile.Put(profileDocuments);
    }

    private async Task<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(AIProfileTemplate template)
    {
        var deployment = await ResolveEmbeddingDeploymentAsync(template);

        if (deployment == null || string.IsNullOrWhiteSpace(deployment.ConnectionName))
        {
            return null;
        }

        return await _aiClientFactory.CreateEmbeddingGeneratorAsync(deployment);
    }

    private async Task<AIDeployment> ResolveEmbeddingDeploymentAsync(AIProfileTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var profileDeployment = await ResolveTemplateDeploymentAsync(template);

        if (profileDeployment != null &&
            !string.IsNullOrWhiteSpace(profileDeployment.ClientName))
        {
            var scopedEmbeddingDeployment = await _deploymentManager.ResolveOrDefaultAsync(
                AIDeploymentType.Embedding,
                clientName: profileDeployment.ClientName);

            if (scopedEmbeddingDeployment != null)
            {
                return scopedEmbeddingDeployment;
            }
        }

        return await _deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.Embedding);
    }

    private async Task<AIDeployment> ResolveTemplateDeploymentAsync(AIProfileTemplate template)
    {
        template.TryGet<ProfileTemplateMetadata>(out var metadata);

        if (metadata is not null && !string.IsNullOrWhiteSpace(metadata.ChatDeploymentName))
        {
            var chatDeployment = await _deploymentManager.ResolveOrDefaultAsync(
                AIDeploymentType.Chat,
                deploymentName: metadata.ChatDeploymentName);

            if (chatDeployment != null)
            {
                return chatDeployment;
            }
        }

        if (!string.IsNullOrWhiteSpace(metadata?.UtilityDeploymentName))
        {
            var utilityDeployment = await _deploymentManager.ResolveOrDefaultAsync(
                AIDeploymentType.Utility,
                deploymentName: metadata.UtilityDeploymentName);

            if (utilityDeployment != null)
            {
                return utilityDeployment;
            }
        }

        return null;
    }
}
