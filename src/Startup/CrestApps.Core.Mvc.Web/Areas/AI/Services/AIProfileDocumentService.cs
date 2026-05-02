using CrestApps.Core.AI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Mvc.Web.Areas.AI.Services;

public sealed class AIProfileDocumentService
{
    private readonly IAIDocumentStore _documentStore;
    private readonly IAIDocumentChunkStore _chunkStore;
    private readonly IDocumentFileStore _fileStore;
    private readonly IAIDocumentProcessingService _documentProcessingService;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIClientFactory _aiClientFactory;
    private readonly DefaultAIDocumentIndexingService _documentIndexingService;
    private readonly ILogger<AIProfileDocumentService> _logger;

    public AIProfileDocumentService(
        IAIDocumentStore documentStore,
        IAIDocumentChunkStore chunkStore,
        IDocumentFileStore fileStore,
        IAIDocumentProcessingService documentProcessingService,
        IAIDeploymentManager deploymentManager,
        IAIClientFactory aiClientFactory,
        DefaultAIDocumentIndexingService documentIndexingService,
        ILogger<AIProfileDocumentService> logger)
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

    public async Task UploadDocumentsAsync(AIProfile profile, IReadOnlyCollection<IFormFile> files, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(files);

        var embeddingGenerator = await CreateEmbeddingGeneratorAsync(profile);

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
                    profile.ItemId,
                    AIReferenceTypes.Document.Profile,
                    embeddingGenerator);

                if (!result.Success)
                {
                    _logger.LogWarning("Failed to process file '{FileName}': {Error}", file.FileName, result.Error);
                    continue;
                }

                var storageLocation = DocumentFileStoragePath.Create(
                    AIReferenceTypes.Document.Profile,
                    profile.ItemId,
                    file.FileName);

                using (var stream = file.OpenReadStream())
                {
                    await _fileStore.SaveFileAsync(storageLocation.StoragePath, stream);
                }

                result.Document.StoredFileName = storageLocation.StoredFileName;
                result.Document.StoredFilePath = storageLocation.StoragePath;

                await _documentStore.CreateAsync(result.Document, cancellationToken);

                foreach (var chunk in result.Chunks)
                {
                    await _chunkStore.CreateAsync(chunk, cancellationToken);
                }

                await _documentIndexingService.IndexAsync(result.Document, result.Chunks, cancellationToken);

                var documentsMetadata = profile.GetOrCreate<DocumentsMetadata>();
                documentsMetadata.Documents ??= [];
                documentsMetadata.Documents.Add(result.DocumentInfo);
                profile.Put(documentsMetadata);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing uploaded file '{FileName}'.", file.FileName);
            }
        }
    }

    public async Task RemoveDocumentsAsync(AIProfile profile, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(documentIds);

        var documentsMetadata = profile.GetOrCreate<DocumentsMetadata>();

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

                var document = await _documentStore.FindByIdAsync(documentId, cancellationToken);

                if (document != null)
                {
                    if (!string.IsNullOrWhiteSpace(document.StoredFilePath))
                    {
                        await _fileStore.DeleteFileAsync(document.StoredFilePath);
                    }

                    await _documentStore.DeleteAsync(document, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing document '{DocumentId}'.", documentId);
            }
        }

        profile.Put(documentsMetadata);
    }

    public Task RemoveAllDocumentsAsync(AIProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var documentIds = (profile.TryGet<DocumentsMetadata>(out var documentsMetadata)
                ? documentsMetadata.Documents ?? []
                : [])
            .Select(document => document.DocumentId)
            .Where(documentId => !string.IsNullOrWhiteSpace(documentId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return RemoveDocumentsAsync(profile, documentIds, cancellationToken);
    }

    private async Task<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(AIProfile profile)
    {
        var deployment = await ResolveEmbeddingDeploymentAsync(profile);

        if (deployment == null || string.IsNullOrWhiteSpace(deployment.ConnectionName))
        {
            return null;
        }

        return await _aiClientFactory.CreateEmbeddingGeneratorAsync(deployment);
    }

    private async Task<AIDeployment> ResolveEmbeddingDeploymentAsync(AIProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var profileDeployment = await ResolveProfileDeploymentAsync(profile);

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

    private async Task<AIDeployment> ResolveProfileDeploymentAsync(AIProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ChatDeploymentName))
        {
            var chatDeployment = await _deploymentManager.ResolveOrDefaultAsync(
                AIDeploymentType.Chat,
                deploymentName: profile.ChatDeploymentName);

            if (chatDeployment != null)
            {
                return chatDeployment;
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.UtilityDeploymentName))
        {
            var utilityDeployment = await _deploymentManager.ResolveOrDefaultAsync(
                AIDeploymentType.Utility,
                deploymentName: profile.UtilityDeploymentName);

            if (utilityDeployment != null)
            {
                return utilityDeployment;
            }
        }

        return null;
    }
}
