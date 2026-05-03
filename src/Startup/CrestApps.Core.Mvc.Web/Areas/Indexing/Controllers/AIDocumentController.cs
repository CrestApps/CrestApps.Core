using CrestApps.Core.AI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrestApps.Core.Mvc.Web.Areas.Indexing.Controllers;

[Area("Indexing")]
[Authorize(Policy = "Admin")]
[Route("[area]/[controller]")]
public sealed class AIDocumentController : Controller
{
    private readonly IAIDocumentStore _documentStore;
    private readonly IAIDocumentChunkStore _chunkStore;
    private readonly IAIProfileManager _profileManager;
    private readonly IDocumentFileStore _fileStore;
    private readonly IAIDocumentProcessingService _documentProcessingService;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIClientFactory _aiClientFactory;
    private readonly DefaultAIDocumentIndexingService _documentIndexingService;

    public AIDocumentController(
        IAIDocumentStore documentStore,
        IAIDocumentChunkStore chunkStore,
        IAIProfileManager profileManager,
        IDocumentFileStore fileStore,
        IAIDocumentProcessingService documentProcessingService,
        IAIDeploymentManager deploymentManager,
        IAIClientFactory aiClientFactory,
        DefaultAIDocumentIndexingService documentIndexingService)
    {
        _documentStore = documentStore;
        _chunkStore = chunkStore;
        _profileManager = profileManager;
        _fileStore = fileStore;
        _documentProcessingService = documentProcessingService;
        _deploymentManager = deploymentManager;
        _aiClientFactory = aiClientFactory;
        _documentIndexingService = documentIndexingService;
    }

    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(string profileId, IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file provided." });
        }

        var profile = await _profileManager.FindByIdAsync(profileId);

        if (profile == null)
        {
            return NotFound(new { error = "Profile not found." });
        }

        var embeddingDeployment = await _deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.Embedding);
        var embeddingGenerator = embeddingDeployment == null
            ? null
            : await _aiClientFactory.CreateEmbeddingGeneratorAsync(embeddingDeployment);
        var result = await _documentProcessingService.ProcessFileAsync(
            file,
            profileId,
            AIReferenceTypes.Document.Profile,
            embeddingGenerator);

        if (!result.Success)
        {
            return BadRequest(new { error = result.Error });
        }

        var storageLocation = DocumentFileStoragePath.Create(
            AIReferenceTypes.Document.Profile,
            profileId,
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

        await _documentIndexingService.IndexAsync(result.Document, result.Chunks);

        // Update the profile's document metadata.
        var documentsMetadata = profile.GetOrCreate<DocumentsMetadata>();
        documentsMetadata.Documents ??= [];
        documentsMetadata.Documents.Add(result.DocumentInfo);
        profile.Put(documentsMetadata);

        await _profileManager.UpdateAsync(profile);

        return Ok(new
        {
            id = result.Document.ItemId,
            fileName = result.Document.FileName,
            contentType = result.Document.ContentType,
            fileSize = result.Document.FileSize,
        });
    }

    [HttpPost("delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string profileId, string documentId)
    {
        var profile = await _profileManager.FindByIdAsync(profileId);

        if (profile == null)
        {
            return NotFound(new { error = "Profile not found." });
        }

        var document = await _documentStore.FindByIdAsync(documentId);

        if (document != null)
        {
            var chunks = await _chunkStore.GetChunksByAIDocumentIdAsync(documentId);
            await _documentIndexingService.DeleteChunksAsync(chunks.Select(chunk => chunk.ItemId));
            await _chunkStore.DeleteByDocumentIdAsync(documentId);
            if (!string.IsNullOrWhiteSpace(document.StoredFilePath))
            {
                await _fileStore.DeleteFileAsync(document.StoredFilePath);
            }
            await _documentStore.DeleteAsync(document);
        }

        // Remove from profile metadata.
        var documentsMetadata = profile.GetOrCreate<DocumentsMetadata>();
        documentsMetadata.Documents ??= [];
        documentsMetadata.Documents = documentsMetadata.Documents.Where(d => d.DocumentId != documentId).ToList();
        profile.Put(documentsMetadata);

        await _profileManager.UpdateAsync(profile);

        return Ok(new { success = true });
    }
}
