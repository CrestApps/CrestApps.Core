using CrestApps.Core.AI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Blazor.Web.Areas.AI.Services;

/// <summary>
/// Handles document upload and removal for AI profiles in the Blazor Server application.
/// Uses <see cref="IServiceScopeFactory"/> to create isolated scopes for database operations,
/// preventing <see cref="ObjectDisposedException"/> when async work outlives the circuit's DI scope.
/// </summary>
public sealed class AIProfileDocumentService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AIProfileDocumentService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIProfileDocumentService"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory used to create isolated DI scopes.</param>
    /// <param name="logger">The logger instance.</param>
    public AIProfileDocumentService(
        IServiceScopeFactory scopeFactory,
        ILogger<AIProfileDocumentService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Uploads and processes documents for the specified AI profile.
    /// </summary>
    /// <param name="profile">The AI profile to attach documents to.</param>
    /// <param name="files">The collection of files to upload.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task UploadDocumentsAsync(AIProfile profile, IReadOnlyCollection<IFormFile> files, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(files);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var documentStore = scope.ServiceProvider.GetRequiredService<IAIDocumentStore>();
        var chunkStore = scope.ServiceProvider.GetRequiredService<IAIDocumentChunkStore>();
        var fileStore = scope.ServiceProvider.GetRequiredService<IDocumentFileStore>();
        var documentProcessingService = scope.ServiceProvider.GetRequiredService<IAIDocumentProcessingService>();
        var deploymentManager = scope.ServiceProvider.GetRequiredService<IAIDeploymentManager>();
        var aiClientFactory = scope.ServiceProvider.GetRequiredService<IAIClientFactory>();
        var documentIndexingService = scope.ServiceProvider.GetRequiredService<DefaultAIDocumentIndexingService>();

        var embeddingGenerator = await CreateEmbeddingGeneratorAsync(profile, deploymentManager, aiClientFactory);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file is null || file.Length == 0)
            {
                continue;
            }

            try
            {
                var result = await documentProcessingService.ProcessFileAsync(
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
                    await fileStore.SaveFileAsync(storageLocation.StoragePath, stream);
                }

                result.Document.StoredFileName = storageLocation.StoredFileName;
                result.Document.StoredFilePath = storageLocation.StoragePath;

                await documentStore.CreateAsync(result.Document, cancellationToken);

                foreach (var chunk in result.Chunks)
                {
                    await chunkStore.CreateAsync(chunk, cancellationToken);
                }

                await documentIndexingService.IndexAsync(result.Document, result.Chunks, cancellationToken);

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

    /// <summary>
    /// Removes the specified documents from the AI profile.
    /// </summary>
    /// <param name="profile">The AI profile to remove documents from.</param>
    /// <param name="documentIds">The IDs of documents to remove.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task RemoveDocumentsAsync(AIProfile profile, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(documentIds);

        var documentsMetadata = profile.GetOrCreate<DocumentsMetadata>();

        if (documentsMetadata?.Documents == null || documentsMetadata.Documents.Count == 0)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var documentStore = scope.ServiceProvider.GetRequiredService<IAIDocumentStore>();
        var chunkStore = scope.ServiceProvider.GetRequiredService<IAIDocumentChunkStore>();
        var fileStore = scope.ServiceProvider.GetRequiredService<IDocumentFileStore>();
        var documentIndexingService = scope.ServiceProvider.GetRequiredService<DefaultAIDocumentIndexingService>();

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

                var chunks = await chunkStore.GetChunksByAIDocumentIdAsync(documentId);

                if (chunks.Count > 0)
                {
                    await documentIndexingService.DeleteChunksAsync(chunks.Select(c => c.ItemId).ToArray(), cancellationToken);
                }

                await chunkStore.DeleteByDocumentIdAsync(documentId);

                var document = await documentStore.FindByIdAsync(documentId, cancellationToken);

                if (document != null)
                {
                    if (!string.IsNullOrWhiteSpace(document.StoredFilePath))
                    {
                        await fileStore.DeleteFileAsync(document.StoredFilePath);
                    }

                    await documentStore.DeleteAsync(document, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing document '{DocumentId}'.", documentId);
            }
        }

        profile.Put(documentsMetadata);
    }

    /// <summary>
    /// Removes all documents associated with the specified AI profile.
    /// </summary>
    /// <param name="profile">The AI profile to remove all documents from.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
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

    private static async Task<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(
        AIProfile profile,
        IAIDeploymentManager deploymentManager,
        IAIClientFactory aiClientFactory)
    {
        var deployment = await ResolveEmbeddingDeploymentAsync(profile, deploymentManager);

        if (deployment == null || string.IsNullOrWhiteSpace(deployment.ConnectionName))
        {
            return null;
        }

        return await aiClientFactory.CreateEmbeddingGeneratorAsync(deployment);
    }

    private static async Task<AIDeployment> ResolveEmbeddingDeploymentAsync(AIProfile profile, IAIDeploymentManager deploymentManager)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var profileDeployment = await ResolveProfileDeploymentAsync(profile, deploymentManager);

        if (profileDeployment != null &&
            !string.IsNullOrWhiteSpace(profileDeployment.ClientName))
        {
            var scopedEmbeddingDeployment = await deploymentManager.ResolveOrDefaultAsync(
                AIDeploymentType.Embedding,
                clientName: profileDeployment.ClientName);

            if (scopedEmbeddingDeployment != null)
            {
                return scopedEmbeddingDeployment;
            }
        }

        return await deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.Embedding);
    }

    private static async Task<AIDeployment> ResolveProfileDeploymentAsync(AIProfile profile, IAIDeploymentManager deploymentManager)
    {
        if (!string.IsNullOrWhiteSpace(profile.ChatDeploymentName))
        {
            var chatDeployment = await deploymentManager.ResolveOrDefaultAsync(
                AIDeploymentType.Chat,
                deploymentName: profile.ChatDeploymentName);

            if (chatDeployment != null)
            {
                return chatDeployment;
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.UtilityDeploymentName))
        {
            var utilityDeployment = await deploymentManager.ResolveOrDefaultAsync(
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
